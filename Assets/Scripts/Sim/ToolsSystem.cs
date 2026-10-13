using System;
using System.Collections.Generic;
using System.Linq;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 道具系统(2026-09-16 三十七轮从 HexSystem 拆出:道具与海克斯是两套系统,
    /// 旧代码把 UseRam/UseFort 写在 HexSystem 里属模块错位;拆出后堡垒的生成/脉冲/到期
    /// 运行态也统一归这里。铁壁防线已于同日拍板改为增益卡,堡垒仅为道具临时阵地所有)。
    ///
    /// 攻城锥:点选敌方建筑直伤(15);临时阵地:限时堡垒(30s,持续脉冲伤敌,到期消失)。
    /// 放置校验(2026-09-16 制作人终审):越界 / 地图轮廓外 / 水域 /
    /// **非三条路且非己方大本营区域**(路上向敌延伸 = 最前己方完好建筑视野内)/ 格子被占 / EP 不足。
    /// </summary>
    public static class ToolsSystem
    {
        private static int Ticks(SimCore sim, double seconds) =>
            Math.Max(1, (int)Math.Ceiling(seconds * sim.Config.TickRate));

        /// <summary>放置合法性 reason 常量(2026-09-17 契约 4R:单一来源,调用方不得硬编码字面量)。</summary>
        public static class PlacementReason
        {
            public const string OutOfBounds = "格子越界";
            public const string OutsideMap = "地图轮廓外不可放置";
            public const string Water = "水域不可放置";
            public const string NotLaneOrPlaza = "只能放在三条路或己方大本营区域(路上不超出最前己方建筑视野)";
            public const string Occupied = "格子被占用";
        }

        /// <summary>敌方完好建筑控区半径已废:2026-09-16 制作人终审改为
        /// 「三条路 + 己方大本营区域;路上向敌延伸 = 最前己方完好建筑视野内」(见 PlacementBlocked)。</summary>

        // ---------- 攻城锥 ----------
        public static bool UseRam(SimCore sim, Side side, int buildingId, out string error)
        {
            error = null;
            if (sim == null || sim.Game == null) { error = "道具状态无效"; return false; }
            if (side == Side.Neutral) { error = "阵营无效"; return false; }
            if (!sim.Entities.TryGetValue(buildingId, out var b) || b.Kind != EntityKind.Building)
            { error = "目标无效"; return false; }
            if (b.Rubbled || b.Side == Side.Neutral || b.Side == side)
            { error = "目标必须是敌方建筑"; return false; }
            if (!sim.SpendEp(side, sim.Game.Ep.RamCost)) { error = "能量不足"; return false; }
            DestroySystem.ApplyBuildingDamage(sim, b, (float)sim.Game.Tools.RamDamage, side);
            return true;
        }

        // ---------- 临时阵地 ----------
        public static bool UseFort(SimCore sim, Side side, GridPos cell, out string error)
        {
            error = null;
            if (sim == null || sim.Game == null) { error = "道具状态无效"; return false; }
            if (side == Side.Neutral) { error = "阵营无效"; return false; }
            if (PlacementBlocked(sim, side, cell, out var reason))
            { error = "目标无效:" + reason; return false; }
            if (!sim.SpendEp(side, sim.Game.Ep.FortCost)) { error = "能量不足"; return false; }
            SpawnFort(sim, side, cell, sim.Game.Tools.FortHp);
            return true;
        }

        /// <summary>统一放置校验(2026-09-16 制作人终审):
        /// 只能放在 **三条路或己方大本营区域**;在路上的向敌延伸 = **最前己方完好建筑的视野圆盘内**。
        /// 视野半径与 VisionSystem 同一权威(大本营 40 / 箭塔 24 / 其余 20),
        /// 于是"占领前线箭塔 → 阵地随视野前推"天然成立。敌方大本营区域不可放。</summary>
        public static bool PlacementBlocked(SimCore sim, Side side, GridPos cell, out string reason)
        {
            if (cell.Row < 0 || cell.Row >= LaneGrid.Rows || cell.Col < 0 || cell.Col >= LaneGrid.Cols)
            { reason = PlacementReason.OutOfBounds; return true; }
            if (!TerrainMap.IsInside(cell))
            { reason = PlacementReason.OutsideMap; return true; }
            if (TerrainMap.IsWater(SimVec2.FromGrid(cell)))
            { reason = PlacementReason.Water; return true; }
            if (!InOwnPlazaOrLaneReach(sim, side, cell))
            { reason = PlacementReason.NotLaneOrPlaza; return true; }
            if (CellOccupied(sim, cell))
            { reason = PlacementReason.Occupied; return true; }
            reason = null;
            return false;
        }

        private static bool InOwnPlazaOrLaneReach(SimCore sim, Side side, GridPos cell)
        {
            var p = SimVec2.FromGrid(cell);
            if (TerrainMap.InBasePlaza(p, side)) return true;   // 己方大本营区域(石路圆盘)
            bool onLane = TerrainMap.OnLane(p, 0) || TerrainMap.OnLane(p, 1) || TerrainMap.OnLane(p, 2);
            if (!onLane) return false;

            // 最前己方完好建筑(蓝取最大 X / 红取最小 X;含占领来的建筑与己方阵地)
            Entity front = null;
            float bestX = side == Side.Blue ? float.MinValue : float.MaxValue;
            foreach (var b in sim.Entities.Values)
            {
                if (b == null || b.Kind != EntityKind.Building || b.Rubbled || b.Side != side) continue;
                float x = SimVec2.FromGrid(b.Pos).X;
                if (side == Side.Blue ? x > bestX : x < bestX) { bestX = x; front = b; }
            }
            if (front == null) return false;   // 没有任何己方完好建筑(理论不会发生:基地恒在)
            return SimVec2.Distance(p, SimVec2.FromGrid(front.Pos)) <= VisionSystem.BuildingVisionRadius(front);
        }

        // ---------- 堡垒运行态(道具临时阵地专用;2026-09-16 制作人拍板铁壁防线改为增益卡,不再生成堡垒) ----------
        /// <summary>生成堡垒并登记运行态(道具口径 Tools.FortHp=100)。
        /// 静默生成 + 延迟通告(2026-09-14 同款修复):IsFort 必须在视图建 Bind **之前**就位,
        /// 否则视图在 SpawnEntity 事件里同步创建时 Building=null → 落进箭塔兜底 ——
        /// 这正是"堡垒渲染成箭塔"(1c)的真正机制,与 EntityView 的分支无关。</summary>
        internal static Entity SpawnFort(SimCore sim, Side side, GridPos cell, float hp)
        {
            var f = sim.SpawnEntity(side, EntityKind.Building, cell, announce: false);
            f.IsFort = true;
            f.Building = null;
            f.Hp = f.MaxHp = hp;
            sim.AnnounceSpawn(f);
            sim.HexRuntime.Forts.Add(new HexRuntimeState.FortEntry
            {
                EntityId = f.Id, Side = side,
                ExpireTick = sim.Tick + Ticks(sim, sim.Game.Tools.FortDurationSeconds),
                // 脉冲伤害沿用 Tools.RamDamage(=15;道具堡垒持续攻击,2026-09-11 制作人口径)
                NextPulseTick = sim.Tick + Ticks(sim, 3.0)
            });
            return f;
        }

        /// <summary>按条件移除堡垒(放兵出城 + 实体移除 + 运行态摘除)。HexSystem.Reset 用 (null) 全清。</summary>
        internal static void RemoveFortsOf(SimCore sim, Side? side)
        {
            var forts = sim.HexRuntime.Forts;
            for (int i = forts.Count - 1; i >= 0; i--)
            {
                var f = forts[i];
                if (side.HasValue && f.Side != side.Value) continue;
                var entity = sim.GetEntity(f.EntityId);
                if (entity != null)
                {
                    ReleaseGarrison(sim, entity);
                    sim.RemoveEntity(entity.Id);
                }
                forts.RemoveAt(i);
            }
        }

        /// <summary>堡垒脉冲与到期:每 3 秒对 2 米内敌军结算 RamDamage;到期放兵出城并消失;
        /// 被打掉的堡垒由 DestroySystem 直接移除(2026-09-11 制作人:不留废墟),这里只做兜底摘除。</summary>
        public static void Tick(SimCore sim, float dt)
        {
            if (sim == null || sim.Game == null) return;
            var forts = sim.HexRuntime.Forts;
            for (int i = forts.Count - 1; i >= 0; i--)
            {
                var f = forts[i];
                var b = sim.GetEntity(f.EntityId);
                if (b == null || b.Rubbled) { forts.RemoveAt(i); continue; }
                if (sim.Tick >= f.NextPulseTick)
                {
                    f.NextPulseTick += Ticks(sim, 3.0);
                    // 2026-09-17 特效契约(已批):脉冲通知 —— 只读事件,伤害路径不变(仍直接扣血、
                    // 不经聚合、与 SettledDamage 互不覆盖)。
                    sim.Emit(new SimEvent(sim.Tick, SimEventType.FortPulsed, b.Id));
                    foreach (var e in sim.Entities.Values)
                    {
                        if (e.Side == b.Side || e.Kind != EntityKind.Unit || e.State == EntityState.Dead) continue;
                        if (SimVec2.Distance(e.PosF, SimVec2.FromGrid(b.Pos)) <= 2f)
                        {
                            e.Hp -= (float)sim.Game.Tools.RamDamage;
                            if (e.Hp <= 0) { e.Hp = 0; e.State = EntityState.Dead; }
                        }
                    }
                }
                if (sim.Tick >= f.ExpireTick)
                {
                    ReleaseGarrison(sim, b);
                    sim.RemoveEntity(b.Id);
                    forts.RemoveAt(i);
                }
            }
        }

        private static void ReleaseGarrison(SimCore sim, Entity fort)
        {
            foreach (var id in fort.GarrisonFormal.Concat(fort.GarrisonTemp).ToList())
            {
                var u = sim.GetEntity(id);
                if (u == null) continue;
                u.GarrisonBuildingId = 0;
                u.TempGarrisonId = 0;
                u.State = EntityState.Idle;
                var free = FindFreeCellNear(sim, fort.Pos);
                if (free.HasValue)
                {
                    u.Pos = free.Value;
                    u.PosF = SimVec2.FromGrid(free.Value);
                }
            }
            fort.GarrisonFormal.Clear();
            fort.GarrisonTemp.Clear();
        }

        // ---------- 放置/空格工具 ----------
        internal static bool CellOccupied(SimCore sim, GridPos cell)
        {
            foreach (var e in sim.Entities.Values)
            {
                if (e.Pos.Row != cell.Row || e.Pos.Col != cell.Col) continue;
                // SpawnEntity keeps a building's grid reservation while it is rubble.
                // Match that authority here so a failed fort spawn cannot occur after EP spend.
                if (e.Kind == EntityKind.Building) return true;
                if (e.State != EntityState.Dead && e.GarrisonBuildingId == 0) return true;
            }
            return false;
        }

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
    }
}
