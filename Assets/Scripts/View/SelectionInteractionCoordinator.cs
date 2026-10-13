using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// SelectionSystem 与建筑命令面板之间的轻量协调层：集中目标切换、UI 命中和输入审计，
    /// 让选择渲染/移动逻辑不依赖建筑操作细节。
    /// </summary>
    public sealed class SelectionInteractionCoordinator
    {
        private readonly SimCore _sim;
        private readonly Side _player;
        private readonly List<int> _selected;
        private BuildingCommandPanel _buildingPanel;
        private ToolCommandPanel _toolPanel;

        public SelectionInteractionCoordinator(SimCore sim, Side player, List<int> selected)
        {
            _sim = sim;
            _player = player;
            _selected = selected;
        }

        public void AttachBuildingPanel(BuildingCommandPanel panel) => _buildingPanel = panel;
        public void AttachToolPanel(ToolCommandPanel panel) => _toolPanel = panel;

        public void HandleWorldClick(int generalId, bool hasGroundPoint, SimVec2 point)
        {
            if (_toolPanel != null && _toolPanel.TryHandleWorldClick(hasGroundPoint, point)) return;
            // 2026-09-11 制作人裁决:框选军队点敌方城墙=拆墙(不走摧毁/占领意图)。
            // 墙非实体,在此直接识别:选中主将 + 点击处是敌方完好墙段 → 行军至墙,推进逻辑自动拆。
            if (hasGroundPoint && _selected.Count > 0 && WallSystem.HitEnemyWall(_sim, _player, point))
            {
                var wallPt = WallSystem.NearestWallPoint(_player, point);
                foreach (int id in _selected)
                {
                    var g = _sim.GetEntity(id);
                    if (g == null || g.IsSoldier || g.Kind != EntityKind.Unit || g.Side != _player) continue;
                    // 2026-09-14 制作人:"坠日居然要走到屏障脸上才能攻击,他应该可以远程的" ——
                    // 远程单位下墙令时落点取**射程外沿**(不贴脸);近战照旧走墙脚(推进遇墙就拆)。
                    var dest = wallPt;
                    if (DamageCalculator.IsRanged(g) && g.Range > 1f)
                    {
                        var toCenter = WallSystem.BaseCenter(_player == Side.Blue ? Side.Red : Side.Blue) - wallPt;
                        var len = toCenter.Magnitude;
                        if (len > 0.01f)
                        {
                            var outward = new SimVec2(-toCenter.X / len, -toCenter.Y / len);   // 墙点向外
                            float standoff = UnityEngine.Mathf.Max(0.6f, g.Range - 1.0f);
                            dest = TerrainMap.ClampToEllipse(wallPt + outward * standoff);
                        }
                    }
                    _sim.SetMoveTarget(id, new GridPos((int)dest.Y, (int)dest.X), true);
                }
#if UNITY_EDITOR
                InputAudit.Log("wall_breach_order at=" + wallPt);
#endif
                return;
            }
            // 2026-09-14 制作人:点敌方建筑要能开出信息面板(含驻军情报切片/占领摧毁),
            // 所以这里的点选用**不过滤迷雾**的版本;情报新旧由面板标注。
            int buildingId = _buildingPanel != null && hasGroundPoint
                ? BuildingCommandModel.PickBuildingAny(_sim, point)
                : 0;
            // 2026-09-13 制作人:"框选后左键点击自家建筑展开建筑信息面板"。
            // 原逻辑"点中附近主将"优先于"点中建筑"→ 自家建筑旁边站着人时永远开不出面板。有选中兵团时建筑优先。
            var decision = buildingId != 0 && _selected.Count > 0
                ? BuildingWorldClickDecision.TargetBuilding
                : BuildingCommandModel.DecideWorldClick(
                    _buildingPanel != null && _buildingPanel.HasTarget, generalId, buildingId);
            switch (decision)
            {
                case BuildingWorldClickDecision.SelectGeneral:
                    _buildingPanel?.ClearTarget("general");
                    _selected.Clear();
                    _selected.Add(generalId);
                    break;
                case BuildingWorldClickDecision.TargetBuilding:
                    _buildingPanel.SetTarget(buildingId); // 建筑目标不破坏当前单选/框选。
                    LogBuildingTarget(buildingId);
                    // 2026-09-14 制作人:"框选后**右键**敌方建筑直接进攻(**前提是有设置偏好**);**左键**点开信息面板"
                    // → 左键这里**只开面板**(原来会顺带按偏好自动进攻,已移除)。
                    // 进攻改由 SelectionSystem 的右键分支调 TryOrderPreferredOn。
                    break;
                case BuildingWorldClickDecision.CloseTargetOnly:
                    _buildingPanel?.ClearTarget("empty_ground");
                    break;
                default:
                    _selected.Clear();
                    break;
            }
        }

        public void CloseTargetForBoxSelection()
        {
            _toolPanel?.CancelTargeting("box_select");
            _buildingPanel?.ClearTarget("box_select");
        }

        /// <summary>右键点非己方建筑:按该建筑已设偏好直接下兵团令(制作人 2026-09-14 口径)。</summary>
        public bool RightClickBuilding(int buildingId) =>
            _buildingPanel != null && _buildingPanel.TryOrderPreferredOn(buildingId);

        public bool CancelToolTargeting(string reason) =>
            _toolPanel != null && _toolPanel.CancelTargeting(reason);

        public bool ShouldApplyFormation(bool pointerOverPanel)
        {
            if (_toolPanel != null && _toolPanel.HasPendingTarget)
            {
                LogFormation("ignored", "tool_targeting", _selected);
                return false;
            }
            if (pointerOverPanel)
            {
                LogFormation("ignored", "pointer_over_panel", _selected);
                return false;
            }
            if (_selected.Count < 2)
            {
                LogFormation("ignored", "requires_2", _selected);
                return false;
            }
            return true;
        }

        public void LogFormationApplied() => LogFormation("applied", null, _selected);

        public static bool IsPointerOverUi(int selectedCount, Rect retreatButtonRect)
        {
            Vector2 mouse = Input.mousePosition;
            // 2026-09-13 系统性 bug 修复:"点面板按钮没反应"。
            // 各面板的 LastPanelRect 是 UITK **面板单位**(worldBound),而这里原来拿**屏幕像素**去比;
            // 缩放模式(ScaleWithScreenSize)下两者不等(2560×1440 时系数 1.333)→ 命中区错位 →
            // 点在面板上的左键**同时穿透到战场**,把框选清掉 → 面板动作(如[驻军])拿不到选中军队 → 点了没反应。
            // 现在分开两套口径:UITK 面板用面板单位,IMGUI 撤退按钮仍用屏幕像素。
            float kUnitPerPx = 1f;
            var ui = UiRoot.Current;
            if (ui != null && ui.Screen != null)
            {
                float pw = ui.Screen.resolvedStyle.width;
                if (!float.IsNaN(pw) && pw > 1f && Screen.width > 0) kUnitPerPx = pw / Screen.width;
            }
            var guiPoint = new Vector2(mouse.x, Screen.height - mouse.y);                 // 像素(IMGUI)
            var uiPoint = new Vector2(guiPoint.x * kUnitPerPx, guiPoint.y * kUnitPerPx);  // 面板单位(UITK)
            return BlockingRectName(uiPoint, selectedCount >= 1 && retreatButtonRect.Contains(guiPoint)) != null;
        }

        /// <summary>
        /// 2026-09-15 制作人"点敌方建筑无法展开面板 / 框选自己军队失灵"的**归因口**:
        /// 返回**哪一个矩形**吞掉了这次输入(null=没吞)。审计用;不改变判定语义。
        /// 面板矩形本身已由 `UiKit.VisibleRectOrZero` 保证"隐藏/已关闭=零矩形"(不再留幽灵拦截区),
        /// 所以这里返回非 null 就一定是真的压在可见 UI 上。
        /// </summary>
        public static string BlockingRectName(Vector2 uiPoint, bool overRetreatButton)
        {
            if (BarracksOverviewPanel.LastPanelRect.Contains(uiPoint)) return "总览面板";
            if (TopBarPanel.LastPanelRect.Contains(uiPoint)) return "顶栏";
            if (MinimapPanel.LastPanelRect.Contains(uiPoint)) return "小地图";
            if (BuildingCommandPanel.LastPanelRect.Contains(uiPoint)) return "建筑面板";
            if (UpgradeCommandPanel.LastPanelRect.Contains(uiPoint)) return "英雄升级面板";
            if (HexCommandPanel.LastPanelRect.Contains(uiPoint)) return "海克斯面板";
            if (ToolCommandPanel.LastPanelRect.Contains(uiPoint)) return "道具面板";
            if (PauseOverlayPanel.LastPanelRect.Contains(uiPoint)) return "暂停遮罩";
            if (overRetreatButton) return "撤退按钮";
            return null;
        }

        /// <summary>审计用:把 9 个拦截矩形的当前值原样导出(定位幽灵矩形)。</summary>
        public static string DumpBlockingRects(Vector2 uiPoint)
        {
            var sb = new System.Text.StringBuilder();
            void Add(string n, Rect r) => sb.Append("\n    ").Append(n.PadRight(12))
                .Append(r.x.ToString("F0")).Append(",").Append(r.y.ToString("F0"))
                .Append(" ").Append(r.width.ToString("F0")).Append("x").Append(r.height.ToString("F0"))
                .Append(r.Contains(uiPoint) ? "  ★压住探测点" : "");
            Add("总览", BarracksOverviewPanel.LastPanelRect);
            Add("顶栏", TopBarPanel.LastPanelRect);
            Add("小地图", MinimapPanel.LastPanelRect);
            Add("建筑", BuildingCommandPanel.LastPanelRect);
            Add("英雄升级", UpgradeCommandPanel.LastPanelRect);
            Add("海克斯", HexCommandPanel.LastPanelRect);
            Add("道具", ToolCommandPanel.LastPanelRect);
            Add("暂停遮罩", PauseOverlayPanel.LastPanelRect);
            return sb.ToString();
        }

        public static void LogBlockedLeftRelease()
        {
#if UNITY_EDITOR
            InputAudit.Log("world_input blocked=left_release reason=panel");
#endif
        }

        public static void LogSelection(bool click, IReadOnlyList<int> selected)
        {
#if UNITY_EDITOR
            InputAudit.Log("selection mode=" + (click ? "click" : "box")
                + " selected_ids=" + InputAudit.FormatIds(selected));
#endif
        }

        public static void LogMoveCommand(GridPos target, IReadOnlyList<int> selected)
        {
#if UNITY_EDITOR
            InputAudit.Log("move_command target=" + target
                + " selected_ids=" + InputAudit.FormatIds(selected));
#endif
        }

        private static void LogFormation(string result, string reason, IReadOnlyList<int> selected)
        {
#if UNITY_EDITOR
            InputAudit.Log("formation result=" + result
                + (reason == null ? "" : " reason=" + reason)
                + " selected_ids=" + InputAudit.FormatIds(selected));
#endif
        }

        private void LogBuildingTarget(int buildingId)
        {
#if UNITY_EDITOR
            var building = _sim.GetEntity(buildingId);
            InputAudit.Log("building_target building=" + buildingId + " side=" + building.Side
                + " type=" + (building.Building.HasValue ? building.Building.Value.ToString() : "Fort")
                + " selected_ids=" + InputAudit.FormatIds(_selected));
#endif
        }
    }
}
