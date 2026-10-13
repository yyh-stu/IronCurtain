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
        // 2026-09-14 制作人:两个单位互卡 2 秒 → 放行**同阵营**穿透(标准做法,比调碰撞半径/加寻路代价便宜)。
        // 口径:**先绕后穿** —— 1.5s 先给旁路点(StuckThresholdTicks,已有逻辑),再卡到 2s 才开穿透窗口;
        // 窗口 0.8s,过期自动恢复(不会长期穿模)。只放**同阵营**:敌我之间仍互卡(卡位/挡路是战术要素,
        // 也不能让近战滑进敌方后排)。墙体/建筑占地**不在此列**(走 WallSystem/BuildingFootprint,照旧挡)。
        public const int GhostAfterTicks = 20;      // 被同阵营连续推挤 2s(10Hz)→ 开穿透窗口
        public const int GhostWindowTicks = 8;      // 穿透窗口 0.8s
        public const float ProbeDist = 1.2f;        // 前向探测距离
        public const float Margin = 1.5f;           // 建筑外扩余量

        /// <summary>计算**建筑**旁路点:前方建筑挡路 → 绕其外扩矩形的较近角。单位挡路不走这里(见 SimCore.SteerAroundAllies)。</summary>
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
            // 2026-09-14:"单位互堵 → 垂直偏移 2 米"的旁路点**已删除**,由 `SimCore.SteerAroundAllies`
            // 的连续绕行取代(旧做法只是一次性挪 2 米,到了就清、再顶上去,来回震荡;且它和"互卡穿透窗口"
            // 是两套半成品机制叠在一起,制作人明确要求换新办法时不要留旧残留)。
            if (blk == null) return null;   // 不是建筑挡路 → 交给 SteerAroundAllies 处理
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
