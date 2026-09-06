namespace IronCurtain.Sim
{
    // 线单位战场:两座相邻建筑之间为独立线空间;单线 demo 将整线做成连续棋盘。
    // 2026-09-05 用户拍板:改为椭圆形大地图,矩形包围盒 30 行 × 286 列(椭圆判定见 TerrainMap)。
    public sealed class LaneGrid
    {
        public const int Rows = 30;
        public const int Cols = 286;
        public const int MaxUnits = Rows * Cols;

        public bool InBounds(GridPos p) => p.IsValid;

        public int ToIndex(GridPos p) => p.Row * Cols + p.Col;
    }
}
