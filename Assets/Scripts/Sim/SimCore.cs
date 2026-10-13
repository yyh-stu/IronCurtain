using System;
using System.Collections.Generic;

namespace IronCurtain.Sim
{
    public sealed class SimCore
    {
        public SimConfig Config { get; }
        public SeededRng Rng { get; }
        public long Tick { get; private set; }
        public int EntityCount => _entities.Count;
        public IReadOnlyDictionary<int, Entity> Entities => _entities;

        /// <summary>海克斯抽取/手牌/效果的对局级权威状态；不得放回 View 或静态系统。</summary>
        internal HexRuntimeState HexRuntime { get; } = new HexRuntimeState();

        /// <summary>将名额上限(M8.4b 名额制)。2026-09-13 制作人:全图三路下正常对局上限 8。</summary>
        public const int GeneralCap = 8;

        /// <summary>游戏数值配置(由调用方注入;null 时战斗模块不生效)。</summary>
        public GameConfig Game { get; set; }

        /// <summary>胜负:大本营被摧毁时置位。</summary>
        public Side? Winner { get; private set; }
        public void SetWinner(Side side) { Winner = side; }

        /// <summary>对局阶段(M6.4a):Normal → Overtime → Ended。加时 buff 读此状态。</summary>
        public MatchPhase Phase { get; set; } = MatchPhase.Normal;

        /// <summary>各方位阵亡主将数(M6.4a 积分平局链末级;士兵不计)。</summary>
        public readonly Dictionary<Side, int> LostGenerals = new Dictionary<Side, int> { { Side.Blue, 0 }, { Side.Red, 0 } };

        // 补兵池(M8.4a):按阵营的全局池
        public readonly Dictionary<Side, float> ReinforceAccum = new Dictionary<Side, float> { { Side.Blue, 0f }, { Side.Red, 0f } };
        public readonly Dictionary<Side, int> ReinforcePool = new Dictionary<Side, int> { { Side.Blue, 0 }, { Side.Red, 0 } };

        // 升级(M4):每方已购买的英雄被动
        private readonly Dictionary<Side, HashSet<HeroId>> _upgrades = new Dictionary<Side, HashSet<HeroId>>();

        public bool IsUpgraded(Side side, HeroId hero)
        {
            return _upgrades.TryGetValue(side, out var s) && s.Contains(hero);
        }

        /// <summary>2EP 购买升级,单英雄每局一次。</summary>
        public bool BuyUpgrade(Side side, HeroId hero, out string error)
        {
            error = null;
            if (IsUpgraded(side, hero)) { error = "该英雄已升级"; return false; }
            if (!SpendEp(side, Game.Ep.UpgradeCost)) { error = "EP 不足"; return false; }
            if (!_upgrades.TryGetValue(side, out var s))
            {
                s = new HashSet<HeroId>();
                _upgrades[side] = s;
            }
            s.Add(hero);
            return true;
        }

        // 经济(M2)
        private readonly Dictionary<Side, float> _ep = new Dictionary<Side, float>();
        private readonly Dictionary<Side, float> _epAccum = new Dictionary<Side, float>();
        private readonly Dictionary<Side, GridPos> _rally = new Dictionary<Side, GridPos>
        {
            // 三路地图(2026-09-11):默认集结点=中路前线桥西/东桥头(己方一侧)
            { Side.Blue, new GridPos(80, 128) },
            { Side.Red, new GridPos(80, 141) },
        };

        private readonly Dictionary<int, Entity> _entities = new Dictionary<int, Entity>();
        private readonly HashSet<int> _occupied = new HashSet<int>();
        private readonly Queue<ICommand> _commandQueue = new Queue<ICommand>();
        private readonly List<Action<SimEvent>> _listeners = new List<Action<SimEvent>>();
        private readonly LaneGrid _grid = new LaneGrid();
        private int _nextId = 1;

        public SimCore(SimConfig config, SeededRng rng)
        {
            Config = config;
            Rng = rng;
        }

        public Entity SpawnEntity(Side side, EntityKind kind, GridPos pos) => SpawnEntity(side, kind, pos, true);

        /// <summary>
        /// 生成实体。`announce=false` 表示**先不发 EntitySpawned**,由调用方把身份(IsSoldier/Hero/GeneralId)
        /// 与属性都配好之后再调 `AnnounceSpawn`。
        ///
        /// 2026-09-14 ★交付级修复:原来无条件"先发事件再返回",而 **View 就是以这个事件建视图的**
        /// (`BoardView.OnSimEvent → CreateView → EntityView.Bind`),于是:
        /// - `RegimentSystem.SpawnGeneral` 里 `g.Hero = hero` 落在事件**之后** → 建视图时 Hero 还是 null
        ///   → `Bind` 走 `BuildUnit(hero ?? HeroId.PanShi)` → **不管生产什么主将,建模一律是磐石**;
        /// - `SpawnSoldier` 里 `s.IsSoldier = true` 同样落在事件之后 → 建视图时 IsSoldier=false
        ///   → 士兵走**主将分支**、也用磐石模型,而且用主将尺寸 → 制作人说的"士兵和主将都是磐石、大小 1:1"。
        /// 这就是"什么将都出磐石"的真因(此前几轮探针都是**手工**建视图、绕过了事件路径,所以一直没抓到)。
        /// </summary>
        public Entity SpawnEntity(Side side, EntityKind kind, GridPos pos, bool announce)
        {
            if (!_grid.InBounds(pos))
                throw new InvalidOperationException($"位置越界: {pos}");
            int idx = _grid.ToIndex(pos);
            if (_occupied.Contains(idx))
                throw new InvalidOperationException($"格子已占用: {pos}");
            // 连续坐标下单位不再占格;出生防重叠用距离校验(驻军/城内单位不占位)
            if (kind == EntityKind.Unit)
            {
                var center = SimVec2.FromGrid(pos);
                foreach (var o in _entities.Values)
                    if (o.Kind == EntityKind.Unit && o.GarrisonBuildingId == 0
                        && o.State != EntityState.Dead
                        && SimVec2.Distance(o.PosF, center) < 0.5f)
                        throw new InvalidOperationException($"出生点过近: {pos}");
            }

            var e = new Entity(_nextId++, side, kind, pos, 0f);
            e.PosF = SimVec2.FromGrid(pos);
            _entities[e.Id] = e;
            if (kind == EntityKind.Building)
                _occupied.Add(idx);
            if (announce) AnnounceSpawn(e);
            return e;
        }

        /// <summary>声明一个实体已经配好、可以建视图了(见 SpawnEntity(announce) 的说明)。</summary>
        public void AnnounceSpawn(Entity e)
        {
            if (e != null) Emit(new SimEvent(Tick, SimEventType.EntitySpawned, e.Id));
        }

