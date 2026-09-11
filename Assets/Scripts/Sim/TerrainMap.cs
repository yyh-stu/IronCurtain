using System;

namespace IronCurtain.Sim
{
    /// <summary>地表类型(只读查询,铺路/小地图用;Sim 权威)。</summary>
    public enum SurfaceKind { Road, Grass, Sand, Water, Bridge }

    /// <summary>
    /// 三路大地图地形规则(2026-09-11 制作人拍板,规格 MAP-3LANE-layout-spec.md v4)。
    /// 坐标:x∈[0,270) 列(蓝基地在左、红基地在右),y∈[0,160) 行(上=Row 小)。
    /// 核心:超椭圆轮廓 p=1.8(菱形磨圆四角)、中线河贯全高、上下路沿边缘弧度距边 10、中路居中。
    /// </summary>
    public static class TerrainMap
    {
        public const int Rows = 160;
        public const int Cols = 270;

        // ---- 轮廓(超椭圆 |dx/a|^p + |dy/b|^p <= 1,p=1.8 = 菱形磨圆四角) ----
        public const float CenterX = 135f;
        public const float CenterY = 80f;
        public const float HalfX = 135f;
        public const float HalfY = 80f;
        public const float P = 1.8f;

        // ---- 河(中线,贯全高;可涉水) ----
        public const float RiverHalfWidth = 5f;        // 河宽 10
        public const float RiverMinX = CenterX - RiverHalfWidth;
        public const float RiverMaxX = CenterX + RiverHalfWidth;
        public const float WadeMoveFactor = 0.7f;      // 涉水 −30%

        // ---- 桥:三路各一,桥宽=车道宽 14 ----
        public const float BridgeHalfWidth = 7f;

        // ---- 外围带(自河岸向外):沙 10 / 草 8 / 石路 12 ----
        public const float BandSand = 10f;
        public const float BandGrass = 8f;
        public const float BandRoad = 12f;

        // ---- 三路 ----
        public const float LaneHalfWidth = 7f;         // 车道宽 14
        public const float EdgeInset = 10f;            // 上下路距地图边缘

        /// <summary>轮廓在 x 处的半高。</summary>
        public static float OutlineHalfY(float x)
        {
            double t = Math.Min(1d, Math.Abs(x - CenterX) / HalfX);
            return (float)(HalfY * Math.Pow(Math.Max(0d, 1d - Math.Pow(t, P)), 1d / P));
        }

        /// <summary>lane: 0 上(Row 小侧) / 1 中 / 2 下。上下路=轮廓内缩 10 的偏移曲线。</summary>
        public static float LaneY(float x, int lane)
        {
            if (lane == 1) return CenterY;
            float off = Math.Max(0f, OutlineHalfY(x) - EdgeInset);
            return lane == 0 ? CenterY - off : CenterY + off;
        }

        public static bool OnLane(SimVec2 v, int lane)
        {
            return Math.Abs(v.Y - LaneY(v.X, lane)) <= LaneHalfWidth;
        }

        /// <summary>桥中心行 = 该路过河处的 LaneY。</summary>
        public static float BridgeCenterY(int lane) { return LaneY(CenterX, lane); }

        public static bool IsBridge(SimVec2 v)
        {
            if (v.X < RiverMinX || v.X >= RiverMaxX) return false;
            for (int lane = 0; lane < 3; lane++)
                if (Math.Abs(v.Y - BridgeCenterY(lane)) <= BridgeHalfWidth) return true;
            return false;
        }

        /// <summary>中央岛(旧 demo 遗产):三路地图无中央岛,恒 false。保留签名兼容旧调用。</summary>
        public static bool IsIsland(SimVec2 v) { return false; }

        public static bool IsWater(SimVec2 v)
        {
            if (v.X < RiverMinX || v.X >= RiverMaxX) return false;
            return !IsBridge(v);
        }

        public static bool IsInside(SimVec2 v)
        {
            double dx = Math.Abs(v.X - CenterX) / HalfX, dy = Math.Abs(v.Y - CenterY) / HalfY;
            return Math.Pow(dx, P) + Math.Pow(dy, P) <= 1d;
        }

        public static bool IsInside(GridPos p) => IsInside(SimVec2.FromGrid(p));

        /// <summary>涉水 ×0.7(制作人:全河段可涉水,速度 −30%);桥/陆 ×1。</summary>
        public static float MoveFactor(SimVec2 v)
        {
            return IsWater(v) ? WadeMoveFactor : 1f;
        }

