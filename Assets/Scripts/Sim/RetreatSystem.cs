using System;

namespace IronCurtain.Sim
{
    /// <summary>玩家授意撤退(M8.7 13c):兵团原子撤退至最近己方非废墟据点附近站定;
    /// 撤退/溃逃期间输出 −25%、承伤 +25%(SimCore 乘区);到达后 RetreatHold 压制自动层,玩家手动命令解除。</summary>
    public static class RetreatSystem
    {
        public const float StandoffOffset = 1.2f; // 站定点=建筑边缘外 1.2 米

        /// <summary>下令撤退。id 不存在抛 InvalidOperationException(与 SetMoveTarget 同风格);
        /// 士兵/死亡/驻军中/交换中/无非废墟己方建筑 → return false(不动状态)。</summary>
        public static bool OrderRetreat(SimCore sim, int generalId)
        {
            if (!sim.Entities.TryGetValue(generalId, out var g))
                throw new InvalidOperationException($"实体不存在: {generalId}");
            if (g.Kind != EntityKind.Unit || g.IsSoldier || g.State == EntityState.Dead) return false;
            if (g.GarrisonBuildingId != 0 || g.State == EntityState.Exchanging) return false;

            Entity dest = null; float bestD = float.MaxValue;
            foreach (var b in sim.Entities.Values)
            {
                if (b.Kind != EntityKind.Building || b.Side != g.Side || b.Rubbled) continue;
                float d = SimVec2.Distance(g.PosF, b.PosF);
                if (d < bestD - 1e-4f) { dest = b; bestD = d; }
            }
            if (dest == null) return false;

            var dir = g.PosF - dest.PosF;
            if (dir.Magnitude < 1e-4f) dir = g.Side == Side.Blue ? new SimVec2(-1f, 0f) : new SimVec2(1f, 0f);
            dir = dir.Normalized();
            float half = System.Math.Max(BuildingFootprint.HalfW(dest), BuildingFootprint.HalfH(dest));
            SimVec2 stand = TerrainMap.ClampToEllipse(dest.PosF + dir * (half + StandoffOffset));

            g.State = EntityState.Routing;
            g.HasMoveTarget = true;
            g.MoveTarget = stand.ToGrid();
            g.Detour = null; g.DetourTicks = 0; g.StuckTicks = 0;
            g.AutoKind = 0; g.AutoTargetId = 0; // 撤退即玩家意志,清自动决策
            g.RetreatHold = true;

            foreach (var s in sim.Entities.Values)
                if (s.Kind == EntityKind.Unit && s.IsSoldier && s.GeneralId == g.Id
                    && s.State != EntityState.Dead && s.GarrisonBuildingId == 0)
                    s.State = EntityState.Routing; // 兵团原子:士兵同步撤退(减益+只被迫接战)
            return true;
        }
    }
}