        public bool RemoveEntity(int id)
        {
            if (!_entities.TryGetValue(id, out var e)) return false;
            if (e.Kind == EntityKind.Building)
                _occupied.Remove(_grid.ToIndex(e.Pos));
            _entities.Remove(id);
            Emit(new SimEvent(Tick, SimEventType.EntityRemoved, id));
            return true;
        }

        public Entity GetEntity(int id) => _entities.TryGetValue(id, out var e) ? e : null;

        /// <summary>释放格位占用(驻军/入城交换等"离格但实体仍在"的场景)。</summary>
        public void FreeCell(GridPos pos)
        {
            _occupied.Remove(_grid.ToIndex(pos));
        }

        public void SubmitCommand(ICommand cmd) => _commandQueue.Enqueue(cmd);

        /// <summary>单位移动指令:目标越界/实体非单位时抛异常。既有两参签名视为手动命令。</summary>
        public void SetMoveTarget(int id, GridPos target) => SetMoveTarget(id, target, true);

        /// <summary>M8.5b 重载:manual=true(玩家/AI 手动)打断自动决策并写防复读字段;manual=false 为自动层专用。</summary>
        public void SetMoveTarget(int id, GridPos target, bool manual)
        {
            if (!_entities.TryGetValue(id, out var e))
                throw new InvalidOperationException($"实体不存在: {id}");
            if (e.Kind != EntityKind.Unit)
                throw new InvalidOperationException($"非线单位不可设置移动目标: {id}");
            if (!_grid.InBounds(target))
                throw new InvalidOperationException($"移动目标越界: {target}");

            e.AllyPushTicks = 0;    // 新指令:互推计数清零(窗口本身会自然过期)
            e.BestGoalDist = float.MaxValue;   // 新指令:停滞判定(历史最优目标距离)重新计
            if (manual && !e.IsSoldier)
            {
                if (e.AutoKind != 0) { e.SupKind = e.AutoKind; e.SupTargetId = e.AutoTargetId; }
                e.AutoKind = 0;
                e.AutoTargetId = 0;
                e.RetreatHold = false; // M8.7:玩家再下令=重新接管自动层(解除撤退压制)
            }

            e.MoveTarget = target;
            e.HasMoveTarget = true;
            e.State = EntityState.Moving;
        }

        public void Step()
        {
            Tick++;
            Emit(new SimEvent(Tick, SimEventType.StepBegin, 0));
            while (_commandQueue.Count > 0)
            {
                var cmd = _commandQueue.Dequeue();
                cmd.Execute(this);
                Emit(new SimEvent(Tick, SimEventType.CommandExecuted, 0));
            }
            MoveUnits();
            CombatUpdate();
            EconomyUpdate();
            SiegeSystem.Tick(this, 1f / Config.TickRate);
            TowerCombatSystem.Tick(this, 1f / Config.TickRate);
            GarrisonSystem.Tick(this, 1f / Config.TickRate);
            DestroySystem.Tick(this, 1f / Config.TickRate);
            InstantCaptureSystem.Tick(this, 1f / Config.TickRate); // 2026-09-11 制作人裁决:敌军贴建筑即占领(无交换)
            MatchClockSystem.Tick(this, 1f / Config.TickRate); // 对局时钟(M6.4a):时限/加时/积分判定
            HexSystem.Tick(this, 1f / Config.TickRate);
            ToolsSystem.Tick(this, 1f / Config.TickRate); // 堡垒脉冲/到期(2026-09-16 三十七轮自 HexSystem 拆出)
            VisionSystem.Tick(this, 1f / Config.TickRate);
            AiSystem.Tick(this, 1f / Config.TickRate);
            if (Config.AutoBehavior) RegimentBehaviorSystem.Tick(this, 1f / Config.TickRate); // M8.5b 兵团自主行为
            Emit(new SimEvent(Tick, SimEventType.StepEnd, 0));
        }

        public void AddEventListener(Action<SimEvent> listener) => _listeners.Add(listener);
        public void ClearEventListeners() => _listeners.Clear();

        // 2026-09-10 公共接口加法扩展(主编排授权):AudioSystem OnDisable 防域重载重复注册
        public void RemoveEventListener(System.Action<SimEvent> listener)
        {
            if (listener != null) _listeners.Remove(listener);
        }

        // ===== 经济与生产挂钩(M2;M8.4b 人口→名额制)=====

        /// <summary>生成建筑实体(归属方 owner)。中立建筑无血量;已方建筑按配置填数值。</summary>
        public Entity SpawnBuilding(Side owner, BuildingId type, GridPos pos)
        {
            var e = SpawnEntity(owner, EntityKind.Building, pos);
            e.Building = type;
            if (Game != null && owner != Side.Neutral)
            {
                var c = BuildingConfigOf(type);
                e.MaxHp = c.MaxHp;
                e.Hp = c.MaxHp;
                e.Attack = c.Attack;
                e.AttackIntervalSec = (float)c.AttackIntervalSec;
                e.Range = c.Range;
            }
            return e;
        }

        public BuildingCfg BuildingConfigOf(BuildingId id)
        {
            foreach (var b in Game.Buildings)
                if (b.Id == id) return b;
            throw new InvalidOperationException("建筑配置缺失: " + id);
        }

        /// <summary>某方拥有的资源点数量(按场上建筑实况计算)。</summary>
        public int OwnedResourcePointsOf(Side side)
        {
            int n = 0;
            foreach (var e in _entities.Values)
                if (e.Kind == EntityKind.Building && e.Building == BuildingId.ResourcePoint && e.Side == side)
                    n++;
            return n;
        }

        /// <summary>名额制(M8.4b):每个将恒占 1 名额。</summary>
        public int PopCostOf(HeroId id) => 1;

        /// <summary>已用名额 = 该方存活主将数(非士兵 Unit,名额制 M8.4b)。</summary>
        public int PopulationOf(Side side)
        {
            int used = 0;
            foreach (var e in _entities.Values)
                if (e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == side && e.State != EntityState.Dead)
                    used++;
            return used;
        }

        /// <summary>训练场名额上限覆盖(2026-09-13 制作人:训练场上限无限制;null=用 GeneralCap)。
        /// 由 View 层按模式设置,Sim 不感知 AppFlow。</summary>
        public int? PopulationCapOverride;

        /// <summary>名额上限 = (覆盖值 ?? GeneralCap 8) + 快速部署加成 + 加时 +1(GDD「人口+10」映射,M6.4a)。
        /// 快速部署接线(2026-09-16 三十七轮):+20% 四舍五入 —— 此前 PopCapBonusOf 无人调用,卡效果从未生效。</summary>
        public int PopulationCapOf(Side side) => (PopulationCapOverride ?? GeneralCap)
            + HexSystem.PopCapBonusOf(this, side)
            + (Phase == MatchPhase.Overtime ? 1 : 0);

