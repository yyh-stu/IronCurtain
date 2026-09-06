using System;
using System.Collections.Generic;
using System.Linq;

namespace IronCurtain.Sim
{
    public enum AiTier { Simple, Normal }

    public static class AiSystem
    {
        const int DecisionInterval = 20;
        const int ReinforceInterval = 50;     // M8.5b:AI 自动补兵评估间隔(5s)
        const int ReinforcePoolThreshold = 10; // 池 ≥10 才放,避免碎补

        class AiState
        {
            public Side Side;
            public AiTier Tier;
            public int ProducedSinceZhenYue; // 已产英雄计数(配比)
            public bool ZhenYueQueued;       // 下一只为镇岳
            public bool WaveActive;
            public int WaveStartCount;
            public List<int> WaveUnits = new List<int>();
        }

        static readonly Dictionary<Side, AiState> _ais = new Dictionary<Side, AiState>();

        public static void Configure(SimCore sim, Side side, AiTier tier)
        {
            _ais[side] = new AiState { Side = side, Tier = tier };
        }

        public static void Reset() => _ais.Clear();

        public static void Tick(SimCore sim, float dt)
        {
            // M8.5b:AI 自动补兵(Simple/Normal 共用,顶层入口)
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
            {
                if (ai.Tier == AiTier.Simple) TickSimple(sim, ai);
                else TickNormal(sim, ai);
            }
        }

        // ---------- 简单 AI ----------
        static void TickSimple(SimCore sim, AiState ai)
        {
            FillProduction(sim, ai, simple: true);
            PassiveDefend(sim, ai);
        }

        static void PassiveDefend(SimCore sim, AiState ai)
        {
            var myBuildings = sim.Entities.Values
                .Where(e => e.Side == ai.Side && e.Kind == EntityKind.Building && !e.Rubbled)
                .ToList();
            foreach (var b in myBuildings)
            {
                var threat = sim.Entities.Values
                    .Where(e => e.Kind == EntityKind.Unit && e.Side != ai.Side && e.State != EntityState.Dead
                                && VisionSystem.IsUnitVisible(sim, ai.Side, e.Id)
                                && Dist(sim, e, b) <= 8f)
                    .FirstOrDefault();
                if (threat == null) continue;
                var idle = sim.Entities.Values
                    .Where(e => e.Kind == EntityKind.Unit && e.Side == ai.Side && !e.IsSoldier
                                && e.State == EntityState.Idle && e.GarrisonBuildingId == 0)
                    .OrderBy(e => Dist(sim, e, b))
                    .Take(5).ToList();
                foreach (var u in idle)
                {
                    var t = FrontCell(sim, b);
                    if (t.HasValue) sim.SetMoveTarget(u.Id, t.Value);
                }
            }
        }

        // ---------- 普通 AI ----------
        static void TickNormal(SimCore sim, AiState ai)
        {
            // 1. 补产(配比)
            FillProduction(sim, ai, simple: false);
            // 2. 升级
            if (sim.EpOf(ai.Side) >= 2 && !sim.IsUpgraded(ai.Side, HeroId.ZhenYue))
                sim.BuyUpgrade(ai.Side, HeroId.ZhenYue, out _);
            // 3. 规律抽卡
            if (sim.Tick >= 1800 && HexSystem.CanDraw(sim, ai.Side))
                TryDrawHex(sim, ai);
            // 5. 受挫撤退(先于新波次判断)
            if (ai.WaveActive) CheckRetreat(sim, ai);
            // 4. 集结进攻
            if (!ai.WaveActive) TryLaunchWave(sim, ai);
            // 6. 主动防守
            ActiveDefend(sim, ai);
        }

        static void TryDrawHex(SimCore sim, AiState ai)
        {
            var prio = new[] { HexId.SteelTide, HexId.RapidDeploy, HexId.HoldFast, HexId.Vengeance,
                               HexId.LightMobility, HexId.IronWall, HexId.HiddenPath };
            var choices = HexSystem.DrawChoices(sim, ai.Side);
            foreach (var c in prio)
            {
                if (c == HexId.Sandbag) continue;
                if (choices.Contains(c) && HexSystem.UseHex(sim, ai.Side, c, 0, out _)) return;
            }
        }

        static void CheckRetreat(SimCore sim, AiState ai)
        {
            ai.WaveUnits = ai.WaveUnits
                .Where(id => sim.Entities.TryGetValue(id, out var e) && e.State != EntityState.Dead && !e.IsSoldier)
                .ToList();
            if (ai.WaveUnits.Count < ai.WaveStartCount * 0.4f)
            {
                var home = sim.Entities.Values
                    .FirstOrDefault(e => e.Side == ai.Side && e.Kind == EntityKind.Building && e.Building == BuildingId.Base);
                var front = home != null ? FrontCell(sim, home) : (GridPos?)null;
                foreach (var id in ai.WaveUnits)
                    if (front.HasValue) sim.SetMoveTarget(id, front.Value);
                ai.WaveActive = false;
                ai.WaveUnits.Clear();
            }
        }

