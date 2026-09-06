using System;
using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>建筑占地矩形(米制半宽半高,中心=建筑格中心 SimVec2.FromGrid(b.Pos))。M8.1a:占地类,纯 C#。</summary>
    public static class BuildingFootprint
    {
        public static float HalfW(Entity b)
        {
            if (b.IsFort) return 1.5f;
            switch (b.Building)
            {
                case BuildingId.Base: return 2.5f;
                case BuildingId.Tower: return 2.0f;
                case BuildingId.Barracks: return 2.5f;
                case BuildingId.ResourcePoint: return 2.0f;
                default: return 1.5f;
            }
        }

        public static float HalfH(Entity b)
        {
            if (b.IsFort) return 1.5f;
            switch (b.Building)
            {
                case BuildingId.Base: return 2.5f;
                case BuildingId.Tower: return 2.0f;
                case BuildingId.Barracks: return 2.0f;
                case BuildingId.ResourcePoint: return 2.0f;
                default: return 1.5f;
            }
        }

        /// <summary>点是否在中心矩形内(含边界)。</summary>
        public static bool Contains(Entity b, SimVec2 p)
        {
            var c = SimVec2.FromGrid(b.Pos);
            float hw = HalfW(b), hh = HalfH(b);
            return p.X >= c.X - hw && p.X <= c.X + hw && p.Y >= c.Y - hh && p.Y <= c.Y + hh;
        }

        /// <summary>点在矩形外→矩形上最近点;点在矩形内→沿最小穿透轴推出后的缘点(供 PushOutOfBuildings 一次完成推出)。</summary>
        public static SimVec2 ClosestPoint(Entity b, SimVec2 p)
        {
            var c = SimVec2.FromGrid(b.Pos);
            float hw = HalfW(b), hh = HalfH(b);
            float minX = c.X - hw, maxX = c.X + hw, minY = c.Y - hh, maxY = c.Y + hh;
            if (p.X < minX || p.X > maxX || p.Y < minY || p.Y > maxY)
                return new SimVec2(Math.Min(Math.Max(p.X, minX), maxX),
                                   Math.Min(Math.Max(p.Y, minY), maxY));
            // 在矩形内:沿最小穿透轴推出
            float left = p.X - minX, right = maxX - p.X, down = p.Y - minY, up = maxY - p.Y;
            float m = Math.Min(Math.Min(left, right), Math.Min(down, up));
            if (m == left) return new SimVec2(minX, p.Y);
            if (m == right) return new SimVec2(maxX, p.Y);
            if (m == down) return new SimVec2(p.X, minY);
            return new SimVec2(p.X, maxY);
        }

        /// <summary>枚举矩形覆盖的所有格(格中心落在 [min,max) 半开区间内,保证基地25/箭塔16/兵营20/资源点16/堡垒9)。</summary>
        public static List<GridPos> CellsOccupied(Entity b)
        {
            var list = new List<GridPos>();
            var c = SimVec2.FromGrid(b.Pos);
            float hw = HalfW(b), hh = HalfH(b);
            int rc = (int)Math.Ceiling(hh), cc = (int)Math.Ceiling(hw);
            for (int dr = -rc; dr <= rc; dr++)
                for (int dc = -cc; dc <= cc; dc++)
                {
                    int row = b.Pos.Row + dr, col = b.Pos.Col + dc;
                    if (row < 0 || row >= LaneGrid.Rows || col < 0 || col >= LaneGrid.Cols) continue;
                    var ctr = SimVec2.FromGrid(new GridPos(row, col));
                    if (ctr.X >= c.X - hw && ctr.X < c.X + hw && ctr.Y >= c.Y - hh && ctr.Y < c.Y + hh)
                        list.Add(new GridPos(row, col));
                }
            return list;
        }
    }
}