        /// <summary>EP 上限覆盖(2026-09-14 制作人:"训练场 EP 无限")。设了值就把 EP **钉在**该值上
        /// —— 花掉立刻补满,等于无限;顶栏显示 X/X 一眼能看出是训练场特权。与 `PopulationCapOverride` 同款做法。</summary>
        public int? EpCapOverride;

        /// <summary>海克斯自由抽取(2026-09-17 制作人:"训练场海克斯抽卡不限时间不限次数")。
        /// true 时跳过"抽取窗口"与"节点已消耗"两道闸(EP 仍照扣 —— 训练场 EP 已钉 99,等于不限)。
        /// 由 View 层按模式设置,Sim 不感知 AppFlow,与上面两个覆盖同款做法。</summary>
        public bool HexFreeDraw;

        /// <summary>本局实际生效的 EP 上限(顶栏显示用同一个口径)。</summary>
        public int EpCapEffective => EpCapOverride ?? (Game != null && Game.Ep != null ? Game.Ep.Cap : 5);

        public float EpOf(Side side) => _ep.TryGetValue(side, out var v) ? v : 0f;

        public bool SpendEp(Side side, float amount)
        {
            float cur = EpOf(side);
            if (cur < amount) return false;
            _ep[side] = cur - amount;
            Emit(new SimEvent(Tick, SimEventType.EpChanged, (int)side, -amount));
            return true;
        }

        public GridPos RallyPointOf(Side side) => _rally[side];
        public void SetRallyPoint(Side side, GridPos p) { _rally[side] = p; }

        private void EconomyUpdate()
        {
            if (Game == null) return;
            float dt = 1f / Config.TickRate;

            foreach (Side s in new[] { Side.Blue, Side.Red })
            {
                if (EpCapOverride.HasValue) { _ep[s] = EpCapOverride.Value; continue; }   // 训练场:EP 无限(钉在上限)
                int pts = OwnedResourcePointsOf(s);
                if (pts <= 0) continue;
                float acc = _epAccum.TryGetValue(s, out var a) ? a : 0f;
                acc += pts * dt;
                float interval = (float)(Phase == MatchPhase.Overtime ? Game.Match.OvertimeEpInterval : Game.Ep.IntervalSeconds); // 加时 EP 60s(M6.4a)
                while (acc >= interval)
                {
                    acc -= interval;
                    float cur = EpOf(s);
                    if (cur < Game.Ep.Cap)
                    {
                        _ep[s] = cur + 1f;
                        Emit(new SimEvent(Tick, SimEventType.EpChanged, (int)s, 1f));
                    }
                }
                _epAccum[s] = acc;
            }

            ProductionSystem.Tick(this, dt);
            ReinforceSystem.Tick(this, dt);
        }

        private void MoveUnits()
        {
            float perTick = 1f / Config.TickRate;
            foreach (var e in _entities.Values)
            {
                // 常态士兵跟随主将；攻城士兵临时脱队，按自身命令目标移动入场。
                if (e.IsSoldier && e.TargetBuildingId == 0) continue;
                if (e.Kind != EntityKind.Unit || (e.State != EntityState.Moving && e.State != EntityState.Routing) || e.MoveSpeed <= 0f)
                    continue;
                if (e.GarrisonBuildingId != 0) continue; // 驻军单位不占线移动
                if (!e.HasMoveTarget) continue;          // 无目标的 Routing 原地待命

                // M8.5d 旁路点状态机:抵达(<0.8m)或超时(>45 tick)→ 清除,转回原目标
                if (e.Detour.HasValue)
                {
                    e.DetourTicks++;
                    if (SimVec2.Distance(e.PosF, e.Detour.Value) < NavigationAid.DetourClearDist
                        || e.DetourTicks > NavigationAid.DetourTimeoutTicks)
                    {
                        e.Detour = null;
                        e.DetourTicks = 0;
                    }
                }

                SimVec2 origTarget = SimVec2.FromGrid(e.MoveTarget); // 原目标(唯一抵达判定基准)
                SimVec2 target = e.Detour ?? origTarget;             // 有效目标(移动朝向)

                // 2026-09-14 卡死检测改口径(制作人:"士兵被卡住的时间太久了,似乎要很久才能过去"):
                // 旧判据是"本 tick 位移 < 期望步长×0.25" → **贴着建筑边滑行**的单位每 tick 都有微小位移,
                // 于是永远判不出卡死、绕行永远不触发(实测 NavigationTests.T4 复现:推着建筑边滑 1200 tick)。
                // 新判据看**有没有向目标靠近**:目标距离不再刷新历史最优 → 停滞计数;靠得近了就清零。
                float spd = MoveSpeedOf(e);
                float goalDist = (origTarget - e.PosF).Magnitude;
                if (e.BestGoalDist == float.MaxValue || goalDist < e.BestGoalDist - 0.05f)
                {
                    e.BestGoalDist = System.Math.Min(goalDist, e.BestGoalDist);
                    e.StuckTicks = 0;
                }
                else e.StuckTicks++;
                e.PrevPosF = e.PosF; // 采样须在检查之后(与士兵分支同序)
                if (e.StuckTicks >= NavigationAid.StuckThresholdTicks && !e.Detour.HasValue)
                {
                    e.Detour = NavigationAid.ComputeDetour(this, e, target);
                    e.StuckTicks = 0;
                }

                SimVec2 delta = origTarget - e.PosF;
                if (delta.Magnitude <= 0.15f)
                {
                    e.HasMoveTarget = false;
                    e.State = EntityState.Idle;
                    e.MoveAccum = 0f;
                    e.Detour = null;
                    e.DetourTicks = 0;
                    e.PrevPosF = e.PosF;
                    e.BestGoalDist = float.MaxValue;   // 抵达:停滞判定重置
                    continue;
                }

                delta = target - e.PosF;
                float dist = delta.Magnitude;
                float step = System.Math.Min(spd * perTick, dist);
                // 2026-09-14 制作人「多兵团交战时后续支援会被友军卡住」:走动方向先过**同阵营局部绕行** ——
                // 前方有己方单位(非同团)就把朝向往更空的一侧偏,走过去而不是顶上去。
                var moveDir = SteerAroundAllies(e, delta.Normalized());
                var nextPos = TerrainMap.ClampToEllipse(e.PosF + moveDir * step);
                // 三路城墙(2026-09-11):敌方完好段不可穿;被挡则原地等待(绕行/门洞由行为层收敛)
                // 2026-09-14:改用带起点的判定 → 被挤进薄壳里的单位也能向外走出来(否则永久卡死)
                e.PosF = WallSystem.CanPass(e.Side, e.PosF, nextPos) ? nextPos : e.PosF;
                PushOutOfBuildings(e);
                e.PosF = TerrainMap.ClampToEllipse(e.PosF);
                e.Pos = e.PosF.ToGrid();

                e.MoveAccum += step;
                if (e.MoveAccum >= 1f)
                {
                    e.MoveAccum = 0f;
                    Emit(new SimEvent(Tick, SimEventType.UnitAdvanced, e.Id)); // 节流:约每 1 米一次
                }

                if ((origTarget - e.PosF).Magnitude <= 0.15f)
                {
                    e.HasMoveTarget = false;
                    e.State = EntityState.Idle;
                    e.MoveAccum = 0f;
                    e.Detour = null;
                    e.DetourTicks = 0;
                }
            }
            RegimentSystem.Tick(this, perTick); // 兵团底座(M8.2a):阵型跟随/无将续战,先于软碰撞
            SoftCollide();
        }

