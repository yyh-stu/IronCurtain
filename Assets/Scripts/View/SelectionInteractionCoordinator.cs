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
                    _sim.SetMoveTarget(id, new GridPos((int)wallPt.Y, (int)wallPt.X), true);
                }
#if UNITY_EDITOR
                InputAudit.Log("wall_breach_order at=" + wallPt);
#endif
                return;
            }
            int buildingId = _buildingPanel != null && hasGroundPoint
                ? BuildingCommandModel.PickVisibleBuilding(_sim, _player, point)
                : 0;
            var decision = BuildingCommandModel.DecideWorldClick(
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
                    // 2026-09-09 制作人裁决:有选中兵团时点城=按现有偏好直接进攻(兵随主将)
                    if (_selected.Count >= 1) _buildingPanel.TryOrderPreferred();
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
            var guiPoint = new Vector2(mouse.x, Screen.height - mouse.y);
            return BarracksOverviewPanel.LastPanelRect.Contains(guiPoint)
                || TopBarPanel.LastPanelRect.Contains(guiPoint)
                || MinimapPanel.LastPanelRect.Contains(guiPoint)
                || BuildingCommandPanel.LastPanelRect.Contains(guiPoint)
                || UpgradeCommandPanel.LastPanelRect.Contains(guiPoint)
                || HexCommandPanel.LastPanelRect.Contains(guiPoint)
                || ToolCommandPanel.LastPanelRect.Contains(guiPoint)
                || (selectedCount >= 1 && retreatButtonRect.Contains(guiPoint));
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