        static void TryLaunchWave(SimCore sim, AiState ai)
        {
            var field = sim.Entities.Values
                .Where(e => e.Kind == EntityKind.Unit && e.Side == ai.Side && !e.IsSoldier
                            && e.State != EntityState.Dead && e.GarrisonBuildingId == 0
                            && e.State != EntityState.Exchanging)
                .ToList();
            // 将兵制(M8.4b 名额上限 4):波次门槛按兵团计——8 只单兵的旧口径在 4 名额下永不满足,AI 永不推进
            if (field.Count < 2) return;

            var home = Home(sim, ai.Side);
            // 目标选择
            var neutral = sim.Entities.Values
                .Where(e => e.Kind == EntityKind.Building && e.Building == BuildingId.ResourcePoint
                            && e.Side == Side.Neutral && !e.Rubbled
                            && VisionSystem.IsBuildingKnown(sim, ai.Side, e.Id))
                .OrderBy(e => home != null ? Dist(sim, e, home) : 0f)
                .FirstOrDefault();
            int targetId; SiegeIntent intent;
            if (neutral != null) { targetId = neutral.Id; intent = SiegeIntent.Capture; }
            else
            {
                var enemyBuildings = sim.Entities.Values
                    .Where(e => e.Kind == EntityKind.Building && e.Side != ai.Side && e.Side != Side.Neutral
                                && !e.Rubbled && e.Building != BuildingId.Base
                                && VisionSystem.IsBuildingKnown(sim, ai.Side, e.Id))
                    .Select(e => new { E = e, G = VisionSystem.LastKnownGarrison(sim, ai.Side, e.Id) ?? 999 })
                    .OrderBy(x => x.G).ThenBy(x => home != null ? Dist(sim, x.E, home) : 0f)
                    .FirstOrDefault();
                if (enemyBuildings == null) return;
                targetId = enemyBuildings.E.Id;
                intent = enemyBuildings.E.Building == BuildingId.Tower ? SiegeIntent.Destroy : SiegeIntent.Capture;
            }

            var targetB = sim.GetEntity(targetId);
            var wave = field.OrderBy(e => targetB != null ? Dist(sim, e, targetB) : 0f).Take(4).Select(e => e.Id).ToList(); // 名额 4,一波最多全员
            if (SiegeSystem.SetIntent(sim, targetId, ai.Side, intent, out _))
            {
                SiegeSystem.IssueOrder(sim, wave, targetId, out _);
                ai.WaveActive = true;
                ai.WaveUnits = wave;
                ai.WaveStartCount = wave.Count;
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
                                && e.State == EntityState.Idle && e.GarrisonBuildingId == 0)
                    .OrderBy(e => Dist(sim, e, b))
                    .Take(6).ToList();
                // 将兵制中间态:非士兵无法驻军(仅士兵可),防御=调到城门前排站位;士兵驻军防御在 M8.5 接上
                foreach (var u in idle)
                {
                    var t = FrontCell(sim, b);
                    if (t.HasValue) sim.SetMoveTarget(u.Id, t.Value);
                }
                return;
            }
        }

        // ---------- 公共 ----------
        /// <summary>产将(M8.4b):向己方主基地入队;名额未满且队列空才入队。兵营无生产集,不再入队。</summary>
        static void FillProduction(SimCore sim, AiState ai, bool simple)
        {
            if (sim.PopulationOf(ai.Side) >= sim.PopulationCapOf(ai.Side)) return; // 名额满,等待

            var b = Home(sim, ai.Side);
            if (b == null || b.Prod == null) return;

            for (int s = 0; s < b.Prod.Slots.Length; s++)
            {
                var slot = b.Prod.Slots[s];
                if (slot.Current != null || slot.Pending.Count > 0) return; // 队列非空,不重复入队
            }

            HeroId hero;
            if (simple) hero = HeroId.PanShi;
            else if (ai.ZhenYueQueued) hero = HeroId.ZhenYue;
            else hero = ai.ProducedSinceZhenYue % 2 == 0 ? HeroId.PanShi : HeroId.DuanFeng;

            ProductionSystem.Enqueue(sim, b.Id, hero, out _);

            if (simple) return;
            if (hero == HeroId.ZhenYue) { ai.ZhenYueQueued = false; ai.ProducedSinceZhenYue = 0; }
            else
            {
                ai.ProducedSinceZhenYue++;
                if (ai.ProducedSinceZhenYue >= 3) ai.ZhenYueQueued = true;
            }
        }

        static Entity Home(SimCore sim, Side side)
        {
            return sim.Entities.Values.FirstOrDefault(e => e.Side == side && e.Kind == EntityKind.Building
                                                        && e.Building == BuildingId.Base && !e.Rubbled);
        }

        /// <summary>城门站位格:建筑朝敌侧(碰撞半径+0.6 米),行序 15 优先、椭圆内、非水域。</summary>
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
