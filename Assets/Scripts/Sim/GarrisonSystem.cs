using System.Collections.Generic;
using System.Linq;

namespace IronCurtain.Sim
{
    /// <summary>驻军系统(M8.3a 将兵化:仅士兵可驻军;临时驻军=城后等候不冻结移动;士兵永不溃逃)。M8.1b:城门占地矩形语义(D18)。</summary>
    public static class GarrisonSystem
    {
        // D27:建筑脱战 20 秒后每秒回复 4 点,至上限
        private const float RegenDelaySec = 20f;
        private const float RegenPerSec = 4f; // D27
        private const float TempFollowRange = 15f; // 城后等候:主将距城超出即解除

        private static readonly HashSet<HeroId> RangedHeroes = new HashSet<HeroId> { HeroId.ZhuiRi, HeroId.XingLuo };

        public static bool EnterFormal(SimCore sim, IReadOnlyList<int> unitIds, int buildingId, out string error)
        {
            error = null;
            if (!sim.Entities.ContainsKey(buildingId)) { error = "建筑不存在"; return false; }
            var b = sim.GetEntity(buildingId);
            var cfg = sim.BuildingConfigOf(b.Building.Value);
            if (cfg.GarrisonFormal <= 0) { error = "该建筑不可驻军"; return false; }
            var accepted = new List<int>();
            foreach (var id in unitIds)
            {
                if (!sim.Entities.ContainsKey(id)) continue;
                var u = sim.GetEntity(id);
                if (!u.IsSoldier) { error = "仅士兵可驻军"; continue; } // 主将永不进城
                if (u.Side != b.Side) { error = "建筑须为己方"; Rollback(sim, accepted, buildingId); return false; }
                if (b.GarrisonFormal.Count >= cfg.GarrisonFormal)
                {   // 驻军全满:路过或城门站定(仅士兵)
                    if (!u.HasMoveTarget && u.GarrisonBuildingId == 0)
                    {
                        var g = FindGateCell(sim, b);
                        if (g != null) sim.SetMoveTarget(id, g.Value);
                    }
                    continue;
                }
                DoEnterFormal(sim, u, buildingId, b.GarrisonFormal);
                accepted.Add(id);
            }
            return true;
        }

        public static bool EnterTemp(SimCore sim, IReadOnlyList<int> unitIds, int buildingId, out string error)
        {
            error = null;
            if (!sim.Entities.ContainsKey(buildingId)) { error = "建筑不存在"; return false; }
            var b = sim.GetEntity(buildingId);
            int cap = b.IsFort ? 3 : (b.Building == null ? 0 : sim.BuildingConfigOf(b.Building.Value).GarrisonTemp); // 堡垒驻军上限 3(D32)
            if (cap <= 0) { error = "该建筑不可临时驻军"; return false; }
            var accepted = new List<int>();
            foreach (var id in unitIds)
            {
                if (!sim.Entities.ContainsKey(id)) continue;
                var u = sim.GetEntity(id);
                if (!u.IsSoldier) { error = "仅士兵可驻军"; continue; } // 主将城外等候
                if (u.Side != b.Side) { error = "建筑须为己方"; RollbackTemp(sim, accepted, buildingId); return false; }
                if (b.GarrisonTemp.Count >= cap)
                {   // 临时驻军全满:路过或城门站定(仅士兵)
                    if (!u.HasMoveTarget && u.GarrisonBuildingId == 0)
                    {
                        var g = FindGateCell(sim, b);
                        if (g != null) sim.SetMoveTarget(id, g.Value);
                    }
                    continue;
                }
                DoEnterTemp(u, buildingId, b.GarrisonTemp);
                accepted.Add(id);
            }
            return true;
        }