        /// <summary>径向投影:超椭圆外 → 沿射线推回边界(牛顿迭代,旧椭圆版同思路)。</summary>
        public static SimVec2 ClampToEllipse(SimVec2 v)
        {
            if (IsInside(v)) return v;
            double dx = v.X - CenterX, dy = v.Y - CenterY;
            if (dx == 0d && dy == 0d) return new SimVec2(CenterX + HalfX, CenterY);
            double theta = Math.Atan2(dy / HalfY, dx / HalfX);
            double bx = HalfX * Math.Cos(theta), by = HalfY * Math.Sin(theta); // 参数化初值
            for (int i = 0; i < 8; i++)
            {
                double f = Math.Pow(Math.Abs(bx) / HalfX, P) + Math.Pow(Math.Abs(by) / HalfY, P) - 1d;
                if (Math.Abs(f) < 1e-9) break;
                double gx = P * Math.Pow(Math.Abs(bx) / HalfX, P - 1) / HalfX * Math.Sign(bx);
                double gy = P * Math.Pow(Math.Abs(by) / HalfY, P - 1) / HalfY * Math.Sign(by);
                double step = f / (gx * gx + gy * gy + 1e-12);
                bx -= step * gx; by -= step * gy;
            }
            var r = new SimVec2((float)(CenterX + bx), (float)(CenterY + by));
            if (!IsInside(r))
                r = new SimVec2((float)(CenterX + bx * 0.999), (float)(CenterY + by * 0.999));
            return r;
        }

        /// <summary>
        /// 地表类型(只读,给 View 铺路;2026-09-11 新增公共接口,授权记录见 PROJECT_STATE)。
        /// 优先级:桥 &gt; 河 &gt; 车道石路 &gt; 主城广场石路 &gt; 外围带 &gt; 草地。
        /// </summary>
        public static SurfaceKind SurfaceOf(SimVec2 v)
        {
            if (IsBridge(v)) return SurfaceKind.Bridge;
            if (IsWater(v)) return SurfaceKind.Water;
            if (!IsInside(v)) return SurfaceKind.Grass;
            for (int lane = 0; lane < 3; lane++)
                if (OnLane(v, lane)) return SurfaceKind.Road;
            if (InBasePlaza(v, Side.Blue) || InBasePlaza(v, Side.Red)) return SurfaceKind.Road;
            float d = Math.Abs(v.X - CenterX) - RiverHalfWidth;
            if (d >= 0 && d < BandSand) return SurfaceKind.Sand;
            if (d >= BandSand && d < BandSand + BandGrass) return SurfaceKind.Grass;
            // 2026-09-11 修正（制作人：「两方草坪上都有个从上往下走的类似沙地样式的东西，换成草地」）：
            // 这里原本还有一条「前线石路带」(d ∈ [BandSand+BandGrass, +BandRoad))——
            // 带内**不在车道上的一律判成 Sand**，于是图上出现两条贯全高的 12 米竖沙带
            // (x∈[100,112] 与 [158,170])，正好横在两侧草坪上。
            // 车道在函数开头已由 OnLane 判成 Road，该带属冗余，故删除；带内非车道部分归入草地。
            return SurfaceKind.Grass;
        }

        public static SurfaceKind SurfaceOf(int row, int col)
        {
            return SurfaceOf(new SimVec2(col + 0.5f, row + 0.5f));
        }

        // 主城石路(2026-09-11 制作人裁决):**墙内整片石路** —— 即以基地中心为圆心、
        // 半径=城墙半径(20)的整块圆盘。基地中心同时前移到地图左右极限位置。
        public const float BaseInsetX = 8f;            // 基地中心距地图左右端(极限位,留出基地本体)
        public const float PlazaRadius = 20f;          // 墙内石路半径 = 墙半径

        /// <summary>墙内整片石路:距基地中心 ≤ PlazaRadius。</summary>
        public static bool InBasePlaza(SimVec2 v, Side side)
        {
            float dx = v.X - BaseCenterX(side), dy = v.Y - CenterY;
            return dx * dx + dy * dy <= PlazaRadius * PlazaRadius;
        }

        /// <summary>该方基地中心 X(蓝 8 / 红 Cols-8:地图左右极限位)。</summary>
        public static float BaseCenterX(Side side) { return side == Side.Blue ? BaseInsetX : Cols - BaseInsetX; }

        /// <summary>墙内石路圆盘中心(供 View 画真圆网格;与 InBasePlaza 同口径)。</summary>
        public static SimVec2 PlazaCenter(Side side) { return new SimVec2(BaseCenterX(side), CenterY); }

        static bool Near(SimVec2 v, float x, float y, float r)
        {
            float dx = v.X - x, dy = v.Y - y;
            return dx * dx + dy * dy <= r * r;
        }
    }
}
