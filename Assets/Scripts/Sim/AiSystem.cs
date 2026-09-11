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
            public bool WaveActive;
            public int WaveStartCount;
            public List<int> WaveUnits = new List<int>();
            public int NextLane;            // 2026-09-11 三路分推:波次目标路 0上/1中/2下,成功发波后轮转
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
            TryUseHeldHex(sim, ai);
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
            return false; // 例如铁壁暂时无合法落点：保留手牌，下个决策 tick 重试。
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
            // 目标选择(2026-09-11 三路分推):本波主攻路 = ai.NextLane 轮转(上/中/下),
            // 优先打**该路上的**已知敌方建筑;该路暂无已知目标时回退全局最优(旧口径),不至于卡兵。
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
                var candidates = sim.Entities.Values
                    .Where(e => e.Kind == EntityKind.Building && e.Side != ai.Side && e.Side != Side.Neutral
                                && !e.Rubbled && e.Building != BuildingId.Base
                                && VisionSystem.IsBuildingKnown(sim, ai.Side, e.Id))
                    .Select(e => new { E = e, G = VisionSystem.LastKnownGarrison(sim, ai.Side, e.Id) ?? 999,
                                       Lane = LaneIndexOf(SimVec2.FromGrid(e.Pos)) })
                    .OrderBy(x => x.G).ThenBy(x => home != null ? Dist(sim, x.E, home) : 0f)
                    .ToList();
                if (candidates.Count == 0) return;
                var pick = candidates.FirstOrDefault(x => x.Lane == ai.NextLane) ?? candidates[0];
                targetId = pick.E.Id;
                intent = IntentFor(pick.E);
            }

            var targetB = sim.GetEntity(targetId);
            var wave = field.OrderBy(e => targetB != null ? Dist(sim, e, targetB) : 0f).Take(4).Select(e => e.Id).ToList(); // 名额 4,一波最多全员
            var order = SiegeSystem.TryIssueRegimentOrder(sim, wave, targetId, ai.Side, intent);
            if (order.Success)
            {
                ai.WaveActive = true;
                ai.WaveUnits = wave;
                ai.WaveStartCount = wave.Count;
                ai.NextLane = (ai.NextLane + 1) % 3;   // 下一波换路,三路都有压力
            }
        }

        /// <summary>进攻意图(2026-09-11):路上的箭塔=摧毁(旧口径);**草坪箭塔=占领**——
        /// 无守军时贴上即被 InstantCapture 接管,保住箭塔为我所用,比拆掉划算。</summary>
        static SiegeIntent IntentFor(Entity b)
        {
            if (b.Building != BuildingId.Tower) return SiegeIntent.Capture;
            var p = SimVec2.FromGrid(b.Pos);
            for (int l = 0; l < 3; l++)
                if (Math.Abs(p.Y - TerrainMap.LaneY(p.X, l)) <= TerrainMap.LaneHalfWidth + 2f)
                    return SiegeIntent.Destroy;     // 路上的防御塔
            return SiegeIntent.Capture;             // 四块草坪的箭塔(不在任何路上)
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

            // 2026-09-11 制作人拍板"AI 选将随机化,避免全屏磐石":替换旧的固定配比
            // (Simple=恒磐石;Normal=磐石/断峰交替+每第 4 只镇岳)。
            HeroId hero = PickProductionHero(sim, simple ? AiTier.Simple : AiTier.Normal);
            ProductionSystem.Enqueue(sim, b.Id, hero, out _);
        }

        /// <summary>产将选择(2026-09-11):用 sim 的确定性 RNG(回放/测试可复现)。
        /// Simple=三近战随机;Normal=六英雄加权(近战各×2、远程各×1)——前压为主、偶有远程混编。
        /// 公开供测试断言分布。</summary>
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
