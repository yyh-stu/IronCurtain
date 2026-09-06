using IronCurtain.Sim;

namespace IronCurtain.View
{
    public enum BuildingFogState { Unknown, Stale, Fresh }

    /// <summary>D34 战争迷雾纯规则层(渲染侧信息过滤,不影响战斗判定)。禁 UnityEngine。</summary>
    public static class FogRules
    {
        public const byte FogAlphaValue = 160;   // 未照亮雾 alpha(0..255)

        public static bool UnitVisible(SimCore sim, Side player, Entity e)
        {
            if (e.Side == player) return true;
            return VisionSystem.IsUnitVisible(sim, player, e.Id);
        }

        public static BuildingFogState BuildingState(SimCore sim, Side player, Entity b)
        {
            if (b.Side == player) return BuildingFogState.Fresh;
            if (!VisionSystem.IsBuildingKnown(sim, player, b.Id)) return BuildingFogState.Unknown;
            foreach (var cell in BuildingFootprint.CellsOccupied(b))
                if (VisionSystem.IsFresh(sim, player, cell)) return BuildingFogState.Fresh;
            return BuildingFogState.Stale;
        }

        public static byte FogAlpha(SimCore sim, Side player, int row, int col)
        {
            return VisionSystem.IsFresh(sim, player, new GridPos(row, col)) ? (byte)0 : FogAlphaValue;
        }
    }
}