        private float MoveSpeedOf(Entity e)
        {
            float spd = e.MoveSpeed * e.Status.MoveMultiplier;
            if (e.IsCharging && e.Hero == HeroId.ZhuFeng && Game != null)
                spd = (float)ChargeSpeedOf(HeroId.ZhuFeng);
            if (e.Hero == HeroId.ZhenYue && Tick >= 1800) // 3 分钟后移速衰减(D11 默认 1.4)
                spd = IsUpgraded(e.Side, HeroId.ZhenYue) ? 2.2f : 1.4f;
            spd *= HexSystem.MoveMultiplierOf(this, e); // 海克斯移速层
            spd *= TerrainMap.MoveFactor(e.PosF);       // 涉水减半/桥上正常
            return spd;
        }

        /// <summary>建筑碰撞:把单位推出建筑占地矩形(连续坐标下的障碍语义,M8.1a 矩形化)。</summary>
        private void PushOutOfBuildings(Entity u)
        {
            // 2026-09-14 制作人:「自己的建筑被卡住 1s 后可以直接穿透」—— 同阵营建筑:连续被同一栋挤住
            // ≥1s(10 tick)→ 开 1.2s 穿透窗口;敌方建筑照旧硬挡(卡位是战术要素)。
            foreach (var b in _entities.Values)
            {
                if (b.Kind != EntityKind.Building || b.Rubbled) continue;
                if (!BuildingFootprint.Contains(b, u.PosF)) continue;
                if (b.Side == u.Side)
                {
                    if (u.OwnBuildingGhostUntilTick > Tick && u.BuildingBlockId == b.Id)
                    { GhostSkipCount++; if (b.Side != u.Side) GhostSkipEnemyCount++; continue; }
                    if (u.BuildingBlockId == b.Id) u.BuildingBlockTicks++;
                    else { u.BuildingBlockId = b.Id; u.BuildingBlockTicks = 1; }
                    if (u.BuildingBlockTicks >= OwnBuildingGhostAfterTicks)
                    { u.BuildingBlockTicks = 0; u.OwnBuildingGhostUntilTick = Tick + OwnBuildingGhostWindowTicks; continue; }
                }
                u.PosF = BuildingFootprint.ClosestPoint(b, u.PosF);
                PushCount++;
                if (b.Side != u.Side) EnemyPushCount++;
            }
        }

        // [临时诊断计数]
        public static int PushCount, EnemyPushCount, GhostSkipCount, GhostSkipEnemyCount;

        public const int OwnBuildingGhostAfterTicks = 10;
        public const int OwnBuildingGhostWindowTicks = 12;

        /// <summary>
        /// 同阵营局部绕行(2026-09-14 制作人「后续支援被友军卡住」的正面解法):
        /// 前方探测距离内有己方单位(排除同团 —— 自己带的兵/同一主将的兵由阵型跟随处理)时,
        /// 把前进方向往侧面偏,去绕开而不是顶上去;左右两侧各探一次,取更空的一侧
        /// (并列时按 (自身 id + 挡路者 id) 的奇偶定侧,**确定性**、可复现)。
        /// 旧的"单位互堵 → 垂直偏移 2 米旁路点"已由本函数取代并从 `NavigationAid` 里删除。
        /// </summary>
        private SimVec2 SteerAroundAllies(Entity u, SimVec2 dir)
        {
            const float probe = 1.4f;
            const float coneWidth = 0.95f;
            const float weight = 1.5f;

            bool found = false; float bestD = float.MaxValue; int blockerId = 0;
            foreach (var o in _entities.Values)
            {
                if (o.Id == u.Id || o.Kind != EntityKind.Unit || o.Side != u.Side) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Dead || o.State == EntityState.Exchanging) continue;
                if (o.GeneralId != 0 && o.GeneralId == (u.IsSoldier ? u.GeneralId : u.Id)) continue;
                var to = o.PosF - u.PosF;
                float along = to.X * dir.X + to.Y * dir.Y;
                if (along <= 0f || along > probe) continue;
                float lat = System.Math.Abs(-to.Y * dir.X + to.X * dir.Y);
                if (lat > coneWidth) continue;
                float d = to.Magnitude;
                if (d >= bestD) continue;
                bestD = d; blockerId = o.Id; found = true;
            }
            if (!found) return dir;

            var left = new SimVec2(-dir.Y, dir.X);
            var right = new SimVec2(dir.Y, -dir.X);
            float cl = SideBlocked(u, left), cr = SideBlocked(u, right);
            SimVec2 side;
            if (cl < cr - 0.01f) side = left;
            else if (cr < cl - 0.01f) side = right;
            else side = (((u.Id + blockerId) & 1) == 0) ? left : right;
            var steer = new SimVec2(dir.X + side.X * weight, dir.Y + side.Y * weight).Normalized();
            return steer.SqrMagnitude < 1e-6f ? dir : steer;
        }

