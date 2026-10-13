using System;
using System.Collections.Generic;
using System.Linq;

namespace IronCurtain.Sim
{
    public enum AiTier { Simple, Hard }   // 2026-09-12 制作人两档:简单/困难(Normal 并入 Hard)

    /// <summary>
    /// AI(2026-09-12 制作人定稿·两档同一战术骨架):
    /// 共用脚本(制作人方案)——立足(占前线驻防)→攻势(集结推一路,必含重装/战士/射手)→
    /// 收复(失地优先:停攻分散夺回,全部收复再继续攻)。
    /// 档位差异仅三点:
    /// - 简单:不抽卡不海克斯不升级;驻防=前线每城1兵团+本方草坪箭塔2座各1兵团;**其余兵团只攻不防**
    ///   (无回防/威胁响应/受挫撤退),有多少攻多少(波次不限4将);
    /// - 困难:抽卡+海克斯+升级;驻防=前线每城2兵团(站稳脚跟);保留回防/威胁响应/受挫撤退;波次≤4将集结。
    /// 补兵两档都做(战斗力核心机制)。
    /// 防卡死五道保险(制作人担心点):①集结/阶段超时强制推进(40s);②驻防将可被波次抽走(非死锁资源);
    /// ③收复失败>2次的失地跳过标记;④一切等待有 else 出口;⑤心跳日志(LogHook,View 层订阅转 Debug.Log,
    /// Sim 不引 UnityEngine)。
    /// </summary>
    public static class AiSystem
    {
        const int DecisionInterval = 20;
        const int ReinforceInterval = 50;      // M8.5b:AI 自动补兵评估间隔(5s)——两档共用(战斗力核心)
        const int ReinforcePoolThreshold = 10;
        const int PhaseTimeoutTicks = 400;     // 保险①:阶段/集结超时(40s)强制推进
        const float GuardRadiusF = 7f;         // 驻防将留守半径(城旁多少米算驻防)
        const int RetakeFailLimit = 2;         // 保险③:同一失地夺回失败上限,超过跳过标记

        /// <summary>心跳/决策日志钩子(View 层订阅转 Debug.Log;Sim 层禁 UnityEngine)。</summary>
        public static Action<string> LogHook;

        enum AiPhase { Establish, Offensive, Reclaim }

        sealed class Wave
        {
            public int TargetId;
            public int StartCount;
            public List<int> Units = new List<int>();
            public bool IsRetake;
        }

        class AiState
        {
            public Side Side;
            public AiTier Tier;
            public AiPhase Phase = AiPhase.Establish;
            public long PhaseSince;
            public readonly List<Wave> Waves = new List<Wave>();
            public readonly HashSet<int> LostIds = new HashSet<int>();       // 失地(被夺走的原己方城)
            public readonly HashSet<int> SkipRetake = new HashSet<int>();    // 夺回失败超限,跳过
            public readonly Dictionary<int, int> RetakeFails = new Dictionary<int, int>();
            public readonly HashSet<int> OwnedSet = new HashSet<int>();      // 立足完成时的地盘快照
            public readonly Dictionary<int, float> BuildingHp = new Dictionary<int, float>();
            public readonly Dictionary<int, long> DefendCooldown = new Dictionary<int, long>();
            public long LastSeenTick = -1;      // 弱侧攻击:首个敌方建筑变已知的 tick(延迟2s基准)
            public long LastHeartbeat;

            public bool UseHexes => Tier == AiTier.Hard;          // 简单:不抽卡不升级
            public bool DefendReflex => Tier == AiTier.Hard;      // 简单:只攻不防
            public bool Retreat => Tier == AiTier.Hard;           // 简单:死战不撤
            public int WaveCap => Tier == AiTier.Hard ? 4 : int.MaxValue;   // 简单:有多少攻多少

            // 2026-09-13 制作人⑦⑧:试探虚实
            public ProbeState Probe;
            public long LastRecallTick = -600;   // ⑨:召回冷却
        }

        sealed class ProbeState
        {
            public int UnitId;          // 试探军团(将军)
            public int Lane;            // 试探的路
            public int MainLane;        // 主攻路
            public int MainTargetId;    // 主攻目标
            public int OriginBuildingId;// 试探出发城(虚→调其守军支援主攻)
            public long StartTick;
            public bool Resolved;
        }

        static readonly Dictionary<Side, AiState> _ais = new Dictionary<Side, AiState>();

        public static void Configure(SimCore sim, Side side, AiTier tier)
        {
            _ais[side] = new AiState { Side = side, Tier = tier, PhaseSince = sim.Tick };
        }

        public static void Reset() => _ais.Clear();

        /// <summary>
        /// 2026-09-14 制作人:"你不能把 AI 的状态机单独做吗?这样老是搞串还得修"。
        /// 这是**唯一**用来区分"AI 的调度"与"玩家的部队"的闸门:凡是只该给 AI 用的自动下令
        /// (如"产出后自动行军到集结点"),都必须先问这里,不允许无条件对双方生效。
        /// 注意:兵团自主行为层(视野索敌自动进攻/15m 支援/射手靠拢)**不**用这道闸门 ——
        /// 制作人明确"视野内出现敌人自动攻击是简化逻辑,玩家也一样",那一条是有意对双方生效的。
        /// </summary>
        public static bool IsAiControlled(Side side) => _ais.ContainsKey(side) && _ais[side] != null;

        /// <summary>调试快照(测试/排障用):指定方 AI 的阶段/失地/地盘/波次。 </summary>
        public static string DebugStateOf(Side side)
        {
            if (!_ais.TryGetValue(side, out var ai) || ai == null) return "no-ai";
            return "phase=" + ai.Phase + " lost=" + ai.LostIds.Count + " owned=" + ai.OwnedSet.Count
                + " waves=" + ai.Waves.Count + " tier=" + ai.Tier
                + " since=" + ai.PhaseSince;
        }

        public static void Tick(SimCore sim, float dt)
        {
            if (sim.Tick % ReinforceInterval == 0)
            {
                foreach (var ai in _ais.Values.ToList())
                {
                    if (sim.ReinforcePool[ai.Side] >= ReinforcePoolThreshold)
                        ReinforceSystem.RequestReinforce(sim, ai.Side, out _);
                }
            }

            if (sim.Tick % DecisionInterval != 0) return;
            foreach (var ai in _ais.Values.ToList())
                TickScripted(sim, ai);
        }

        // ---------- 主循环(两档共用骨架) ----------

        static void TickScripted(SimCore sim, AiState ai)
        {
            FillProduction(sim, ai);
            if (ai.UseHexes)
            {
                if (sim.EpOf(ai.Side) >= 2 && !sim.IsUpgraded(ai.Side, HeroId.ZhenYue))
                    sim.BuyUpgrade(ai.Side, HeroId.ZhenYue, out _);
                TryUseHeldHex(sim, ai);
                if (sim.Tick >= 1800 && HexSystem.CanDraw(sim, ai.Side))
                    TryDrawHex(sim, ai);
            }

            MaintainWaves(sim, ai);
            UpdateLost(sim, ai);
            GuardTopUp(sim, ai);   // 2026-09-12 制作人:简单档箭塔始终保持有军队(驻防点未满即补)

            switch (ai.Phase)
            {
                case AiPhase.Establish: StepEstablish(sim, ai); break;
                case AiPhase.Offensive: StepOffensive(sim, ai); break;
                case AiPhase.Reclaim: StepReclaim(sim, ai); break;
            }

            if (ai.DefendReflex)
            {
                ThreatRespond(sim, ai);
                ActiveDefend(sim, ai);
                SortieDefense(sim, ai);   // 2026-09-13 制作人②③④:出城迎击+战术阵型(射手随近战)
                UpdateProbe(sim, ai);      // ⑧:试探虚实分支
                EvaluateRecall(sim, ai);   // ⑨:压境召回战果差的进攻波
            }

            Heartbeat(sim, ai);
        }