        public static bool Exit(SimCore sim, int buildingId, HeroId hero, out string error) // D24:士兵回兵团;非士兵放城门站定
        {
            error = null;
            if (!sim.Entities.ContainsKey(buildingId)) { error = "建筑不存在"; return false; }
            var b = sim.GetEntity(buildingId);
            int id = b.GarrisonFormal.FirstOrDefault(x => sim.GetEntity(x).Hero == hero);
            bool inFormal = id != 0;
            if (id == 0) id = b.GarrisonTemp.FirstOrDefault(x => sim.GetEntity(x).Hero == hero);
            if (id == 0) { error = "城内无该兵种"; return false; }
            var u = sim.GetEntity(id);
            if (inFormal) b.GarrisonFormal.Remove(id); else b.GarrisonTemp.Remove(id);
            if (u.IsSoldier)
            {   // 士兵出城:回到兵团阵型(跟随主将;主将死→无将续战)
                u.GarrisonBuildingId = 0; u.TempGarrisonId = 0;
                u.State = EntityState.Idle; u.HasMoveTarget = false;
                return true;
            }
            var gate = FindGateCell(sim, b);
            if (gate == null) { error = "城门无空位"; return false; }
            u.Pos = gate.Value; u.PosF = SimVec2.FromGrid(gate.Value); u.GarrisonBuildingId = 0; u.State = EntityState.Idle;
            u.HasMoveTarget = false;
            return true;
        }

        /// <summary>城池易主:士兵只清登记(永不溃逃,留场续战);非士兵(旧数据)向同一线后方最近己方据点溃逃。</summary>
        public static void OnCityCaptured(SimCore sim, Entity b)
        {
            var fleeing = new List<int>(b.GarrisonTemp);
            b.GarrisonTemp.Clear();
            foreach (var id in fleeing)
            {
                var u = sim.GetEntity(id);
                if (u == null) continue;
                if (u.IsSoldier)
                {   // 士兵永不溃逃:清登记留兵团(主将已死→无将续战)
                    u.GarrisonBuildingId = 0; u.TempGarrisonId = 0; u.State = EntityState.Idle;
                    continue;
                }
                var dest = FindRoutDestination(sim, u);
                if (dest == null) { u.GarrisonBuildingId = 0; u.State = EntityState.Idle; continue; }
                var gate = FindGateCell(sim, dest);
                if (gate == null)
                {   // 城门无位则就近找空位
                    for (int d = 1; d <= 8 && gate == null; d++)
                        for (int dr = 0; dr < LaneGrid.Rows && gate == null; dr++)
                        {
                            var p = new GridPos(dr, dest.Pos.Col + (u.Side == Side.Blue ? -d : d));
                            if (IsCellFree(sim, p) && TerrainMap.IsInside(p)) gate = p;
                        }
                }
                u.GarrisonBuildingId = 0;
                // 从城池后方出城,避免被已失守的城堵住退路
                int back = u.Side == Side.Blue ? -1 : 1;
                int behind = back * ((int)System.Math.Ceiling(BuildingFootprint.HalfW(b)) + 1); // 起点须落在失守建筑占地矩形之外
                var start = new GridPos(b.Pos.Row, b.Pos.Col + behind);
                if (!IsCellFree(sim, start))
                {
                    for (int dr = 0; dr < LaneGrid.Rows; dr++)
                    {
                        var alt = new GridPos(dr, b.Pos.Col + behind);
                        if (IsCellFree(sim, alt) && TerrainMap.IsInside(alt)) { start = alt; break; }
                    }
                }
                if (IsCellFree(sim, start)) { u.Pos = start; u.PosF = SimVec2.FromGrid(start); }
                u.State = EntityState.Routing;
                u.HasMoveTarget = true;
                u.MoveTarget = gate ?? dest.Pos;
            }
        }

