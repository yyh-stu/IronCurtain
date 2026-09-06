namespace IronCurtain.Sim
{
    /// <summary>
    /// M8.5d 卡死自动绕行:旁路点计算(无 A*/流场,纯几何旁路;禁 UnityEngine)。
    /// </summary>
    public static class NavigationAid
    {
        public const int StuckThresholdTicks = 15;  // 1.5s @10Hz
        public const float StepRatio = 0.25f;       // 本 tick 位移 < 期望步长×0.25 视为停滞
        public const float DetourClearDist = 0.8f;  // 距旁路点至此 → 旁路结束
        public const int DetourTimeoutTicks = 45;   // 旁路 4.5s 保险超时
        public const float ProbeDist = 1.2f;        // 前向探测距离
        public const float Margin = 1.5f;           // 建筑外扩余量

        /// <summary>计算旁路点:前方建筑挡路 → 绕其外扩矩形的较近角;单位互堵(无建筑)→ 垂直向偏移 2m(按实体 id 奇偶定侧,确定性)。无有效方向 → null。</summary>
        public static SimVec2? ComputeDetour(SimCore sim, Entity u, SimVec2 goal)
        {
            var dir = (goal - u.PosF).Normalized();
            if (dir.SqrMagnitude < 1e-8f) return null; // 目标即当前位置,无方向
            var probe = u.PosF + dir * ProbeDist;
            Entity blk = null;
            float best = float.MaxValue;
            foreach (var b in sim.Entities.Values)
            {
                if (b.Kind != EntityKind.Building || b.Rubbled) continue;
                bool hit = BuildingFootprint.Contains(b, probe)
                    || SimVec2.Distance(u.PosF, BuildingFootprint.ClosestPoint(b, u.PosF)) < 1.2f;
                if (!hit) continue;
                // 只算挡在前方的
                var toB = SimVec2.FromGrid(b.Pos) - u.PosF;
                if (toB.X * dir.X + toB.Y * dir.Y <= 0f) continue;
                float d = SimVec2.Distance(u.PosF, SimVec2.FromGrid(b.Pos));
                if (d < best) { best = d; blk = b; }
            }
            if (blk == null)
                return TerrainMap.ClampToEllipse(u.PosF + new SimVec2(-dir.Y, dir.X) * ((u.Id & 1) == 0 ? 2f : -2f)); // 单位互堵:垂直让位
            // 外扩矩形较近角(4 角中取 距u+距goal 最小者)
            var c = SimVec2.FromGrid(blk.Pos);
            float ex = BuildingFootprint.HalfW(blk) + Margin, ey = BuildingFootprint.HalfH(blk) + Margin;
            SimVec2 bestC = u.PosF; best = float.MaxValue;
            foreach (var sgn in new[] { new SimVec2(1f, 1f), new SimVec2(1f, -1f), new SimVec2(-1f, 1f), new SimVec2(-1f, -1f) })
            {
                var corner = new SimVec2(c.X + sgn.X * ex, c.Y + sgn.Y * ey);
                float score = SimVec2.Distance(u.PosF, corner) + SimVec2.Distance(corner, goal);
                if (score < best) { best = score; bestC = corner; }
            }
            return TerrainMap.ClampToEllipse(bestC);
        }
    }
}
