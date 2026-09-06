using System;
using System.Collections.Generic;
using System.Linq;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 海克斯系统 + 道具(攻城锥 / 临时阵地)。静态有状态,Reset() 清空。
    /// </summary>
    public static class HexSystem
    {
        private class SideState
        {
            public HexId? Active;
            public float Remaining;
            public double ActivatedAtSec;
            public List<double> VengeanceExpiry = new List<double>(); // 每层过期时刻(秒,sim 时间)
            public int SandbagUnitId;                                 // 0 = 无
            public double SandbagExpiry;
            public HashSet<int> UsedCards = new HashSet<int>();
        }

        private class FortEntry
        {
            public int EntityId;
            public double ExpireSec;
            public double NextPulseSec;
        }

        private static readonly SideState[] _s = { new SideState(), new SideState() };
        private static readonly List<FortEntry> _forts = new List<FortEntry>();
        private static double _timeSec;

        private static SideState St(Side side) => side == Side.Blue ? _s[0] : _s[1];

        // ---------- 查询接口 ----------
        public static HexId? ActiveOf(Side side) => St(side).Active;
        public static float ActiveRemainingOf(Side side) => St(side).Remaining;

        public static void Reset()
        {
            _s[0] = new SideState();
            _s[1] = new SideState();
            _forts.Clear();
            _timeSec = 0;
        }

        // ---------- 抽取 ----------
        public static bool CanDraw(SimCore sim, Side side)
        {
            if (sim.Tick < sim.Game.Hexes[0].UnlockMinute * 60 * 10) return false; // 1800
            foreach (var hex in sim.Game.Hexes)
            {
                foreach (var node in hex.DrawNodes)
                {
                    int n = node * 600; // 节点单位=分钟 → tick
                    if (sim.Tick >= n && sim.Tick < n + 100)
                    {
                        return sim.EpOf(side) >= sim.Game.Ep.HexDrawCost;
                    }
                }
            }
            return false;
        }

        public static List<HexId> DrawChoices(SimCore sim, Side side)
        {
            var st = St(side);
            var pool = sim.Game.Hexes.ToList();
            var picked = new List<HexId>();
            while (picked.Count < 3 && pool.Count > 0)
            {
                double total = 0;
                var weights = new double[pool.Count];
                for (int i = 0; i < pool.Count; i++)
                {
                    weights[i] = st.UsedCards.Contains((int)pool[i].Id) ? pool[i].UsedWeightPenalty : 1.0;
                    total += weights[i];
                }
                double r = sim.Rng.NextDouble01() * total;
                int idx = 0;
                double acc = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    acc += weights[i];
                    if (r < acc) { idx = i; break; }
                }
                picked.Add(pool[idx].Id);
                pool.RemoveAt(idx);
            }
            return picked;
        }

        // ---------- 激活 ----------
        public static bool UseHex(SimCore sim, Side side, HexId card, int targetUnitId, out string error)
        {
            error = null;
            var st = St(side);
            if (!sim.SpendEp(side, sim.Game.Ep.HexDrawCost)) { error = "能量不足"; return false; }

            if (card == HexId.Sandbag)
            {
                var e = sim.Entities.TryGetValue(targetUnitId, out var t) ? t : null;
                if (e == null || e.Side != side || e.Kind != EntityKind.Unit)
                { error = "目标无效"; return false; }
            }

            // 覆盖旧卡:回收旧效果
            Deactivate(sim, side);

            st.Active = card;
            st.Remaining = (float)sim.Game.Hexes.First(h => h.Id == card).DurationSeconds;
            st.ActivatedAtSec = _timeSec;
            st.UsedCards.Add((int)card);

            if (card == HexId.IronWall)
            {
                // 立即生成堡垒(临时阵地)于主基地旁空闲格
                var cell = FindFreeCellNear(sim, side);
                if (cell.HasValue)
                {
                    var f = sim.SpawnEntity(side, EntityKind.Building, cell.Value);
                    f.IsFort = true;
                    f.Building = null; f.IsFort = true;
                    f.Hp = f.MaxHp = sim.Game.Tools.FortHp;
                    _forts.Add(new FortEntry { EntityId = f.Id, ExpireSec = _timeSec + sim.Game.Tools.FortDurationSeconds, NextPulseSec = _timeSec + 3.0 });
                }
            }
            if (card == HexId.Sandbag)
            {
                st.SandbagUnitId = targetUnitId;
                st.SandbagExpiry = _timeSec + sim.Game.Tools.FortDurationSeconds; // 30s
            }
            return true;
        }

        private static void Deactivate(SimCore sim, Side side)
        {
            var st = St(side);
            st.Active = null;
            st.Remaining = 0;
            st.VengeanceExpiry.Clear();
            st.SandbagUnitId = 0;
        }

        // ---------- 计时 ----------
        public static void Tick(SimCore sim, float dt)
        {
            _timeSec += dt;
            for (int i = 0; i < 2; i++)
            {
                var side = i == 0 ? Side.Blue : Side.Red;
                var st = _s[i];
                if (st.Active != null)
                {
                    st.Remaining -= dt;
                    st.VengeanceExpiry.RemoveAll(t => t <= _timeSec);
                    if (st.Remaining <= 0) Deactivate(sim, side);
                }
                if (st.SandbagUnitId != 0 && _timeSec >= st.SandbagExpiry) st.SandbagUnitId = 0;
            }

            // 堡垒:脉冲伤害 + 到期
            for (int i = _forts.Count - 1; i >= 0; i--)
            {
                var f = _forts[i];
                var b = sim.Entities.TryGetValue(f.EntityId, out var fort) ? fort : null;
                if (b == null || b.Rubbled) { _forts.RemoveAt(i); continue; }

                if (_timeSec >= f.NextPulseSec)
                {
                    f.NextPulseSec += 3.0;
                    foreach (var e in sim.Entities.Values)
                    {
                        if (e.Side == b.Side || e.Kind != EntityKind.Unit || e.State == EntityState.Dead) continue;
                        if (SimVec2.Distance(e.PosF, SimVec2.FromGrid(b.Pos)) <= 2f)
                        {
                            e.Hp -= (float)sim.Game.Tools.RamDamage; // 15 直伤(护盾层由 ApplyAttack 之外的简化:直接扣血)
                            if (e.Hp <= 0) { e.Hp = 0; e.State = EntityState.Dead; }
                        }
                    }
                }
                if (_timeSec >= f.ExpireSec)
                {
                    ReleaseGarrison(sim, b);
                    sim.RemoveEntity(b.Id);
                    _forts.RemoveAt(i);
                }
            }
        }

        private static void ReleaseGarrison(SimCore sim, Entity fort)
        {
            foreach (var id in fort.GarrisonFormal.Concat(fort.GarrisonTemp).ToList())
            {
                if (!sim.Entities.TryGetValue(id, out var u)) continue;
                u.GarrisonBuildingId = 0;
                u.State = EntityState.Idle;
                var free = FindFreeCellNear(sim, fort.Pos);
                if (free.HasValue) u.Pos = free.Value;
            }
            fort.GarrisonFormal.Clear();
            fort.GarrisonTemp.Clear();
        }

        // ---------- 效果查询 ----------
        public static float MoveMultiplierOf(SimCore sim, Entity e)
        {
            float m = 1f;
            var st = St(e.Side);
            if (st.Active == HexId.SteelTide && e.Hp >= 100) m *= 1.5f;
            if (st.Active == HexId.LightMobility && NearOwnBuilding(sim, e, 10)) m *= 2f;
            if (st.Active == HexId.HoldFast && NearOwnBuilding(sim, e, 10)) m *= 1.1f;
            if (st.Active == HexId.Vengeance) m *= 1f + 0.05f * st.VengeanceExpiry.Count;
            return m;
        }

        public static float OutputBonusOf(SimCore sim, Entity e)
        {
            float b = 0f;
            var st = St(e.Side);
            if (st.Active == HexId.Vengeance) b += 0.05f * st.VengeanceExpiry.Count;
            if (st.Active == HexId.HoldFast && NearOwnBuilding(sim, e, 10)) b += 0.10f;
            if (st.SandbagUnitId == e.Id) b -= 0.5f;
            return b;
        }

        public static float DamageTakenBonusOf(SimCore sim, Entity e)
        {
            float b = 0f;
            var st = St(e.Side);
            if (st.Active == HexId.Vengeance) b -= 0.05f * st.VengeanceExpiry.Count;
            if (st.Active == HexId.HoldFast && NearOwnBuilding(sim, e, 10)) b -= 0.10f;
            return b;
        }

        public static float DamageReductionOf(SimCore sim, Entity e)
        {
            float r = 1f;
            var st = St(e.Side);
            if (st.Active == HexId.SteelTide && SteelTideWindowActive(st)) r *= 0.5f;
            if (st.SandbagUnitId == e.Id) r *= 0.5f;
            return r;
        }

        private static bool SteelTideWindowActive(SideState st)
            => _timeSec - st.ActivatedAtSec < 3.0; // 前 30 tick = 3 秒

        public static double ProductionSecondsMultiplier(SimCore sim, Side side)
            => St(side).Active == HexId.RapidDeploy ? 1.0 / 1.4 : 1.0;

        public static int PopCapBonusOf(SimCore sim, Side side)
        {
            if (St(side).Active != HexId.RapidDeploy) return 0;
            // 用基础值计算,禁止回调 PopulationCapOf(会递归)
            int baseCap = (int)sim.Game.Production.BasePopulation
                        + sim.Game.Production.ResourcePointPopulation * sim.OwnedResourcePointsOf(side);
            return (int)Math.Round(baseCap * 0.20);
        }

        public static bool TowerExempt(SimCore sim, Side side) => St(side).Active == HexId.HiddenPath;

        public static bool DebuffImmune(SimCore sim, Entity e) => St(e.Side).Active == HexId.SteelTide;

        public static void OnUnitDied(SimCore sim, Side side)
        {
            var st = St(side);
            if (st.Active == HexId.Vengeance) st.VengeanceExpiry.Add(_timeSec + 15.0);
        }

        // ---------- 道具 ----------
        public static bool UseRam(SimCore sim, Side side, int buildingId, out string error)
        {
            error = null;
            if (!sim.Entities.TryGetValue(buildingId, out var b) || b.Kind != EntityKind.Building)
            { error = "目标无效"; return false; }
            if (b.Rubbled || b.Side == Side.Neutral || b.Side == side)
            { error = "目标必须是敌方建筑"; return false; }
            if (!sim.SpendEp(side, sim.Game.Ep.RamCost)) { error = "能量不足"; return false; }
            DestroySystem.ApplyBuildingDamage(sim, b, (float)sim.Game.Tools.RamDamage, side);
            return true;
        }

        public static bool UseFort(SimCore sim, Side side, GridPos cell, out string error)
        {
            error = null;
            if (CellOccupied(sim, cell)) { error = "目标无效:格子被占用"; return false; }
            if (!sim.SpendEp(side, sim.Game.Ep.FortCost)) { error = "能量不足"; return false; }
            var f = sim.SpawnEntity(side, EntityKind.Building, cell);
            f.IsFort = true;
            f.Building = null; f.IsFort = true;
            f.Hp = f.MaxHp = sim.Game.Tools.FortHp;
            _forts.Add(new FortEntry { EntityId = f.Id, ExpireSec = _timeSec + sim.Game.Tools.FortDurationSeconds, NextPulseSec = _timeSec + 3.0 });
            return true;
        }

        // ---------- 工具 ----------
        private static bool NearOwnBuilding(SimCore sim, Entity e, int range)
        {
            foreach (var b in sim.Entities.Values)
                if (b.Kind == EntityKind.Building && b.Side == e.Side && !b.Rubbled
                    && SimVec2.Distance(e.PosF, SimVec2.FromGrid(b.Pos)) <= range)
                    return true;
            return false;
        }

        private static bool CellOccupied(SimCore sim, GridPos cell)
        {
            foreach (var e in sim.Entities.Values)
                if (e.Pos.Row == cell.Row && e.Pos.Col == cell.Col && !b_RubbleSkip(e)) return true;
            return false;
        }

        private static bool b_RubbleSkip(Entity e) => e.Rubbled;

        private static GridPos? FindFreeCellNear(SimCore sim, GridPos anchor)
        {
            for (int d = 0; d <= 10; d++)
            {
                for (int dr = 0; dr < LaneGrid.Rows; dr++)
                {
                    var c = new GridPos(dr, anchor.Col + d);
                    if (c.Col > LaneGrid.Cols - 1) break;
                    if (!CellOccupied(sim, c)) return c;
                    var c2 = new GridPos(dr, anchor.Col - d);
                    if (c2.Col >= 0 && !CellOccupied(sim, c2)) return c2;
                }
            }
            return null;
        }

        private static GridPos? FindFreeCellNear(SimCore sim, Side side)
        {
            GridPos basePos = side == Side.Blue ? new GridPos(3, 3) : new GridPos(3, LaneGrid.Cols - 4);
            for (int d = 1; d <= LaneGrid.Cols - 2; d++)
            {
                var c = new GridPos(basePos.Row, basePos.Col + (side == Side.Blue ? d : -d));
                if (c.Col < 0 || c.Col > LaneGrid.Cols - 1) break;
                if (!CellOccupied(sim, c)) return c;
            }
            return null;
        }
    }
}
