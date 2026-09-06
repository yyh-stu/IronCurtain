using System;

namespace IronCurtain.Sim
{
    /// <summary>椭圆大地图地形规则(权威规格,详见 M6.5)。</summary>
    public static class TerrainMap
    {
        public const int Rows = 30;
        public const int Cols = 286;

        // 椭圆参数
        public const float CenterX = 143f;
        public const float CenterY = 15f;
        public const float HalfX = 143f;
        public const float HalfY = 15f;

        // 河(楚河汉界)
        public const float RiverMinX = 138f;
        public const float RiverMaxX = 148f;
        // 桥
        public const float BridgeTopMinY = 25f;
        public const float BridgeBotMaxY = 5f;
        // 中央岛
        public const float IsleMinX = 139f;
        public const float IsleMaxX = 147f;
        public const float IsleMinY = 11f;
        public const float IsleMaxY = 19f;

        public static bool IsInside(SimVec2 v)
        {
            float dx = (v.X - CenterX) / HalfX;
            float dy = (v.Y - CenterY) / HalfY;
            return dx * dx + dy * dy <= 1f;
        }

        public static bool IsInside(GridPos p)
        {
            return IsInside(SimVec2.FromGrid(p));
        }

        public static bool IsBridge(SimVec2 v)
        {
            if (v.X < RiverMinX || v.X >= RiverMaxX) return false;
            return v.Y >= BridgeTopMinY || v.Y < BridgeBotMaxY;
        }

        public static bool IsIsland(SimVec2 v)
        {
            return v.X >= IsleMinX && v.X < IsleMaxX && v.Y >= IsleMinY && v.Y < IsleMaxY;
        }

        public static bool IsWater(SimVec2 v)
        {
            if (v.X < RiverMinX || v.X >= RiverMaxX) return false;
            return !IsBridge(v) && !IsIsland(v);
        }

        public static float MoveFactor(SimVec2 v)
        {
            return IsWater(v) ? 0.5f : 1f;
        }

        /// <summary>径向投影:把 v 推回椭圆(含边界)。</summary>
        public static SimVec2 ClampToEllipse(SimVec2 v)
        {
            if (IsInside(v)) return v;
            float dx = v.X - CenterX;
            float dy = v.Y - CenterY;
            // 防退化:恰在中心时向右推
            if (dx == 0f && dy == 0f) return new SimVec2(CenterX + HalfX, CenterY);
            double a = HalfX, b = HalfY;
            // 参数角:取点方向,然后牛顿迭代求边界到方向射线的交点
            double theta = Math.Atan2(dy / b, dx / a);
            double ct = Math.Cos(theta), st = Math.Sin(theta);
            // 边界点: x = a*ct, y = b*st (椭圆参数化)
            float bx = (float)(CenterX + a * ct);
            float by = (float)(CenterY + b * st);
            // 参数化点保证在椭圆上;再向内收一点点避免浮点误差
            var r = new SimVec2(bx, by);
            if (!IsInside(r))
                r = new SimVec2((bx - CenterX) * 0.999f + CenterX, (by - CenterY) * 0.999f + CenterY);
            return r;
        }
    }
}