        // ---------- 2026-09-13 制作人规则②③④:出城迎击 + 阵型 ----------

        /// <summary>驻军将军按英雄定位分类(与 BpSession.RoleOf 同口径)。</summary>
        static string RoleOfGeneral(Entity g) =>
            (g.Hero == HeroId.ZhuiRi || g.Hero == HeroId.XingLuo) ? "射手"
            : (g.Hero == HeroId.PanShi || g.Hero == HeroId.ZhenYue) ? "重装" : "战士";

        /// <summary>出城迎击(规则②,2026-09-13):敌方军队出现在建筑视野内 → 城内驻军士兵出城
        /// + 城门驻防将军列阵迎击。城里只有射手(士兵+驻防将全为射手)则不出城(规则⑤,依托射击留守)。
        /// 阵型(规则③):≥3 军团=重装前/战士中/射手后,不足 3=近战前/射手后;
        /// 城内士兵出城后:有主将→回兵团跟随(主将在阵型中),无主将→无将续战就近索敌。</summary>
        static void SortieDefense(SimCore sim, AiState ai)
        {
            foreach (var b in sim.Entities.Values)
            {
                if (b == null || b.Kind != EntityKind.Building || b.Side != ai.Side || b.Rubbled) continue;
                long cd;
                if (ai.DefendCooldown.TryGetValue(b.Id, out cd) && sim.Tick - cd < 300) continue;   // 城级 15s 防抖

                var bc = SimVec2.FromGrid(b.Pos);
                float vr = Math.Min(b.Building == BuildingId.Base ? 40f : b.Building == BuildingId.Tower ? 24f : 20f, 25f);
                var threat = sim.Entities.Values.FirstOrDefault(o =>
                    o != null && o.Kind == EntityKind.Unit && o.Side != ai.Side && o.Side != Side.Neutral
                    && !o.IsSoldier && o.State != EntityState.Dead && o.GarrisonBuildingId == 0
                    && VisionSystem.IsUnitVisible(sim, ai.Side, o.Id)
                    && SimVec2.Distance(o.PosF, bc) <= vr);
                if (threat == null) continue;

                // 迎击力量 = 城内驻将(占领进驻,交换力10/12) + 城门驻防将军(城周自由将)
                var inside = b.GarrisonFormal.Concat(b.GarrisonTemp)
                    .Select(sim.GetEntity).Where(u => u != null && !u.IsSoldier && u.Hero != null).ToList();
                var defenders = inside.Concat(sim.Entities.Values
                    .Where(e => e != null && e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == ai.Side
                                && e.State != EntityState.Dead && e.GarrisonBuildingId == 0
                                && !InAnyWave(ai, e.Id) && Dist(sim, e, b) <= GuardRadiusF))
                    .ToList();
                var garrisonSoldiers = b.GarrisonFormal.Concat(b.GarrisonTemp)
                    .Select(sim.GetEntity).Where(u => u != null && u.IsSoldier).ToList();
                // 规则⑤:城里只有射手(无近战驻防将+城内士兵全射手系) → 不出城
                bool meleeDefender = defenders.Any(u => RoleOfGeneral(u) != "射手");
                bool meleeSoldier = garrisonSoldiers.Any(u => !(u.Hero == HeroId.ZhuiRi || u.Hero == HeroId.XingLuo));
                if (!meleeDefender && !meleeSoldier) continue;
                ai.DefendCooldown[b.Id] = sim.Tick;

                // 城内驻将成建制出城(士兵随将)
                foreach (var g in inside)
                    GarrisonExitWithCompany(sim, b, g);
                // 剩余城内士兵出城:清登记,落城门附近错位(有将的回兵团跟随,无将的就地索敌)
                var gate = FrontCell(sim, b);
                foreach (var so in garrisonSoldiers)
                {
                    b.GarrisonFormal.Remove(so.Id); b.GarrisonTemp.Remove(so.Id);
                    if (gate != null)
                    {
                        var cell = new GridPos(gate.Value.Row + ((so.Id % 3) - 1), gate.Value.Col);
                        so.Pos = cell; so.PosF = SimVec2.FromGrid(cell);
                    }
                    so.GarrisonBuildingId = 0; so.TempGarrisonId = 0; so.State = EntityState.Idle; so.HasMoveTarget = false;
                }

                // 驻防将军列阵迎击(规则③④):射手永远在近战身后
                if (defenders.Count > 0)
                    FormBattleLine(sim, defenders, bc, new SimVec2(threat.PosF.X >= bc.X ? 1f : -1f, 0f), threat.PosF);
            }
        }

        /// <summary>把城内驻将与其士兵清出驻军(登记+落位城门)。城内驻将来自占领进驻(交换力 10/12,制作人设计)。</summary>
        static void GarrisonExitWithCompany(SimCore sim, Entity b, Entity g)
        {
            b.GarrisonFormal.Remove(g.Id); b.GarrisonTemp.Remove(g.Id);
            var gate = FrontCell(sim, b);
            if (gate != null) { g.Pos = gate.Value; g.PosF = SimVec2.FromGrid(gate.Value); }
            g.GarrisonBuildingId = 0; g.TempGarrisonId = 0; g.State = EntityState.Idle; g.HasMoveTarget = false;
            foreach (var id in b.GarrisonFormal.Concat(b.GarrisonTemp).ToList())
            {
                var so = sim.GetEntity(id);
                if (so == null || !so.IsSoldier || so.Hero != g.Hero) continue;
                b.GarrisonFormal.Remove(id); b.GarrisonTemp.Remove(id);
                if (gate != null)
                {
                    var cell = new GridPos(gate.Value.Row + ((id % 3) - 1), gate.Value.Col);
                    so.Pos = cell; so.PosF = SimVec2.FromGrid(cell);
                }
                so.GarrisonBuildingId = 0; so.TempGarrisonId = 0; so.State = EntityState.Idle; so.HasMoveTarget = false;
            }
        }

