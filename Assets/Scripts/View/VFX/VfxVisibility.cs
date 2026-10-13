using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// M1.3 统一可见性闸(纯函数)。所有附着效果、弹道、伤害数字必须经此;依据 FogRules,
    /// 禁止用 Renderer.enabled / 镜头内外代替规则(README §5.6)。
    /// 弹道裁剪:中段越雾按可见区域采样(每 ~3 米一点),不是只查起终点(计划明文要求);
    /// 返回可见段 [0..tVisible](0=完全不可见,1=全程可见),调用方按比例缩短轨迹。
    /// </summary>
    public static class VfxVisibility
    {
        /// <summary>附着类效果(状态图标/受击):目标单位当前对玩家可见才显示。</summary>
        public static bool ShowAttached(SimCore sim, Side player, Entity target) =>
            FogRules.UnitVisible(sim, player, target);

        /// <summary>建筑侧效果:按情报状态——Fresh 才显示实时受损/占领类;Stale/Unknown 只保留静态外观。</summary>
        public static bool ShowBuildingLiveState(SimCore sim, Side player, Entity building) =>
            building != null && building.Side == player
                || FogRules.BuildingState(sim, player, building) == BuildingFogState.Fresh;

        /// <summary>弹道可见比例:从发射点到命中点采样,首个不可见采样点之前的比例。
        /// 已知限制(记录在案):采样步长 3 米;恰好跨过窄雾带(<3m)可能漏裁。</summary>
        public static float BeamVisibleFraction(SimCore sim, Side player, SimVec2 from, SimVec2 to)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) return FogVisibleAt(sim, player, from) ? 1f : 0f;
            const float step = 3f;
            int n = Mathf.CeilToInt(len / step);
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                var p = new SimVec2(from.X + dx * t, from.Y + dy * t);
                if (!FogVisibleAt(sim, player, p)) return Mathf.Max(0f, (i - 0.5f) / n);   // 回退半段,别让端点贴雾
            }
            return 1f;
        }

        /// <summary>点的迷雾可见性(FogAlpha:0=全黑不可见)。建筑格与单位格口径由 FogRules 决定。</summary>
        public static bool FogVisibleAt(SimCore sim, Side player, SimVec2 p)
        {
            int row = Mathf.Clamp((int)p.Y, 0, LaneGrid.Rows - 1);
            int col = Mathf.Clamp((int)p.X, 0, LaneGrid.Cols - 1);
            return FogRules.FogAlpha(sim, player, row, col) == 0;
        }
    }
}
