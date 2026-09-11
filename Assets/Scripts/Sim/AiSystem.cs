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
            public long LastHeartbeat;

            public bool UseHexes => Tier == AiTier.Hard;          // 简单:不抽卡不升级
            public bool DefendReflex => Tier == AiTier.Hard;      // 简单:只攻不防
            public bool Retreat => Tier == AiTier.Hard;           // 简单:死战不撤
            public int WaveCap => Tier == AiTier.Hard ? 4 : int.MaxValue;   // 简单:有多少攻多少
        }

        static readonly Dictionary<Side, AiState> _ais = new Dictionary<Side, AiState>();

        public static void Configure(SimCore sim, Side side, AiTier tier)
        {
            _ais[side] = new AiState { Side = side, Tier = tier, PhaseSince = sim.Tick };
        }

        public static void Reset() => _ais.Clear();

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
            }

            Heartbeat(sim, ai);
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

            int lane = (int)(sim.Rng.NextUInt64() % 3);   // 制作人:随机选一路
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
            }
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
                var pool = FreeGenerals(sim, ai).ToList();
                if (pool.Count == 0) break;
                var squad = PickComposition(pool, 2);   // 每处失地 1-2 将,分散同时夺回
                if (squad.Count == 0) break;
                var order = SiegeSystem.TryIssueRegimentOrder(sim, squad.Select(e => e.Id).ToList(),
                    lostId, ai.Side, SiegeIntent.Capture);
                if (order.Success)
                    ai.Waves.Add(new Wave { TargetId = lostId, StartCount = squad.Count,
                        Units = squad.Select(e => e.Id).ToList(), IsRetake = true });
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