        /// <summary>战术列阵(规则③④):以城门为基点朝敌方向排纵深,重装最靠敌、射手最后;
        /// 军团不足三类时退化为 近战前/射手后。给各将军下移动令,到位 Idle 由索敌自动接战。</summary>
        static void FormBattleLine(SimCore sim, List<Entity> generals, SimVec2 anchor, SimVec2 dir, SimVec2 enemyPos)
        {
            var perp = new SimVec2(0f, 1f);
            bool three = generals.Select(RoleOfGeneral).Distinct().Count() >= 3;
            foreach (var g in generals)
            {
                string role = RoleOfGeneral(g);
                bool ranged = role == "射手";
                bool tank = role == "重装";
                // 纵深:三类阵=重装6/战士3/射手0.5;两类阵=近战5/射手0.5
                float depth = three ? (tank ? 6f : ranged ? 0.5f : 3f) : (ranged ? 0.5f : 5f);
                var row = generals.Where(x => RoleOfGeneral(x) == role).ToList();
                int idx = row.IndexOf(g), n = row.Count;
                float lateral = (idx - (n - 1) * 0.5f) * 2.5f;
                var spot = anchor + dir * depth + perp * lateral;
                var cell = spot.ToGrid();
                if (cell.Col < 0 || cell.Col >= LaneGrid.Cols || cell.Row < 0 || cell.Row >= LaneGrid.Rows) continue;
                if (!TerrainMap.IsInside(cell) || TerrainMap.IsWater(spot)) continue;
                sim.SetMoveTarget(g.Id, cell);
            }
        }

        // ---------- 阶段①:立足(占前线+驻防) ----------

        static void StepEstablish(SimCore sim, AiState ai)
        {
            var posts = GuardPosts(sim, ai);
            bool allGuarded = true;
            foreach (var post in posts)
            {
                int want = WantGuards(ai, post);
                int have = CountGuards(sim, ai, post, includeEnRoute: true);
                while (have < want)
                {
                    var g = TakeFreeGeneral(sim, ai, post);
                    if (g == null) { allGuarded = false; break; }
                    var front = FrontCell(sim, post);
                    if (front.HasValue) sim.SetMoveTarget(g.Id, front.Value);
                    have++;   // 派出即计(含赶路),防同 tick 重复派
                }
                if (have < want) allGuarded = false;
            }
            if ((posts.Count == 0 || allGuarded) || sim.Tick - ai.PhaseSince > PhaseTimeoutTicks)
            {
                // 立足完成(或超时保险①)→记录地盘快照,进入攻势
                ai.OwnedSet.Clear();
                foreach (var b in sim.Entities.Values)
                    if (b != null && b.Kind == EntityKind.Building && b.Side == ai.Side && !b.Rubbled)
                        ai.OwnedSet.Add(b.Id);
                ai.Phase = AiPhase.Offensive;
                ai.PhaseSince = sim.Tick;
            }
        }

        /// <summary>驻防点:本方三路各最前(朝敌方向最远)的己方建筑;简单档附加本方草坪箭塔 2 座。
        /// 前线城被拆/被占 → 该点自动消失(动态降级,else 出口)。</summary>
        static List<Entity> GuardPosts(SimCore sim, AiState ai)
        {
            var posts = new List<Entity>();
            var own = sim.Entities.Values
                .Where(e => e != null && e.Kind == EntityKind.Building && e.Side == ai.Side
                            && !e.Rubbled && e.Building != BuildingId.Base)
                .ToList();
            for (int lane = 0; lane < 3; lane++)
            {
                var inLane = own.Where(b => LaneIndexOf(SimVec2.FromGrid(b.Pos)) == lane).ToList();
                if (inLane.Count == 0) continue;
                // 朝敌最远 = 距基地最近敌方向 x 极值;用基地距离最远近似(三路几何下等价)
                var home = Home(sim, ai.Side);
                var front = inLane.OrderByDescending(b => home != null ? Dist(sim, b, home) : 0f).First();
                posts.Add(front);
            }
            if (ai.Tier == AiTier.Simple)
            {
                // 本方草坪箭塔(不在任何路带的己方 Tower,每方 2 座)
                foreach (var b in own)
                    if (b.Building == BuildingId.Tower && IntentFor(b) == SiegeIntent.Capture)
                        posts.Add(b);
            }
            if (posts.Count == 0)
            {
                // 2026-09-12 制作人:前线/二线全失守 → 完全收缩到第三线与大本营(Home 兜底为驻防点)
                var home = Home(sim, ai.Side);
                if (home != null) posts.Add(home);
            }
            return posts;
        }

        static int WantGuards(AiState ai, Entity post)
        {
            if (ai.Tier == AiTier.Hard) return 2;                        // 困难:前线每城 2 兵团
            bool lawn = post.Building == BuildingId.Tower && IntentFor(post) == SiegeIntent.Capture;
            return 1;                                                    // 简单:前线/草坪塔各 1
        }

        /// <summary>城旁 GuardRadiusF 内的本方将数;includeEnRoute 把"移动目标指向该城"的赶路将也计入。</summary>
        static int CountGuards(SimCore sim, AiState ai, Entity post, bool includeEnRoute)
        {
            int n = 0;
            var front = FrontCell(sim, post);
            foreach (var e in sim.Entities.Values)
            {
                if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier || e.Side != ai.Side) continue;
                if (e.State == EntityState.Dead || e.GarrisonBuildingId != 0) continue;
                if (InAnyWave(ai, e.Id)) continue;
                if (Dist(sim, e, post) <= GuardRadiusF) { n++; continue; }
                if (includeEnRoute && front.HasValue && e.HasMoveTarget && e.MoveTarget == front.Value) n++;
            }
            return n;
        }

