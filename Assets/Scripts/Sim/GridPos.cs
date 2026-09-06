namespace IronCurtain.Sim
{
    // 值类型保证事件与实体位置复制的确定性,避免引用共享
    public struct GridPos
    {
        public int Row;
        public int Col;

        public GridPos(int row, int col) { Row = row; Col = col; }

        public bool IsValid => Row >= 0 && Row < LaneGrid.Rows && Col >= 0 && Col < LaneGrid.Cols;

        public static bool operator ==(GridPos a, GridPos b) => a.Row == b.Row && a.Col == b.Col;
        public static bool operator !=(GridPos a, GridPos b) => !(a == b);

        public override bool Equals(object obj) => obj is GridPos p && this == p;
        public override int GetHashCode() => Row * 31 + Col;
        public override string ToString() => $"({Row},{Col})";
    }
}