        /// <summary>建筑化为废墟:士兵只清登记(永不溃逃);非士兵(旧数据)强制后撤(途中遇敌交战 -30%)。</summary>
        public static void OnBuildingRubbled(SimCore sim, Entity b)
        {
            var fleeing = new List<int>(b.GarrisonFormal);
            fleeing.AddRange(b.GarrisonTemp);
            b.GarrisonFormal.Clear();
            b.GarrisonTemp.Clear();
            foreach (var id in fleeing)
            {
                var usedGates = new System.Collections.Generic.HashSet<long>(); // 溃逃单位分派不同城门格,避免堆叠无法抵达
                var u = sim.GetEntity(id);
                if (u == null) continue;
                if (u.IsSoldier)
                {   // 士兵永不溃逃:清登记留兵团
                    u.GarrisonBuildingId = 0; u.TempGarrisonId = 0; u.State = EntityState.Idle;
                    continue;
                }
                var dest = FindRoutDestination(sim, u);
                if (dest == null) { u.GarrisonBuildingId = 0; u.State = EntityState.Idle; continue; }
                var gate = FindGateCell(sim, dest, usedGates);
                if (gate != null)
                    usedGates.Add(((long)gate.Value.Row << 32) | (uint)gate.Value.Col);
                if (gate == null)
                {
                    for (int d = 1; d <= 8 && gate == null; d++)
                        for (int dr = 0; dr < LaneGrid.Rows && gate == null; dr++)
                        {
                            var p = new GridPos(dr, dest.Pos.Col + (u.Side == Side.Blue ? d : -d));
                            if (IsCellFree(sim, p) && TerrainMap.IsInside(p)) gate = p;
                        }
                }
                u.GarrisonBuildingId = 0;
                int back = u.Side == Side.Blue ? -1 : 1;
                int behind = back * ((int)System.Math.Ceiling(BuildingFootprint.HalfW(b)) + 1); // 起点须落在失守建筑占地矩形之外
                var start = new GridPos(b.Pos.Row, b.Pos.Col + behind);
                if (!IsCellFree(sim, start))
                {
                    for (int dr = 0; dr < LaneGrid.Rows; dr++)
                    {
                        var alt = new GridPos(dr, b.Pos.Col + behind);
                        if (IsCellFree(sim, alt) && TerrainMap.IsInside(alt)) { start = alt; break; }
                    }
                }
                if (IsCellFree(sim, start)) { u.Pos = start; u.PosF = SimVec2.FromGrid(start); }
                u.State = EntityState.Routing;
                u.HasMoveTarget = true;
                u.MoveTarget = gate ?? dest.Pos;
            }
        }