        /// <summary>挑一个可调动的将:非波次、非驻防留守(距任一驻防点超半径)、离目标最近。</summary>
        static Entity TakeFreeGeneral(SimCore sim, AiState ai, Entity near)
        {
            var posts = GuardPosts(sim, ai);
            Entity best = null; float bd = float.MaxValue;
            foreach (var e in sim.Entities.Values)
            {
                if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier || e.Side != ai.Side) continue;
                if (e.State == EntityState.Dead || e.State == EntityState.Exchanging) continue;
                if (e.GarrisonBuildingId != 0 || InAnyWave(ai, e.Id)) continue;
                bool guardingOther = false;
                foreach (var p in posts)
                    if (p.Id != near.Id && Dist(sim, e, p) <= GuardRadiusF) { guardingOther = true; break; }
                if (guardingOther) continue;
                float d = Dist(sim, e, near);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        // ---------- 阶段②:攻势(集结推一路) ----------

        static void StepOffensive(SimCore sim, AiState ai)
        {
            if (ai.LostIds.Count > 0)
            {
                ai.Phase = AiPhase.Reclaim;
                ai.PhaseSince = sim.Tick;
                return;
            }
            bool attackUnderway = ai.Waves.Any(w => !w.IsRetake);
            if (attackUnderway) return;

            var pool = FreeGenerals(sim, ai).ToList();
            // 集结等待:人数不足且未超时 → 等;超时后强抽驻防将(保险②:驻防非死锁资源)
            if (pool.Count < 2)
            {
                if (sim.Tick - ai.PhaseSince < PhaseTimeoutTicks) return;
                pool = pool.Concat(PullFromGuards(sim, ai, 2 - pool.Count)).ToList();
            }

            var wave = PickComposition(pool, ai.WaveCap);
            if (wave.Count == 0) return;

            // 2026-09-12 制作人:两档偏好**弱侧**——视野之灵开视野后延迟 2s,对兵力最弱侧发动攻击
            int lane = WeakestLane(sim, ai);
            if (lane < 0) return;   // 无任何已知敌情(视野未开)
            var target = PickTargetInLane(sim, ai, lane);
            if (target == null) target = PickAnyTarget(sim, ai);
            if (target == null) return;

            var order = SiegeSystem.TryIssueRegimentOrder(sim, wave.Select(e => e.Id).ToList(),
                target.Id, ai.Side, IntentFor(target));
            if (order.Success)
            {
                ai.Waves.Add(new Wave { TargetId = target.Id, StartCount = wave.Count,
                    Units = wave.Select(e => e.Id).ToList(), IsRetake = false });
                ai.PhaseSince = sim.Tick;

                if (ai.Tier == AiTier.Hard)
                {
                    CommitLaneGuards(sim, ai, target, wave);   // ⑥:押上该路守军
                    LaunchProbe(sim, ai, lane, target.Id);     // ⑦:另两路随机挑一路试探虚实
                }
            }
        }

        /// <summary>规则⑥(困难):从某路进攻时,把该路的守军也押上——
        /// 城内驻军(成建制出城)+该路驻防点上的将军,全部并入主攻波(同目标同意图)。</summary>
        static void CommitLaneGuards(SimCore sim, AiState ai, Entity target, List<Entity> wave)
        {
            int lane = LaneIndexOf(SimVec2.FromGrid(target.Pos));
            var extra = new List<int>();
            // a) 目标路己方城内驻将(占领进驻)成建制出城押上;全射手城留守(⑤)
            foreach (var gb in sim.Entities.Values)
            {
                if (gb == null || gb.Kind != EntityKind.Building || gb.Side != ai.Side || gb.Rubbled) continue;
                if (LaneIndexOf(SimVec2.FromGrid(gb.Pos)) != lane) continue;
                var gens = gb.GarrisonFormal.Concat(gb.GarrisonTemp)
                    .Select(sim.GetEntity)
                    .Where(u => u != null && !u.IsSoldier && u.Hero != null).ToList();
                if (gens.Count == 0) continue;
                if (gens.All(u => RoleOfGeneral(u) == "射手")) continue;
                foreach (var g in gens) { GarrisonExitWithCompany(sim, gb, g); extra.Add(g.Id); }
            }
            // b) 该路驻防点上的自由守将
            // b) 该路驻防点上的自由守将(不进城的那种)
            foreach (var p in GuardPosts(sim, ai))
            {
                if (LaneIndexOf(SimVec2.FromGrid(p.Pos)) != lane) continue;
                foreach (var e in sim.Entities.Values)
                {
                    if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier || e.Side != ai.Side) continue;
                    if (e.State == EntityState.Dead || e.GarrisonBuildingId != 0) continue;
                    if (InAnyWave(ai, e.Id) || wave.Contains(e) || extra.Contains(e.Id)) continue;
                    if (Dist(sim, e, p) > GuardRadiusF) continue;
                    extra.Add(e.Id);
                }
            }
            if (extra.Count == 0) return;
            var order = SiegeSystem.TryIssueRegimentOrder(sim, extra, target.Id, ai.Side, IntentFor(target));
            if (order.Success)
            {
                var w = ai.Waves.FirstOrDefault(x => x.TargetId == target.Id && !x.IsRetake);
                if (w != null) w.Units.AddRange(extra);
            }
            else
            {
                // 兜底:正式令被拒 → 直接压向目标前排(索敌接战+贴身即占既有语义)
                var tf = FrontCell(sim, target);
                if (tf.HasValue) foreach (var id in extra) sim.SetMoveTarget(id, tf.Value);
                var w2 = ai.Waves.FirstOrDefault(x => x.TargetId == target.Id && !x.IsRetake);
                if (w2 != null) w2.Units.AddRange(extra);
            }
        }

        /// <summary>规则⑦(困难):发起主攻时,在另外两路随机挑一路派一支近战军团试探虚实。</summary>
        static void LaunchProbe(SimCore sim, AiState ai, int mainLane, int mainTargetId)
        {
            if (ai.Probe != null && !ai.Probe.Resolved) return;   // 上一支还没回来结论
            var others = new List<int> { 0, 1, 2 };
            others.Remove(mainLane);
            int lane = others[(int)((sim.Tick + (long)mainTargetId + 7) % 2)];   // 随机挑一路(确定性)
            var probe = FreeGenerals(sim, ai)
                .Where(e => RoleOfGeneral(e) != "射手")     // 试探用近战(射手不单独行动,规则④)
                .OrderBy(e => LaneDist(e, lane))
                .FirstOrDefault();
            if (probe == null) return;
            // 试探目的地:该路最前己方建筑与敌情之间(有已知敌建筑→其前排;否则己方该路最前建筑前 10m)
            var dest = PickTargetInLane(sim, ai, lane);
            GridPos? cell;
            if (dest != null) cell = FrontCell(sim, dest);
            else
            {
                var own = sim.Entities.Values
                    .Where(b => b != null && b.Kind == EntityKind.Building && b.Side == ai.Side && !b.Rubbled
                                && LaneIndexOf(SimVec2.FromGrid(b.Pos)) == lane)
                    .OrderBy(b => Dist(sim, b, Home(sim, ai.Side)))
                    .FirstOrDefault();
                cell = own != null ? FrontCell(sim, own) : (GridPos?)null;
            }
            if (!cell.HasValue) return;
            sim.SetMoveTarget(probe.Id, cell.Value);
            // 出发城 = 离试探者最近的己方建筑(虚→调它的守军支援主攻)
            var origin = sim.Entities.Values
                .Where(b => b != null && b.Kind == EntityKind.Building && b.Side == ai.Side && !b.Rubbled)
                .OrderBy(b => Dist(sim, b, probe))
                .FirstOrDefault();
            ai.Probe = new ProbeState { UnitId = probe.Id, Lane = lane, MainLane = mainLane,
                MainTargetId = mainTargetId, OriginBuildingId = origin != null ? origin.Id : 0,
                StartTick = sim.Tick };
        }

        static float LaneDist(Entity e, int lane) => Math.Abs(e.PosF.Y - TerrainMap.LaneY(e.PosF.X, lane));

