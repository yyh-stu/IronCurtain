using System;

namespace IronCurtain.Sim
{
    /// <summary>连续坐标(米制)。1 格 = 1 米;X=列方向,Y=行方向。</summary>
    public struct SimVec2
    {
        public float X;
        public float Y;

        public SimVec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static SimVec2 FromGrid(GridPos p) => new SimVec2(p.Col + 0.5f, p.Row + 0.5f);

        public static SimVec2 operator +(SimVec2 a, SimVec2 b) => new SimVec2(a.X + b.X, a.Y + b.Y);
        public static SimVec2 operator -(SimVec2 a, SimVec2 b) => new SimVec2(a.X - b.X, a.Y - b.Y);
        public static SimVec2 operator *(SimVec2 a, float s) => new SimVec2(a.X * s, a.Y * s);

        public float SqrMagnitude => X * X + Y * Y;
        public float Magnitude => (float)Math.Sqrt(X * X + Y * Y);

        public SimVec2 Normalized()
        {
            float m = Magnitude;
            return m > 1e-6f ? new SimVec2(X / m, Y / m) : new SimVec2(0f, 0f);
        }

        public static float Distance(SimVec2 a, SimVec2 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static float SqrDistance(SimVec2 a, SimVec2 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public static SimVec2 Lerp(SimVec2 a, SimVec2 b, float t) =>
            new SimVec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>连续坐标 → 所在格(向下取整)。</summary>
        public GridPos ToGrid()
        {
            int col = X < 0 ? 0 : (int)X;
            int row = Y < 0 ? 0 : (int)Y;
            if (col > LaneGrid.Cols - 1) col = LaneGrid.Cols - 1;
            if (row > LaneGrid.Rows - 1) row = LaneGrid.Rows - 1;
            return new GridPos(row, col);
        }

        public override string ToString() => $"({X:F2},{Y:F2})";
    }
}