        /// <summary>某一侧 1.6 米内同阵营单位数(用于挑"更空的一侧")。</summary>
        private float SideBlocked(Entity u, SimVec2 side)
        {
            float n = 0f;
            foreach (var o in _entities.Values)
            {
                if (o.Id == u.Id || o.Kind != EntityKind.Unit || o.Side != u.Side) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Dead || o.State == EntityState.Exchanging) continue;
                if (o.GeneralId != 0 && o.GeneralId == (u.IsSoldier ? u.GeneralId : u.Id)) continue;
                float along = o.PosF.X * side.X + o.PosF.Y * side.Y - (u.PosF.X * side.X + u.PosF.Y * side.Y);
                if (along <= 0.1f || along > 1.6f) continue;
                float lat = System.Math.Abs(-(o.PosF.Y - u.PosF.Y) * side.X + (o.PosF.X - u.PosF.X) * side.Y);
                if (lat > 1.0f) continue;
                n += 1f;
            }
            return n;
        }

        /// <summary>单位间软碰撞:间距小于 0.9 米时沿连线对半推开。</summary>
        private void SoftCollide()
        {
            // 2026-09-14 制作人:"两个建模互相卡住 2s 左右就放行穿透"。
            // 触发口径(实测后改正):**不看位移**。实验证明单位对撞会被互推挤开(双方都在前进,
            // 位移不为 0)、而真正停滞的是被地形夹住的单位(那种卡住穿透也救不了)—— 所以按位移判会
            // 永远触发不到。这里直接数"被**同阵营**推挤的连续 tick":达 GhostAfterTicks 即开窗口。
            var allyPushed = new HashSet<int>();
            var units = new List<Entity>();
            foreach (var e in _entities.Values)
                if (e.Kind == EntityKind.Unit && e.GarrisonBuildingId == 0
                    && e.State != EntityState.Dead && e.State != EntityState.Exchanging)
                    units.Add(e);

            const float MinDist = 0.9f;
            var moved = new HashSet<int>();
            for (int i = 0; i < units.Count; i++)
            {
                for (int j = i + 1; j < units.Count; j++)
                {
                    var a = units[i];
                    var b = units[j];
                    // 兵团底座(M8.2a):同兵团士兵之间、士兵与己方主将之间不互推,避免阵型震荡
                    if ((a.IsSoldier || b.IsSoldier) && (a.GeneralId == b.Id || b.GeneralId == a.Id || (a.IsSoldier && b.IsSoldier && a.GeneralId != 0 && a.GeneralId == b.GeneralId))) continue;
                    // 2026-09-14 制作人:互卡 2 秒后放行**同阵营**穿透(计数与开窗都在本函数里,阈值见 NavigationAid)。
                    // 只对同阵营放行:敌我之间照旧互卡(卡位是战术要素,也不能让近战滑进敌方后排)。
                    float dx = a.PosF.X - b.PosF.X, dy = a.PosF.Y - b.PosF.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= MinDist * MinDist) continue;
                    bool sameSide = a.Side == b.Side;
                    if (sameSide)
                    {
                        // ★触发口径修正(2026-09-14):原实现把"同阵营"计数写在**距离判定之前**,
                        //   于是地图上任何两个同阵营单位(哪怕隔着几十米)每 tick 都在互相"计数",
                        //   结果整张图上的单位 2 秒后轮流拿到穿透窗口 —— 既不是"互卡",行为也无规律。
                        //   现在只对**真正贴着(接触)**且在走的一对计数(下面统一算一次,**不许再写第二处**)。
                        if (Tick <= a.GhostUntilTick || Tick <= b.GhostUntilTick) continue;
                        // ★2026-09-14(制作人「后续支援被友军卡住」的正面解法):
                        //   同阵营之间,只要有一方是**停着的**(待命/交战站定/驻城),双方就**不互推** ——
                        //   走的一方直接穿过(不再顶、不再被顶),站着的一方原地不动。
                        //   这正是制作人那条"互卡就放行穿透"口径的正确触发版:不必等 2 秒,
                        //   因为"一方在走、一方站定"本身就是明确的穿越意图;卡位战术语义只对**敌我**保留。
                        //   两个都在走时才互推(行军列队不叠在一起),2 秒窗口作最后兜底。
                        bool aMoving = a.HasMoveTarget && a.State == EntityState.Moving;
                        bool bMoving = b.HasMoveTarget && b.State == EntityState.Moving;
                        // 一方走、一方停(待命/交战站定)→ 走的一方穿过,停着的一方**原地不动**
                        if (aMoving != bMoving) continue;
                        // 两方都停着:照旧互推(否则静止的友军会永久重叠成一坨);
                        // 两方都在走且贴着:互推 + 计入"互卡"时长(2 秒窗口的最后兜底)
                        if (aMoving) { allyPushed.Add(a.Id); allyPushed.Add(b.Id); a.AllyPushTicks++; b.AllyPushTicks++; }
                    }
                    float d = (float)System.Math.Sqrt(d2);
                    float push = (MinDist - d) * 0.5f;
                    float nx = d > 1e-6f ? dx / d : 1f;
                    float ny = d > 1e-6f ? dy / d : 0f;
                    a.PosF = TerrainMap.ClampToEllipse(a.PosF + new SimVec2(nx * push, ny * push));
                    b.PosF = TerrainMap.ClampToEllipse(b.PosF - new SimVec2(nx * push, ny * push));
                    moved.Add(a.Id);
                    moved.Add(b.Id);
                }
            }
            // 本 tick 没被同阵营推挤的单位,互卡计数归零(必须连续 2 秒才算"卡住")
            foreach (var e in units)
            {
                if (allyPushed.Contains(e.Id))
                {
                    if (e.AllyPushTicks >= NavigationAid.GhostAfterTicks)
                    {
                        e.AllyPushTicks = 0;
                        e.GhostUntilTick = Tick + NavigationAid.GhostWindowTicks;
                    }
                }
                else e.AllyPushTicks = 0;
            }
            // 仅被推挤的单位回写格(避免覆盖未移动单位的手动定位)
            foreach (var e in units)
                if (moved.Contains(e.Id))
                    e.Pos = e.PosF.ToGrid();
        }

        /// <summary>建筑外接半径(米)= Max(HalfW,HalfH)+0.4。兼容旧近战判定,下一批 M8.1b 换精确矩形距;新代码请用 BuildingFootprint。</summary>
        public static float BuildingCollisionRadius(Entity b)
        {
            return System.Math.Max(BuildingFootprint.HalfW(b), BuildingFootprint.HalfH(b)) + 0.4f;
        }

        /// <summary>
        /// 2026-09-15 制作人:"**还是不攻击建筑,全在发呆**"(附图:近战主将站在敌方箭塔旁不动)。
        /// 探针实测:近战主将行军落点离塔心 **7.00 米**、近战士兵落点 **3.00 米**,而旧判定要求
        /// 中心距 ≤ **2.80 米** → 到位即 `State=Attacking` 然后**永久站着不掉血**
        /// (近战单位事实上**完全无法**拆建筑,只有射手能拆,所以一直没被发现)。
        /// 修两处:①判定基准;②到位后还差最后一段时**得继续逼近**。
        ///
        /// ① 判定基准改为**占地矩形距**(旧注释写的"下一批 M8.1b 换精确矩形距"就是这件事):
        /// 中心距在矩形对角线上会误判 —— 箭塔 2×2 的对角 ≈ 2.83 > 2.80,**贴到墙角反而打不到**。
        /// 容差 0.75 覆盖"落点落在格中心(k+0.5)、矩形边不一定在格中心"的半个格量化误差
        /// (2.5 半宽的兵营/大本营,边缘刚好落在两格之间,贴墙落点离边 0.5 米)。
        /// </summary>
        public const float MeleeBuildingReach = 0.75f;

        /// <summary>近战能否打到该建筑:在占地矩形内,或离矩形外沿 ≤ <see cref="MeleeBuildingReach"/>。</summary>
        public static bool InMeleeReachOfBuilding(Entity b, SimVec2 p)
        {
            if (BuildingFootprint.Contains(b, p)) return true;
            return SimVec2.Distance(p, BuildingFootprint.ClosestPoint(b, p)) <= MeleeBuildingReach;
        }

        /// <summary>单位能否攻击该建筑:远程按中心距 ≤ 射程;近战按占地矩形距(见上)。</summary>
        public static bool InAttackReachOfBuilding(Entity e, Entity b)
        {
            if (DamageCalculator.IsRanged(e) && e.Range > 1f)
                return SimVec2.Distance(e.PosF, SimVec2.FromGrid(b.Pos)) <= e.Range;
            return InMeleeReachOfBuilding(b, e.PosF);
        }

        private void CombatUpdate()
        {
            if (Game == null) return;
            float dt = 1f / Config.TickRate;
            var dead = new List<int>();
            ClearPendingDamage();

            // 2026-09-14 已实测确认(但**暂不采用**):战斗结算顺序决定镜像对局的胜负 ——
            // 遍历永远是实体创建顺序,而 `MatchSetup` 先建蓝方 → 蓝方每 tick 先出手;把红蓝位置对调,
            // 赢家也跟着对调(与位置无关、只与顺序有关)。曾试过"按 tick 奇偶交替遍历方向",
            // 实测**只是把偏袒减半、并未消除**(移动/阵型/支援等层同样按实体顺序遍历),
            // 却把 4 条"同一 tick 先手"这类有明确语义的时序断言打乱 → 已回退。
            // 正确的解法是**伤害同 tick 同时结算**(先累计、后统一应用),属核心改动,留作下一步。
            foreach (var e in _entities.Values)
            {
                if (e.Kind != EntityKind.Unit || e.State == EntityState.Dead) continue;
                if (e.GarrisonBuildingId != 0) continue; // 驻军单位不参与场外逻辑

                e.Status.Tick(dt);
                e.SecondsSinceCombat += dt;
                HeroAbilitySystem.UpdatePanShiShield(e, dt);
                if (e.Hero == HeroId.ZhuFeng && e.IsCharging && !HeroAbilitySystem.CanCharge(e, this))
                    e.IsCharging = false;

                if (e.Attack <= 0f) continue;

                if (e.AttackCooldown > 0f)
                    e.AttackCooldown -= dt;

                // 2026-09-13 制作人规则⑩:带攻城令的单位优先攻击视野内的敌人单位(防"无视出城迎战硬拆")。
                // 视野口径与 VisionSystem.UnitRadius 同源(射手将 9/近战将 6;士兵 远程6/近战3)。
                // 视野内发现敌人 → 弃攻城目标,移动接战(进射程后由下方正常索敌结算)。
                if (e.TargetBuildingId != 0 && EnemyInVision(e, out var vt))
                {
                    e.TargetBuildingId = 0;
                    SetMoveTarget(e.Id, vt.Pos);
                }

                Entity target = FindNearestEnemy(e, e.State == EntityState.Routing ? 1.4f : float.MaxValue);
                e.CurrentTargetId = target?.Id ?? 0;   // F-A03:View 层朝向用(区别于 LastAttackerId="谁打了我")
                if (target == null)
                {
                    // 2026-09-11 制作人裁决:推进遇墙就拆——顶在敌方完好墙段上且冷却好 → 打墙
                    // 2026-09-14 制作人:"攻击屏障时坠日居然要走到屏障脸上才能攻击,他应该可以远程的"
                    //   → 触发口径从"贴墙(`BlockedByWall`)"放宽到"**武器射程内**有敌方完好墙段就开打"。
                    //   近战射程 2~3 米 ≈ 贴脸,行为不变;射手 8 米 → 站远处就能拆墙 ✓。
                    // 只对**远程**放宽:近战照旧「贴墙推进就拆」(那套已实测调准,不动它)。
                    var wallNear = DamageCalculator.IsRanged(e) ? WallSystem.HitEnemyPanelWithin(e, e.Range) : null;
                    if (WallSystem.BlockedByWall(e) || wallNear != null)
                    {
                        e.State = EntityState.Attacking;
                        if (e.AttackCooldown <= 0f)
                        {
                            e.AttackCooldown = e.AttackIntervalSec * e.Status.AttackIntervalMultiplier;
                            float wallDmg = e.Attack * (1f + e.Status.AttackBonus);
                            // 2026-09-11 屏障特效:墙段事件(涟漪/击碎表现由 View 监听;加法式,参考 AttackerId 先例)
                            var wallHit = WallSystem.HitEnemyPanel(e, e.PosF) ?? wallNear;
                            if (wallHit != null)
                            {
                                int ws = (int)wallHit.Value.Owner;
                                bool broke = WallSystem.Damage(wallHit.Value.Owner, wallHit.Value.Panel, wallDmg);
                                Emit(new SimEvent(Tick, SimEventType.WallDamaged, wallHit.Value.Panel, wallDmg, e.Id, ws, e.PosF));
                                AddBuildingDamage(e, wallDmg);   // 墙计入对建筑伤害(2026-09-11 结算口径)
                                if (broke)
                                    Emit(new SimEvent(Tick, SimEventType.WallBroken, wallHit.Value.Panel, 0f, e.Id, ws));
                            }
                            Emit(new SimEvent(Tick, SimEventType.DamageDealt, e.Id, wallDmg, e.Id));
                        }
                    }
                    else if (e.State == EntityState.Attacking) e.State = EntityState.Idle;
                    continue;
                }

                // 溃逃单位保持 Routing 状态(不可调遣语义),不转入 Attacking
                if (e.State != EntityState.Routing)
                {
                    e.HasMoveTarget = false;
                    e.State = EntityState.Attacking;
                }
                if (e.AttackCooldown <= 0f)
                {
                    e.AttackCooldown = e.AttackIntervalSec * e.Status.AttackIntervalMultiplier;
                    e.AttackSeq++;   // GPT①:每次真实发射攻击时自增,View 按序号差判断"新的一击"
                    ApplyAttack(e, target, dead);
                }
            }

            // 本 tick 所有伤害统一结算(同 tick 同时结算:互相致命 → 同归于尽)
            ApplyStagedDamage(dead);

            foreach (int id in dead)
            {
                if (_entities.TryGetValue(id, out var e))
                {
                    if (!e.IsSoldier && !e.DeadCounted) { e.DeadCounted = true; LostGenerals[e.Side]++; } // 阵亡兵团计数(M6.4a)
                    if (e.Hero != null) HexSystem.OnUnitDied(this, e.Side); // 陷阵之志
                    Emit(new SimEvent(Tick, SimEventType.EntityDied, id));
                    if (e.Kind == EntityKind.Building)
                        _occupied.Remove(_grid.ToIndex(e.Pos));
                    _entities.Remove(id);
                    Emit(new SimEvent(Tick, SimEventType.EntityRemoved, id));
                }
            }
        }

        /// <summary>攻城单位的视野半径(与 VisionSystem.UnitRadius 同口径;那边是 private,这里同数值)。</summary>
        internal static float SiegeVisionRadius(Entity e)
        {
            if (e.IsSoldier)
                return (e.Hero == HeroId.ZhuiRi || e.Hero == HeroId.XingLuo) ? 6f : 3f;
            if (e.Hero == null) return 6f;
            return (e.Hero == HeroId.ZhuiRi || e.Hero == HeroId.XingLuo) ? 9f : 6f;
        }

        /// <summary>视野内是否有可交战的敌方单位(城内/交换中除外)。规则⑩:攻城优先打人。</summary>
        private bool EnemyInVision(Entity e, out Entity found)
        {
            found = null;
            float r = SiegeVisionRadius(e), best = float.MaxValue;
            foreach (var o in _entities.Values)
            {
                if (o.Kind != EntityKind.Unit || o.Side == e.Side || o.Side == Side.Neutral) continue;
                if (o.State == EntityState.Dead || o.State == EntityState.Exchanging || o.GarrisonBuildingId != 0) continue;
                float d = SimVec2.Distance(e.PosF, o.PosF);
                if (d > r || d >= best) continue;
                found = o; best = d;
            }
            return found != null;
        }

        private Entity FindNearestEnemy(Entity e, float maxDist)
        {
            Entity best = null;
            float bestDist = float.MaxValue;
            foreach (var o in _entities.Values)
            {
                if (o.Kind != EntityKind.Unit || o.Side == e.Side || o.State == EntityState.Dead)
                    continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging)
                    continue; // 城内/交换中的单位不可被城外锁定
                // 2026-09-14 溢出保护:本 tick 已被登记致死的目标不再被选(否则攻击白费)
                if (o.Hp - PendingDamageOf(o.Id) <= 0f) continue;
                float d = SimVec2.Distance(e.PosF, o.PosF);
                float effRange = e.Range + 0.3f; // 近战贴身容差
                if (d > effRange || d > maxDist) continue;
                if (d < bestDist - 1e-4f || (System.Math.Abs(d - bestDist) <= 1e-4f && best != null && o.Id < best.Id))
                {
                    best = o;
                    bestDist = d;
                }
            }
            return best;
        }

        /// <summary>
        /// 标记"这个兵团正在交战"。**关键**:成员(士兵)挨打时,把它的**主将**一起标记 ——
        /// 2026-09-14 制作人口径「主将和它的兵团就是一个单位」。
        /// 不这么做的话,主将的 `SecondsSinceCombat` 一直不刷新,支援规则里"自身已在交战就不跑"那道闸
        /// 会误判"我没在交战"→ 整团被拉去支援别人、把自己眼前的仗丢了(实测对称 3v3:蓝色近战团
        /// `AK2/挨打否` 而兵从 7 一路掉到 0)。
        /// </summary>
        private void MarkInCombat(Entity attacker, Entity defender)
        {
            if (attacker != null) attacker.SecondsSinceCombat = 0f;
            if (defender == null) return;
            defender.SecondsSinceCombat = 0f;
            if (!defender.IsSoldier || defender.GeneralId == 0) return;
            var g = GetEntity(defender.GeneralId);
            if (g != null) g.SecondsSinceCombat = 0f;
        }

        // ================= 同 tick 同时结算(2026-09-14 制作人采用"首选"方案) =================
        // 问题(已实测):战斗原来**立即扣血**,于是"谁先被遍历到谁先出手",而遍历是实体创建顺序
        // (MatchSetup 先建蓝方)→ 镜像对局由创建顺序决定胜负(对调红蓝位置,赢家也跟着对调)。
        // 解法(业界通行做法:先登记、后统一施加,见 PROJEC_STATE 二十五轮调研):
        //   本 tick 内所有伤害只按**受害者聚合**登记,循环结束后统一扣盾/扣血、再统一判死。
        //   聚合后施加 ⇒ 护盾吸收与"谁先谁后"无关;互相致命 ⇒ **同归于尽**(玩家共识语义)。
        // 溢出保护:已登记伤害足以致死的目标不再被选为目标/不再吃溅射,避免浪费攻击。
        private readonly List<KeyValuePair<int, float>> _pendingDamage = new List<KeyValuePair<int, float>>();
        private readonly Dictionary<int, int> _pendingIndex = new Dictionary<int, int>();

        /// <summary>本 tick 已登记(尚未施加)到某单位的伤害合计 —— 溢出保护用。</summary>
        private float PendingDamageOf(int entityId)
        {
            for (int i = 0; i < _pendingDamage.Count; i++)
                if (_pendingDamage[i].Key == entityId) return _pendingDamage[i].Value;
            return 0f;
        }

        private void ClearPendingDamage()
        {
            _pendingDamage.Clear();
            _pendingIndex.Clear();
        }

        /// <summary>登记一次伤害(不立即扣血):记账/事件/交战标记当场做,扣盾扣血延后到本 tick 统一结算。</summary>
        private void QueueDamage(Entity attacker, Entity defender, float dmg)
        {
            MarkInCombat(attacker, defender);
            if (attacker != null) defender.LastAttackerId = attacker.Id;   // "谁在打我"→ 行为层"挨打=看得见"
            AddCombatDamage(attacker, defender, dmg);
            Emit(new SimEvent(Tick, SimEventType.DamageDealt, defender.Id, dmg, attacker != null ? attacker.Id : 0));

            int idx;
            if (_pendingIndex.TryGetValue(defender.Id, out idx)) _pendingDamage[idx] = new KeyValuePair<int, float>(defender.Id, _pendingDamage[idx].Value + dmg);
            else
            {
                _pendingIndex[defender.Id] = _pendingDamage.Count;
                _pendingDamage.Add(new KeyValuePair<int, float>(defender.Id, dmg));
            }
        }

        /// <summary>
        /// 统一结算本 tick 登记的全部伤害:按 **受害者 id 升序**施加(确定性、与遍历顺序无关),
        /// 护盾先吸收(按该受害者本 tick 的**总伤害**聚合吸收,因此与命中次序无关)、溢出扣生命,
        /// 扣完再统一判死并并入 dead 列表。
        /// </summary>
        private void ApplyStagedDamage(List<int> dead)
        {
            if (_pendingDamage.Count == 0) return;
            _pendingDamage.Sort((x, y) => x.Key.CompareTo(y.Key));   // 确定性:按 id 升序
            for (int i = 0; i < _pendingDamage.Count; i++)
            {
                var entry = _pendingDamage[i];
                var victim = GetEntity(entry.Key);
                if (victim == null || victim.State == EntityState.Dead) continue;
                float shieldBefore = victim.Shield, hpBefore = victim.Hp;
                float remaining = entry.Value;
                if (victim.Shield > 0f)
                {
                    float absorbed = System.Math.Min(victim.Shield, remaining);
                    victim.Shield -= absorbed;
                    remaining -= absorbed;
                }
                victim.Hp -= remaining;
                bool diedThisTick = false;
                if (victim.Hp <= 0f)
                {
                    victim.State = EntityState.Dead;
                    dead.Add(victim.Id);
                    diedThisTick = true;
                }
                // 2026-09-17 特效契约(已批):结算后摘要 —— 判死检查之后、EntityDied 派发之前发出;
                // 盾血均无净变化不发(职责分工见 SimEvent 注释)。纯新增,结算本身零变化。
                if (diedThisTick || victim.Shield != shieldBefore || victim.Hp != hpBefore)
                {
                    Emit(new SimEvent(Tick, SimEventType.SettledDamage, victim.Id)
                    {
                        ShieldBefore = shieldBefore, ShieldAfter = victim.Shield,
                        HpBefore = hpBefore, HpAfter = victim.Hp,
                        Died = diedThisTick,
                    });
                }
            }
            ClearPendingDamage();
        }

        private void ApplyAttack(Entity attacker, Entity defender, List<int> dead)
        {
            MarkInCombat(attacker, defender);
            defender.LastAttackerId = attacker.Id;   // "谁在打我":行为层用它做"挨打=看得见"(见 Entity.LastAttackerId)

            // 基础层(GDD 公式)
            float baseDmg = DamageCalculator.CalculateDamage(attacker, defender, Game, attacker.IsCharging);

            // 星落升级·诛灭:基础攻击 5 → 6(基础层内替换)
            bool zhuMie = attacker.Hero == HeroId.XingLuo && IsUpgraded(attacker.Side, HeroId.XingLuo);
            if (zhuMie && attacker.Attack > 0f) baseDmg = baseDmg / attacker.Attack * 6f;

            // 叠算层(D28):输出加算、承伤加算、免伤乘算;撤退/溃逃 −25%/+25%(M8.7)
            float outSum = attacker.Status.AttackBonus + HexSystem.OutputBonusOf(this, attacker)
                         + (attacker.State == EntityState.Routing ? -0.25f : 0f);
            float takeSum = defender.Status.DamageTakenBonus + HexSystem.DamageTakenBonusOf(this, defender)
                          + (defender.State == EntityState.Routing ? 0.25f : 0f);
            float reduce = defender.Status.DamageReduction * HexSystem.DamageReductionOf(this, defender);
            float dmg = baseDmg * (1f + outSum) * (1f + takeSum) * reduce;

            // 附加项(层后加法)
            if (zhuMie) dmg += 0.01f * defender.MaxHp;

            // 2026-09-14:改为**登记**(扣盾扣血延后到本 tick 统一结算,见 QueueDamage/ApplyStagedDamage)
            QueueDamage(attacker, defender, dmg);

            // 命中特效(减益/灼烧等)仍当场施加:它们不改血量,且行为层要立刻看到
            HeroAbilitySystem.ApplyOnHitEffects(this, attacker, defender);

            // 断峰升级·破军:身前 90 度扇形(半径 2 米)内全部敌人受单次 75% 伤害
            if (attacker.Hero == HeroId.DuanFeng && IsUpgraded(attacker.Side, HeroId.DuanFeng))
                Cleave(attacker, defender, dead);

            // (判死移交 ApplyStagedDamage:本 tick 所有伤害施加完之后统一判)
            if (dead == null) { }
        }

        private void Cleave(Entity attacker, Entity mainTarget, List<int> dead)
        {
            float facing = mainTarget.PosF.X >= attacker.PosF.X ? 1f : -1f;
            foreach (var o in _entities.Values)
            {
                if (o.Id == mainTarget.Id) continue;
                if (o.Kind != EntityKind.Unit || o.Side == attacker.Side || o.State == EntityState.Dead) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging) continue;
                float dx = o.PosF.X - attacker.PosF.X;
                float dy = o.PosF.Y - attacker.PosF.Y;
                if (dx * facing < 0) continue;         // 身后不打
                if (dx * dx + dy * dy > 4f) continue;  // 半径 2 米扇形
                // 溢出保护:本 tick 已被登记致死的不再吃溅射(避免把伤害浪费在必定死亡的兵身上)
                if (o.Hp - PendingDamageOf(o.Id) <= 0f) continue;
                float baseDmg = 0.75f * DamageCalculator.CalculateDamage(attacker, o, Game, attacker.IsCharging);
                // 撤退/溃逃 −25%/+25%(M8.7)
                float outSum = attacker.Status.AttackBonus + (attacker.State == EntityState.Routing ? -0.25f : 0f);
                float takeSum = o.Status.DamageTakenBonus + (o.State == EntityState.Routing ? 0.25f : 0f);
                float dmg = baseDmg * (1f + outSum) * (1f + takeSum) * o.Status.DamageReduction;
                QueueDamage(attacker, o, dmg);   // 同样登记,延后统一结算
                if (o.Hp <= 0f) { o.State = EntityState.Dead; dead.Add(o.Id); }
            }
        }

        private double ChargeSpeedOf(HeroId id)
        {
            foreach (var h in Game.Heroes)
                if (h.Id == id) return h.ChargeSpeed;
            return 0;
        }

        /// <summary>战绩归因(2026-09-11):伤害/承伤归到出手方与受击方主将名下——
        /// 士兵实体自带主将 Hero(变装口径),士兵与主将统一用自身 Hero 归因;
        /// 受击为建筑计"对建筑伤害"(墙在攻墙处另行计入)。</summary>
        static void AddCombatDamage(Entity attacker, Entity victim, float dmg)
        {
            if (attacker == null || victim == null || dmg <= 0f) return;
            if (attacker.Side == Side.Neutral) return;
            HeroId aHero = attacker.Hero ?? HeroId.PanShi;
            if (victim.Kind == EntityKind.Building)
                MatchStats.AddBuildingDamage(attacker.Side, aHero, dmg);
            else
                MatchStats.AddHeroDamage(attacker.Side, aHero, dmg);
            if (victim.Kind == EntityKind.Unit && victim.Side != Side.Neutral)
                MatchStats.AddDamageTaken(victim.Side, victim.Hero ?? HeroId.PanShi, dmg);
        }

        /// <summary>墙伤害归因(攻墙处调用)。</summary>
        static void AddBuildingDamage(Entity attacker, float dmg)
        {
            if (attacker == null || attacker.Side == Side.Neutral) return;
            MatchStats.AddBuildingDamage(attacker.Side, attacker.Hero ?? HeroId.PanShi, dmg);
        }

        public void Emit(SimEvent ev)
        {
            for (int i = _listeners.Count - 1; i >= 0; i--)
                _listeners[i]?.Invoke(ev);
        }
    }
}