        /// <summary>规则⑧(困难):试探结果分支。实(接敌/阵亡)→派"除主攻路驻军外"的兵攻那一路;
        /// 虚(25s 无接敌或抵达前线无敌)→维持主攻,并调试探出发城的守军支援主攻。</summary>
        static void UpdateProbe(SimCore sim, AiState ai)
        {
            var p = ai.Probe;
            if (p == null || p.Resolved) return;
            var u = sim.GetEntity(p.UnitId);
            bool contact = u != null && u.Hp < u.MaxHp;   // 掉血=接敌
            if (u == null || u.State == EntityState.Dead) contact = true;   // 阵亡=有敌
            bool timeout = sim.Tick - p.StartTick > 500;   // 25s
            if (!(contact || timeout)) return;
            p.Resolved = true;

            var mainTarget = sim.GetEntity(p.MainTargetId);
            if (contact)
            {
                // 实:另派兵攻试探路("除了进攻另一路的驻军"=不动主攻路的守军)
                var target = PickTargetInLane(sim, ai, p.Lane);
                if (target == null) return;
                var extra = new List<int>();
                foreach (var b2 in sim.Entities.Values)
                {
                    if (b2 == null || b2.Kind != EntityKind.Building || b2.Side != ai.Side || b2.Rubbled) continue;
                    if (LaneIndexOf(SimVec2.FromGrid(b2.Pos)) == p.MainLane) continue;
                    var gens = b2.GarrisonFormal.Concat(b2.GarrisonTemp)
                        .Select(sim.GetEntity).Where(x => x != null && !x.IsSoldier && x.Hero != null).ToList();
                    if (gens.Count == 0 || gens.All(x => RoleOfGeneral(x) == "射手")) continue;
                    foreach (var g in gens) { GarrisonExitWithCompany(sim, b2, g); extra.Add(g.Id); }
                }
                foreach (var e in sim.Entities.Values)
                {
                    if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier || e.Side != ai.Side) continue;
                    if (e.State == EntityState.Dead || e.GarrisonBuildingId != 0) continue;
                    if (InAnyWave(ai, e.Id) || extra.Contains(e.Id)) continue;
                    if (LaneIndexOf(e.PosF) == p.MainLane) continue;
                    if (RoleOfGeneral(e) == "射手" && !sim.Entities.Values.Any(x => x != null && x.Kind == EntityKind.Unit
                            && !x.IsSoldier && x.Side == ai.Side && !InAnyWave(ai, x.Id)
                            && RoleOfGeneral(x) != "射手" && LaneIndexOf(x.PosF) == LaneIndexOf(e.PosF)))
                        continue;
                    extra.Add(e.Id);
                }
                if (extra.Count == 0) return;
                var order = SiegeSystem.TryIssueRegimentOrder(sim, extra, target.Id, ai.Side, IntentFor(target));
                if (order.Success)
                    ai.Waves.Add(new Wave { TargetId = target.Id, StartCount = extra.Count,
                        Units = extra.ToList(), IsRetake = false });
                else
                {
                    var tf = FrontCell(sim, target);
                    if (tf.HasValue) foreach (var id in extra) sim.SetMoveTarget(id, tf.Value);
                }
            }
            else if (mainTarget != null && !mainTarget.Rubbled && mainTarget.Side != ai.Side)
            {
                // 虚:直接进攻已在途 → 调试探出发城的守军支援主攻
                var ob = sim.GetEntity(p.OriginBuildingId);
                if (ob == null || ob.Rubbled || ob.Side != ai.Side) return;
                var extra = new List<int>();
                foreach (var g in ob.GarrisonFormal.Concat(ob.GarrisonTemp)
                    .Select(sim.GetEntity).Where(x => x != null && !x.IsSoldier && x.Hero != null).ToList())
                { GarrisonExitWithCompany(sim, ob, g); extra.Add(g.Id); }
                foreach (var e in sim.Entities.Values)
                {
                    if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier || e.Side != ai.Side) continue;
                    if (e.State == EntityState.Dead || e.GarrisonBuildingId != 0) continue;
                    if (InAnyWave(ai, e.Id) || extra.Contains(e.Id)) continue;
                    if (Dist(sim, e, ob) <= GuardRadiusF) extra.Add(e.Id);
                }
                if (extra.Count == 0) return;
                var order = SiegeSystem.TryIssueRegimentOrder(sim, extra, mainTarget.Id, ai.Side, IntentFor(mainTarget));
                if (order.Success)
                {
                    var w = ai.Waves.FirstOrDefault(x => x.TargetId == mainTarget.Id && !x.IsRetake);
                    if (w != null) w.Units.AddRange(extra);
                }
            }
        }

        /// <summary>规则⑨(困难):某路被大军压境(可见敌将军≥6 且该路己方建筑在掉血)→
        /// 召回战果最差的进攻波(存活率最低)回防;所有进攻波都不利(存活率<0.5)→全数回防。</summary>
        static void EvaluateRecall(SimCore sim, AiState ai)
        {
            if (sim.Tick - ai.LastRecallTick < 600) return;   // 30s 冷却
            var attacks = ai.Waves.Where(w => !w.IsRetake).ToList();
            if (attacks.Count == 0) return;

            // 压境判定:任一路可见敌将军 ≥6 且该路有己方建筑掉血
            var enemyGens = sim.Entities.Values
                .Where(e => e != null && e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side != ai.Side
                            && e.Side != Side.Neutral && e.State != EntityState.Dead
                            && VisionSystem.IsUnitVisible(sim, ai.Side, e.Id))
                .ToList();
            bool pressured = false;
            for (int l = 0; l < 3 && !pressured; l++)
            {
                var inLane = enemyGens.Where(e => LaneIndexOf(e.PosF) == l).ToList();
                if (inLane.Count < 6) continue;
                var laneB = sim.Entities.Values
                    .Where(b => b != null && b.Kind == EntityKind.Building && b.Side == ai.Side
                                && !b.Rubbled && LaneIndexOf(SimVec2.FromGrid(b.Pos)) == l).ToList();
                foreach (var b in laneB)
                {
                    float hp;
                    if (ai.BuildingHp.TryGetValue(b.Id, out hp) && b.Hp < hp - 1f) { pressured = true; break; }
                }
            }
            if (!pressured) return;
            ai.LastRecallTick = sim.Tick;

            var home = Home(sim, ai.Side);
            var sorted = attacks.OrderBy(w => WaveStrength(sim, w)).ToList();
            if (sorted.All(w => WaveStrength(sim, w) < 0.5f))
            {
                foreach (var w in sorted) RecallWave(sim, ai, w, home);
            }
            else
            {
                RecallWave(sim, ai, sorted[0], home);   // 只召回战果最差的一路
            }
        }

        static float WaveStrength(SimCore sim, Wave w) =>
            w.StartCount == 0 ? 0f : w.Units.Count(id => sim.Entities.TryGetValue(id, out var e) && e.State != EntityState.Dead) / (float)w.StartCount;

        static void RecallWave(SimCore sim, AiState ai, Wave w, Entity home)
        {
            var front = home != null ? FrontCell(sim, home) : (GridPos?)null;
            foreach (var id in w.Units)
            {
                var e = sim.GetEntity(id);
                if (e == null) continue;
                e.TargetBuildingId = 0;   // 弃攻城令,回防
                if (front.HasValue) sim.SetMoveTarget(id, front.Value);
            }
            ai.Waves.Remove(w);
        }

        /// <summary>兵种结构(制作人:必含重装/战士/射手,哪类多一不管;缺类降级跳过)。</summary>
        static List<Entity> PickComposition(List<Entity> pool, int cap)
        {
            var result = new List<Entity>();
            Func<HeroId, bool> isTank = h => h == HeroId.PanShi || h == HeroId.ZhenYue;
            Func<HeroId, bool> isWarrior = h => h == HeroId.DuanFeng || h == HeroId.ZhuFeng;
            Func<HeroId, bool> isShooter = h => h == HeroId.ZhuiRi || h == HeroId.XingLuo;
            foreach (var pred in new[] { isTank, isWarrior, isShooter })
            {
                var pick = pool.FirstOrDefault(e => e.Hero.HasValue && pred(e.Hero.Value));
                if (pick != null) result.Add(pick);
            }
            foreach (var e in pool)
            {
                if (result.Count >= cap) break;
                if (!result.Contains(e)) result.Add(e);
            }
            return result;
        }

        /// <summary>弱侧(制作人 2026-09-12):三路中敌方兵力最弱者=该路可见敌单位数+已知驻军总和最少;
        /// 无数据的路不参与比较。首次出现已知敌情后延迟 20 ticks(2s)才放行;全无数据返回 -1。</summary>
        static int WeakestLane(SimCore sim, AiState ai)
        {
            bool anyKnown = false;
            foreach (var e in sim.Entities.Values)
                if (e != null && e.Kind == EntityKind.Building && e.Side != ai.Side && e.Side != Side.Neutral
                    && !e.Rubbled && VisionSystem.IsBuildingKnown(sim, ai.Side, e.Id))
                { anyKnown = true; break; }
            if (!anyKnown) return -1;
            if (ai.LastSeenTick < 0) ai.LastSeenTick = sim.Tick;    // 视野之灵首次开视野
            if (sim.Tick - ai.LastSeenTick < 20) return -1;        // 延迟 2s

            int best = -1; int bestPower = int.MaxValue;
            for (int lane = 0; lane < 3; lane++)
            {
                int power = 0; bool hasData = false;
                foreach (var e in sim.Entities.Values)
                {
                    if (e == null) continue;
                    if (e.Kind == EntityKind.Building && e.Side != ai.Side && e.Side != Side.Neutral && !e.Rubbled
                        && VisionSystem.IsBuildingKnown(sim, ai.Side, e.Id)
                        && LaneIndexOf(SimVec2.FromGrid(e.Pos)) == lane)
                    { power += VisionSystem.LastKnownGarrison(sim, ai.Side, e.Id) ?? 0; hasData = true; }
                    else if (e.Kind == EntityKind.Unit && e.Side != ai.Side && e.State != EntityState.Dead
                             && VisionSystem.IsUnitVisible(sim, ai.Side, e.Id)
                             && LaneIndexOf(e.PosF) == lane)
                    { power += 1; hasData = true; }
                }
                if (hasData && power < bestPower) { bestPower = power; best = lane; }
            }
            if (best >= 0) return best;
            return (int)(sim.Rng.NextUInt64() % 3);   // 全无侧向数据 → 随机
        }

        /// <summary>该路上最前的已知敌建筑(朝我方方向最近)。</summary>
        static Entity PickTargetInLane(SimCore sim, AiState ai, int lane)
        {
            var home = Home(sim, ai.Side);
            return sim.Entities.Values
                .Where(e => e != null && e.Kind == EntityKind.Building && e.Side != ai.Side && e.Side != Side.Neutral
                            && !e.Rubbled && e.Building != BuildingId.Base
                            && VisionSystem.IsBuildingKnown(sim, ai.Side, e.Id)
                            && LaneIndexOf(SimVec2.FromGrid(e.Pos)) == lane)
                .OrderBy(e => home != null ? Dist(sim, e, home) : 0f)
                .FirstOrDefault();
        }

        static Entity PickAnyTarget(SimCore sim, AiState ai)
        {
            var home = Home(sim, ai.Side);
            return sim.Entities.Values
                .Where(e => e != null && e.Kind == EntityKind.Building && e.Side != ai.Side && e.Side != Side.Neutral
                            && !e.Rubbled && e.Building != BuildingId.Base
                            && VisionSystem.IsBuildingKnown(sim, ai.Side, e.Id))
                .OrderBy(e => home != null ? Dist(sim, e, home) : 0f)
                .FirstOrDefault();
        }

        // ---------- 阶段③:收复(失地优先) ----------

        static void StepReclaim(SimCore sim, AiState ai)
        {
            if (ai.LostIds.Count == 0)
            {
                ai.Phase = AiPhase.Offensive;   // 全部收复 → 恢复攻势
                ai.PhaseSince = sim.Tick;
                return;
            }
            foreach (var lostId in ai.LostIds.ToList())
            {
                if (ai.SkipRetake.Contains(lostId)) continue;
                if (ai.Waves.Any(w => w.TargetId == lostId)) continue;   // 已有收复波在途
                var pool = ReclaimPool(sim, ai);
                if (pool.Count == 0) break;
                var squad = PickComposition(pool, 2);   // 每处失地 1-2 将,分散同时夺回
                if (squad.Count == 0) break;
                var order = SiegeSystem.TryIssueRegimentOrder(sim, squad.Select(e => e.Id).ToList(),
                    lostId, ai.Side, SiegeIntent.Capture);
                if (order.Success)
                    ai.Waves.Add(new Wave { TargetId = lostId, StartCount = squad.Count,
                        Units = squad.Select(e => e.Id).ToList(), IsRetake = true });
                else
                {
                    // 兜底(else 出口):正式攻城令被拒(如前排落点无解)→ 直接走位压过去,
                    // 靠既有索敌自动接战+无守军贴身即占(InstantCapture),失地仍可夺回
                    var lb = sim.GetEntity(lostId);
                    var lf = lb != null ? FrontCell(sim, lb) : (GridPos?)null;
                    if (lf.HasValue)
                        foreach (var g in squad) sim.SetMoveTarget(g.Id, lf.Value);
                }
            }
        }

        // ---------- 失地/波次维护 ----------

        static void UpdateLost(SimCore sim, AiState ai)
        {
            foreach (var id in ai.OwnedSet.ToList())
            {
                var b = sim.GetEntity(id);
                if (b == null || b.Rubbled) { ai.LostIds.Remove(id); continue; }   // 没了(废墟)不再追
                if (b.Side != ai.Side) ai.LostIds.Add(id);
                else
                {
                    if (ai.LostIds.Remove(id)) ai.RetakeFails.Remove(id);   // 收复了
                }
            }
        }

        static void MaintainWaves(SimCore sim, AiState ai)
        {
            foreach (var w in ai.Waves.ToList())
            {
                w.Units = w.Units.Where(id => sim.Entities.TryGetValue(id, out var e)
                    && e.State != EntityState.Dead && !e.IsSoldier).ToList();

                var target = sim.GetEntity(w.TargetId);
                bool done = target == null || target.Rubbled || target.Side == ai.Side;
                if (w.Units.Count == 0 || done)
                {
                    if (w.IsRetake && !done)
                    {
                        ai.RetakeFails.TryGetValue(w.TargetId, out var f);
                        ai.RetakeFails[w.TargetId] = f + 1;
                        if (f + 1 > RetakeFailLimit) ai.SkipRetake.Add(w.TargetId);   // 保险③
                    }
                    if (done && w.IsRetake) { /* 收复成功 */ }
                    ai.Waves.Remove(w);
                    continue;
                }
                if (ai.Retreat && !w.IsRetake && w.Units.Count < w.StartCount * 0.4f)
                {
                    var home = Home(sim, ai.Side);
                    var front = home != null ? FrontCell(sim, home) : (GridPos?)null;
                    foreach (var id in w.Units)
                        if (front.HasValue) sim.SetMoveTarget(id, front.Value);
                    ai.Waves.Remove(w);
                }
            }
        }

        static bool InAnyWave(AiState ai, int unitId) => ai.Waves.Any(w => w.Units.Contains(unitId));

        /// <summary>保险②:强抽驻防将(超时后进攻兵源不足时)——驻防是偏好不是死锁。
        /// 抽法:各驻防点只留 1 将,多出的可抽;全都单将时抽离目标最近的(城空了由威胁响应/后续产将回补)。</summary>
        static List<Entity> PullFromGuards(SimCore sim, AiState ai, int need)
        {
            var pulled = new List<Entity>();
            var posts = GuardPosts(sim, ai);
            // 先抽"超编"的(点内 ≥2 将)
            foreach (var p in posts)
            {
                var guards = sim.Entities.Values
                    .Where(e => e != null && e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == ai.Side
                                && e.State != EntityState.Dead && e.GarrisonBuildingId == 0
                                && !InAnyWave(ai, e.Id) && Dist(sim, e, p) <= GuardRadiusF)
                    .OrderByDescending(e => Dist(sim, e, p))
                    .ToList();
                for (int i = 1; i < guards.Count && pulled.Count < need; i++)   // 每点至少留 1
                    pulled.Add(guards[i]);
            }
            if (pulled.Count >= need) return pulled;
            // 仍不够:抽离"任一目标"最近的守将(全点单将的极端场景)
            foreach (var e in sim.Entities.Values)
            {
                if (pulled.Count >= need) break;
                if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier || e.Side != ai.Side) continue;
                if (e.State == EntityState.Dead || e.State == EntityState.Exchanging) continue;
                if (e.GarrisonBuildingId != 0 || InAnyWave(ai, e.Id) || pulled.Contains(e)) continue;
                bool isGuard = false;
                foreach (var p in posts)
                    if (Dist(sim, e, p) <= GuardRadiusF) { isGuard = true; break; }
                if (isGuard) pulled.Add(e);
            }
            return pulled;
        }

        /// <summary>驻防补员(2026-09-12 制作人:简单档箭塔始终保持有军队;两档通用):
        /// 驻防点人数未满 want → 派最近自由将去补(草坪塔参战抽空后自动回填)。</summary>
        static void GuardTopUp(SimCore sim, AiState ai)
        {
            foreach (var post in GuardPosts(sim, ai))
            {
                int want = WantGuards(ai, post);
                int have = CountGuards(sim, ai, post, includeEnRoute: true);
                while (have < want)
                {
                    var g = TakeFreeGeneral(sim, ai, post);
                    if (g == null) return;
                    var front = FrontCell(sim, post);
                    if (front.HasValue) sim.SetMoveTarget(g.Id, front.Value);
                    have++;
                }
            }
        }

        /// <summary>收复兵源(2026-09-12 制作人补例:简单档前线失守时,**草坪箭塔驻军要回防**——
        /// 纳入草坪塔驻将;前线城驻军不动)。困难档=常规自由将。</summary>
        static List<Entity> ReclaimPool(SimCore sim, AiState ai)
        {
            var pool = FreeGenerals(sim, ai).ToList();
            if (ai.Tier != AiTier.Simple) return pool;
            var posts = GuardPosts(sim, ai);
            foreach (var e in sim.Entities.Values)
            {
                if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier || e.Side != ai.Side) continue;
                if (e.State == EntityState.Dead || e.State == EntityState.Exchanging) continue;
                if (e.GarrisonBuildingId != 0 || InAnyWave(ai, e.Id) || pool.Contains(e)) continue;
                foreach (var p in posts)
                    if (Dist(sim, e, p) <= GuardRadiusF
                        && p.Building == BuildingId.Tower && IntentFor(p) == SiegeIntent.Capture)
                    { pool.Add(e); break; }   // 草坪塔驻将参战
            }
            return pool;
        }

        static IEnumerable<Entity> FreeGenerals(SimCore sim, AiState ai)
        {
            var posts = GuardPosts(sim, ai);
            foreach (var e in sim.Entities.Values)
            {
                if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier || e.Side != ai.Side) continue;
                if (e.State == EntityState.Dead || e.State == EntityState.Exchanging) continue;
                if (e.GarrisonBuildingId != 0 || InAnyWave(ai, e.Id)) continue;
                // 驻防留守将不抽(保险②的反面:驻防可抽的条件=该点超编;满编点保留)
                bool guarding = false;
                foreach (var p in posts)
                {
                    int want = WantGuards(ai, p);
                    // 已达 want 的点:留守的将算驻防不抽;未达的点将本来就该去驻防
                    if (CountGuards(sim, ai, p, includeEnRoute: true) >= want
                        && Dist(sim, e, p) <= GuardRadiusF) { guarding = true; break; }
                }
                if (guarding) continue;
                yield return e;
            }
        }

        // ---------- 海克斯(Hard) ----------

        static void TryDrawHex(SimCore sim, AiState ai)
        {
            var prio = new[] { HexId.SteelTide, HexId.RapidDeploy, HexId.HoldFast, HexId.Vengeance,
                               HexId.LightMobility, HexId.IronWall, HexId.HiddenPath };
            var draw = HexSystem.TryDraw(sim, ai.Side);
            if (!draw.Success) return;
            foreach (var c in prio)
            {
                if (c == HexId.Sandbag) continue;
                if (!draw.Choices.Contains(c)) continue;
                if (HexSystem.TryChoose(sim, ai.Side, c) != HexChooseError.None) return;
                TryUseHeldHex(sim, ai);
                return;
            }
        }

        static bool TryUseHeldHex(SimCore sim, AiState ai)
        {
            var prio = new[] { HexId.SteelTide, HexId.RapidDeploy, HexId.HoldFast, HexId.Vengeance,
                               HexId.LightMobility, HexId.IronWall, HexId.HiddenPath };
            var hand = HexSystem.HandOf(sim, ai.Side);
            foreach (var c in prio)
            {
                if (!hand.Contains(c)) continue;
                if (HexSystem.TryUseHeld(sim, ai.Side, c) == HexUseError.None) return true;
            }
            return false;
        }

        // ---------- 威胁响应(Hard;简单只攻不防) ----------

        const float ThreatRadius = 10f;
        const int ThreatEnemies = 3;
        const int DefendCooldownTicks = 150;
        const float GarrisonSoldierRange = 22f;
        const int GarrisonBatch = 8;

        static void ThreatRespond(SimCore sim, AiState ai)
        {
            var hp = ai.BuildingHp;
            foreach (var b in sim.Entities.Values)
            {
                if (b == null || b.Kind != EntityKind.Building || b.Side != ai.Side || b.Rubbled) continue;
                float last;
                hp.TryGetValue(b.Id, out last);
                hp[b.Id] = b.Hp;
                bool underAttack = b.Hp < last - 0.01f;

                int enemiesNear = 0;
                foreach (var e in sim.Entities.Values)
                {
                    if (e == null || e.Kind != EntityKind.Unit || e.Side == ai.Side || e.State == EntityState.Dead) continue;
                    if (!VisionSystem.IsUnitVisible(sim, ai.Side, e.Id)) continue;
                    if (Dist(sim, e, b) <= ThreatRadius) enemiesNear++;
                }
                if (!underAttack && enemiesNear < ThreatEnemies) continue;

                if (ai.DefendCooldown.TryGetValue(b.Id, out long cdTick) && sim.Tick - cdTick < DefendCooldownTicks) continue;
                ai.DefendCooldown[b.Id] = sim.Tick;

                if (b.GarrisonFormal.Count + b.GarrisonTemp.Count < 6)
                {
                    var center = SimVec2.FromGrid(b.Pos);
                    var candidates = new List<int>();
                    foreach (var e in sim.Entities.Values)
                    {
                        if (e == null || e.Kind != EntityKind.Unit || !e.IsSoldier || e.Side != ai.Side) continue;
                        if (e.State == EntityState.Dead || e.GarrisonBuildingId != 0) continue;
                        if (e.TargetBuildingId != 0) continue;
                        if (SimVec2.Distance(e.PosF, center) > GarrisonSoldierRange) continue;
                        candidates.Add(e.Id);
                        if (candidates.Count >= GarrisonBatch) break;
                    }
                    if (candidates.Count > 0)
                        GarrisonSystem.EnterTemp(sim, candidates, b.Id, out _);
                }

                var defenders = sim.Entities.Values
                    .Where(e => e != null && e.Kind == EntityKind.Unit && e.Side == ai.Side && !e.IsSoldier
                                && e.State == EntityState.Idle && e.GarrisonBuildingId == 0
                                && !InAnyWave(ai, e.Id))
                    .OrderBy(e => Dist(sim, e, b))
                    .Take(2).ToList();
                foreach (var d in defenders)
                {
                    var front = FrontCell(sim, b);
                    if (front.HasValue) sim.SetMoveTarget(d.Id, front.Value);
                }
            }
        }

        static void ActiveDefend(SimCore sim, AiState ai)
        {
            var visibleEnemies = sim.Entities.Values
                .Where(e => e.Kind == EntityKind.Unit && e.Side != ai.Side && e.State != EntityState.Dead
                            && VisionSystem.IsUnitVisible(sim, ai.Side, e.Id))
                .ToList();
            if (visibleEnemies.Count < 6) return;
            foreach (var b in sim.Entities.Values.Where(e => e.Side == ai.Side && e.Kind == EntityKind.Building && !e.Rubbled))
            {
                var near = visibleEnemies.Where(e => Dist(sim, e, b) <= 10f).ToList();
                if (near.Count < 6) continue;
                var idle = sim.Entities.Values
                    .Where(e => e.Kind == EntityKind.Unit && e.Side == ai.Side && !e.IsSoldier
                                && e.State == EntityState.Idle && e.GarrisonBuildingId == 0
                                && !InAnyWave(ai, e.Id))
                    .OrderBy(e => Dist(sim, e, b))
                    .Take(6).ToList();
                foreach (var u in idle)
                {
                    var t = FrontCell(sim, b);
                    if (t.HasValue) sim.SetMoveTarget(u.Id, t.Value);
                }
                return;
            }
        }

        // ---------- 心跳日志(保险⑤) ----------

        static void Heartbeat(SimCore sim, AiState ai)
        {
            if (sim.Tick - ai.LastHeartbeat < 300) return;
            ai.LastHeartbeat = sim.Tick;
            if (LogHook == null) return;
            int idleGenerals = 0;
            foreach (var e in sim.Entities.Values)
                if (e != null && e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == ai.Side
                    && e.State == EntityState.Idle && !InAnyWave(ai, e.Id))
                    idleGenerals++;
            LogHook("[ai] side=" + ai.Side + " tier=" + ai.Tier + " phase=" + ai.Phase
                + " waves=" + ai.Waves.Count + " idle=" + idleGenerals
                + " lost=" + ai.LostIds.Count + " skip=" + ai.SkipRetake.Count
                + " pop=" + sim.PopulationOf(ai.Side) + "/" + sim.PopulationCapOf(ai.Side));
        }

        // ---------- 公共 ----------

        /// <summary>产将(M8.4b):向己方主基地入队;名额未满且队列空才入队。</summary>
        static void FillProduction(SimCore sim, AiState ai)
        {
            if (sim.PopulationOf(ai.Side) >= sim.PopulationCapOf(ai.Side)) return;
            var b = Home(sim, ai.Side);
            if (b == null || b.Prod == null) return;
            for (int s = 0; s < b.Prod.Slots.Length; s++)
            {
                var slot = b.Prod.Slots[s];
                if (slot.Current != null || slot.Pending.Count > 0) return;
            }
            HeroId hero = PickProductionHero(sim, ai.Tier);
            ProductionSystem.Enqueue(sim, b.Id, hero, out _);
        }

        /// <summary>产将选择:sim 确定性 RNG。简单=三近战随机;困难=六英雄加权(近战×2/远程×1)。</summary>
        public static HeroId PickProductionHero(SimCore sim, AiTier tier)
        {
            if (tier == AiTier.Simple)
            {
                var pool = new[] { HeroId.ZhenYue, HeroId.PanShi, HeroId.DuanFeng };
                return pool[sim.Rng.NextUInt64() % (ulong)pool.Length];
            }
            var weighted = new[] {
                HeroId.ZhenYue, HeroId.PanShi, HeroId.DuanFeng,
                HeroId.ZhenYue, HeroId.PanShi, HeroId.DuanFeng,
                HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
            return weighted[sim.Rng.NextUInt64() % (ulong)weighted.Length];
        }

        static Entity Home(SimCore sim, Side side)
        {
            return sim.Entities.Values.FirstOrDefault(e => e.Side == side && e.Kind == EntityKind.Building
                                                        && e.Building == BuildingId.Base && !e.Rubbled);
        }

        /// <summary>进攻意图:路上的箭塔=摧毁;草坪箭塔(每方 2 座,不在任何路带)=占领。</summary>
        static SiegeIntent IntentFor(Entity b)
        {
            if (b.Building != BuildingId.Tower) return SiegeIntent.Capture;
            var p = SimVec2.FromGrid(b.Pos);
            for (int l = 0; l < 3; l++)
                if (Math.Abs(p.Y - TerrainMap.LaneY(p.X, l)) <= TerrainMap.LaneHalfWidth + 2f)
                    return SiegeIntent.Destroy;
            return SiegeIntent.Capture;
        }

        /// <summary>点最贴近哪条路(0上/1中/2下)。</summary>
        static int LaneIndexOf(SimVec2 p)
        {
            int best = 1; float bd = float.MaxValue;
            for (int l = 0; l < 3; l++)
            {
                float d = (float)Math.Abs(p.Y - TerrainMap.LaneY(p.X, l));
                if (d < bd) { bd = d; best = l; }
            }
            return best;
        }

        /// <summary>城门站位格:建筑朝敌侧(碰撞半径+0.6 米),椭圆内、非水域。</summary>
        static GridPos? FrontCell(SimCore sim, Entity b)
        {
            int dx = b.Side == Side.Blue ? 1 : -1;
            float offset = SimCore.BuildingCollisionRadius(b) + 0.6f;
            var gate = (SimVec2.FromGrid(b.Pos) + new SimVec2(dx * offset, 0f)).ToGrid();
            foreach (int r in new[] { 0, -1, 1, -2, 2, -3, 3 })
            {
                var p = new GridPos(gate.Row + r, gate.Col);
                if (p.Col < 0 || p.Col >= LaneGrid.Cols || p.Row < 0 || p.Row >= LaneGrid.Rows) continue;
                if (!TerrainMap.IsInside(p)) continue;
                if (TerrainMap.IsWater(SimVec2.FromGrid(p))) continue;
                return p;
            }
            return null;
        }

        /// <summary>实体间距离(米制欧氏):单位取 PosF,建筑取格中心。</summary>
        static float Dist(SimCore sim, Entity a, Entity b)
        {
            SimVec2 pa = a.Kind == EntityKind.Unit ? a.PosF : SimVec2.FromGrid(a.Pos);
            SimVec2 pb = b.Kind == EntityKind.Unit ? b.PosF : SimVec2.FromGrid(b.Pos);
            return SimVec2.Distance(pa, pb);
        }
    }
}