        /// <summary>溃逃目的地:同一线后方(远离敌方向)最近的己方建筑;无则己方大本营。</summary>
        static Entity FindRoutDestination(SimCore sim, Entity u)
        {
            int dir = u.Side == Side.Blue ? -1 : 1; // 远离敌方向
            Entity best = null;
            int bestD = int.MaxValue;
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Building || e.Side != u.Side) continue;
                int d = (e.Pos.Col - u.Pos.Col) * dir;
                if (d <= 0) continue; // 只取后方
                if (d < bestD) { best = e; bestD = d; }
            }
            if (best != null) return best;
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Building && e.Side == u.Side && e.Building == BuildingId.Base)
                    return e;
            return null;
        }

        public static void Tick(SimCore sim, float dt)
        {
            // 0) 城后等候自动解除:主将已死/不存在、主将距城 > 15m、或城不存在 → 离城(士兵跟随主将=截击)
            foreach (var e in sim.Entities.Values.Where(x => x.Kind == EntityKind.Unit && x.TempGarrisonId != 0).ToList())
            {
                var g = e.GeneralId != 0 ? sim.GetEntity(e.GeneralId) : null;
                bool release = g == null || g.Kind != EntityKind.Unit || g.State == EntityState.Dead;
                if (!release)
                {
                    var tb = sim.GetEntity(e.TempGarrisonId);
                    release = tb == null || tb.Kind != EntityKind.Building ||
                              SimVec2.Distance(g.PosF, SimVec2.FromGrid(tb.Pos)) > TempFollowRange;
                }
                if (release)
                {
                    var tb2 = sim.GetEntity(e.TempGarrisonId);
                    if (tb2 != null) tb2.GarrisonTemp.Remove(e.Id);
                    e.TempGarrisonId = 0;
                }
            }

            // 1) 临时驻军:正式有空缺且未封城 → 自动转正式(城守住补入正式)
            foreach (var b in sim.Entities.Values.Where(e => e.Kind == EntityKind.Building).ToList())
            {
                if (b.Sealed) continue; // 封城期间不转
                // 敌方攻城意图存续期间不转:临时驻军受保护,防止"转正后被交换杀死"
                if (b.Intent != SiegeIntent.None && b.IntentIssuer != b.Side) continue;
                int cap = b.IsFort ? 3 : (b.Building == null ? 0 : sim.BuildingConfigOf(b.Building.Value).GarrisonFormal);
                while (b.GarrisonTemp.Count > 0 && b.GarrisonFormal.Count < cap)
                {
                    var id = b.GarrisonTemp[0];
                    b.GarrisonTemp.RemoveAt(0);
                    var u = sim.GetEntity(id);
                    if (u == null) continue;
                    u.TempGarrisonId = 0;
                    DoEnterFormal(sim, u, b.Id, b.GarrisonFormal);
                }
            }

            foreach (var b in sim.Entities.Values.Where(e => e.Kind == EntityKind.Building).ToList())
            {
                var garrison = b.GarrisonFormal.Concat(b.GarrisonTemp).ToList();
                // 2) 依托射击(远程)
                foreach (var id in garrison)
                {
                    var u = sim.GetEntity(id);
                    if (u == null || u.Hero == null || !RangedHeroes.Contains(u.Hero.Value)) continue;
                    u.AttackCooldown -= dt;
                    if (u.AttackCooldown > 0f) continue;
                    var tgt = FindRangedTarget(sim, b, (int)u.Range);
                    if (tgt == null) continue;
                    ApplyDamage(sim, u, tgt);
                    u.AttackCooldown = u.AttackIntervalSec;
                }
                // 3) 近战出击:敌单位欧氏≤2.5 米(建筑中心起算)
                var bc = SimVec2.FromGrid(b.Pos);
                bool threat = sim.Entities.Values.Any(e =>
                    e.Kind == EntityKind.Unit && e.Side != b.Side &&
                    e.GarrisonBuildingId == 0 && e.State != EntityState.Dead && e.State != EntityState.Exchanging &&
                    SimVec2.Distance(e.PosF, bc) <= 2.5f);
                if (threat)
                foreach (var id in garrison)
                {
                    var u = sim.GetEntity(id);
                    if (u == null || u.Hero == null || RangedHeroes.Contains(u.Hero.Value)) continue;
                    var gate = FindGateCell(sim, b);
                    if (gate == null) break;
                    b.GarrisonFormal.Remove(id); b.GarrisonTemp.Remove(id);
                    u.Pos = gate.Value; u.PosF = SimVec2.FromGrid(gate.Value);
                    u.GarrisonBuildingId = 0; u.TempGarrisonId = 0; u.State = EntityState.Idle;
                }
            }

            // 4) 建筑脱战回复(D27):非大本营
            foreach (var b in sim.Entities.Values.Where(e => e.Kind == EntityKind.Building).ToList())
            {
                if (b.Building == BuildingId.Base) continue;
                b.SecondsSinceCombat += dt;
                if (b.SecondsSinceCombat >= RegenDelaySec && b.Hp < b.MaxHp)
                    b.Hp = System.Math.Min(b.MaxHp, b.Hp + RegenPerSec * dt);
            }
        }

        public static GridPos RallyPointOf(Side side) =>
            side == Side.Blue ? new GridPos(15, 270) : new GridPos(15, 16);

        private static void DoEnterFormal(SimCore sim, Entity u, int buildingId, List<int> list)
        {
            sim.FreeCell(u.Pos);
            u.GarrisonBuildingId = buildingId;
            u.State = EntityState.Idle;
            u.HasMoveTarget = false;
            list.Add(u.Id);
        }

        private static void DoEnterTemp(Entity u, int buildingId, List<int> list)
        {   // 城后等候:只登记,不冻结移动(士兵保持跟随主将)
            u.TempGarrisonId = buildingId;
            list.Add(u.Id);
        }

        private static void Rollback(SimCore sim, List<int> accepted, int buildingId)
        { foreach (var id in accepted) sim.GetEntity(buildingId).GarrisonFormal.Remove(id); }
        private static void RollbackTemp(SimCore sim, List<int> accepted, int buildingId)
        {
            var b = sim.GetEntity(buildingId);
            foreach (var id in accepted) { b.GarrisonTemp.Remove(id); var u = sim.GetEntity(id); if (u != null) u.TempGarrisonId = 0; }
        }

        /// <summary>城门位置(D18,M8.1b):占地矩形敌侧缘 +0.6 米列起,行优先建筑行(±ceil(HalfH) 内扩散),椭圆内、非水域,列向外延展找最近空位;全占返回 null。</summary>
        private static GridPos? FindGateCell(SimCore sim, Entity b, System.Collections.Generic.HashSet<long> exclude = null)
        {
            int dir = b.Side == Side.Blue ? 1 : -1; // 朝敌侧:蓝方敌人在 +x、红方敌人在 -x(蓝基地列 15、红基地列 271)
            var gateF = SimVec2.FromGrid(b.Pos) + new SimVec2(dir * (BuildingFootprint.HalfW(b) + 0.6f), 0f);
            int gateCol = gateF.ToGrid().Col;
            int rh = (int)System.Math.Ceiling(BuildingFootprint.HalfH(b)); // 建筑占地行范围
            int[] rows = { 0, -1, 1, -2, 2, -3, 3 }; // 行优先建筑行,向外扩散
            for (int d = 0; d <= 4; d++)
            {
                foreach (int r in rows)
                {
                    if (r < -rh || r > rh) continue;
                    var p = new GridPos(b.Pos.Row + r, gateCol + dir * d);
                    if (p.Row < 0 || p.Row >= LaneGrid.Rows || p.Col < 0 || p.Col >= LaneGrid.Cols) continue;
                    if (!TerrainMap.IsInside(p)) continue;
                    if (TerrainMap.IsWater(SimVec2.FromGrid(p))) continue;
                    if (exclude != null && exclude.Contains(((long)p.Row << 32) | (uint)p.Col)) continue; // 溃逃单位分派不同城门格
                    if (IsCellFree(sim, p)) return p;
                }
            }
            return null; // 城门区全占 → 无空位
        }

        private static bool IsCellFree(SimCore sim, GridPos p)
        {
            var c = SimVec2.FromGrid(p);
            foreach (var e in sim.Entities.Values)
            {
                if (e.State == EntityState.Dead) continue;
                if (e.Kind == EntityKind.Building && !e.Rubbled && e.Pos == p) return false;
                if (e.Kind == EntityKind.Unit && e.GarrisonBuildingId == 0
                    && SimVec2.Distance(e.PosF, c) < 1.0f) return false;
            }
            return true;
        }

        private static Entity FindRangedTarget(SimCore sim, Entity b, int range)
        {
            Entity best = null; float bestD = float.MaxValue; int bestId = int.MaxValue;
            var bc = SimVec2.FromGrid(b.Pos);
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Unit || e.Side == b.Side) continue;
                if (e.GarrisonBuildingId != 0 || e.State == EntityState.Dead || e.State == EntityState.Exchanging) continue;
                float d = SimVec2.Distance(e.PosF, bc);
                if (d > range) continue;
                if (d < bestD || (System.Math.Abs(d - bestD) <= 1e-4f && e.Id < bestId)) { best = e; bestD = d; bestId = e.Id; }
            }
            return best;
        }

        private static void ApplyDamage(SimCore sim, Entity attacker, Entity defender)
        {
            float dmg = DamageCalculator.CalculateDamage(attacker, defender, sim.Game, false);
            if (defender.Shield > 0)
            {
                float abs = System.Math.Min(defender.Shield, dmg);
                defender.Shield -= abs; dmg -= abs;
            }
            if (dmg > 0f) defender.Hp -= dmg;
            if (defender.Hp <= 0f && defender.State != EntityState.Dead)
            { defender.State = EntityState.Dead; sim.RemoveEntity(defender.Id); }
        }
    }

    // 侧别辅助
    internal static class SideUtilities
    {
        public static Side SideOf(SimCore sim, IReadOnlyList<int> unitIds) =>
            unitIds.Count > 0 && sim.Entities.ContainsKey(unitIds[0]) ? sim.GetEntity(unitIds[0]).Side : Side.Blue;
    }
}
