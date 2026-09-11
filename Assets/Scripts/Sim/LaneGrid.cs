namespace IronCurtain.Sim
{
    // 线单位战场。2026-09-11 制作人拍板:单线 demo 扩建为三路大地图(规格
    // glm-pipeline/specs/MAP-3LANE-layout-spec.md),包围盒 160 行 × 270 列;
    // 可玩区轮廓/河/路网/外围带由 TerrainMap 按超椭圆与三路几何判定。
    public sealed class LaneGrid
    {
        public const int Rows = 160;
        public const int Cols = 270;
        public const int MaxUnits = Rows * Cols;

        public bool InBounds(GridPos p) => p.IsValid;

        public int ToIndex(GridPos p) => p.Row * Cols + p.Col;
    }
}
