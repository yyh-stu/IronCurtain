using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>M6.3-C1 紧凑建筑操作面板；动作与反馈全部由 BuildingCommandModel 提供。</summary>
    public sealed class BuildingCommandPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = Rect.zero;

        private BuildingCommandModel _model;
        private IReadOnlyList<int> _selectedIds;
        private int _targetId;
        private string _feedback;
        private bool _feedbackOk;
        private float _feedbackUntil;

        public bool HasTarget => _targetId != 0;
        public int TargetId => _targetId;

        public void Init(SimCore sim, Side player, IReadOnlyList<int> selectedIds)
        {
            _model = new BuildingCommandModel(sim, player);
            _selectedIds = selectedIds;
        }

        public void SetTarget(int buildingId)
        {
            _targetId = buildingId;
            _feedback = null;
        }

        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        private bool MouseOverOtherUi(Vector2 m)
        {
            Vector2 g = new Vector2(m.x, Screen.height - m.y);
            return BarracksOverviewPanel.LastPanelRect.Contains(g) || TopBarPanel.LastPanelRect.Contains(g)
                || MinimapPanel.LastPanelRect.Contains(g) || LastPanelRect.Contains(g);
        }

        private int HoveredBuilding(Vector2 mouse)
        {
            if (MouseOverOtherUi(mouse)) return 0;
            var cam = Camera.main;
            if (cam == null) return 0;
            var ray = cam.ScreenPointToRay(mouse);
            if (!GroundPlane.Raycast(ray, out float enter)) return 0;
            var wp = ray.GetPoint(enter);
            return BuildingCommandModel.PickVisibleBuilding(_model.Sim, _model.Player, new SimVec2(wp.x, 3f - wp.z));
        }

        /// <summary>悬停敌方/中立据点:据点上方出现偏好选择(占领/摧毁);点击=改偏好(有选中兵团即攻)。</summary>
        private void DrawHoverSelector()
        {
            var ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return;
            int id = HoveredBuilding(ev.mousePosition);
            if (id == 0) return;
            var b = _model.Sim.GetEntity(id);
            if (b == null) return;
            var sp = WorldToScreenAbove(b, 2.6f);
            // 已设偏好小标签(第 5 条:未设不可见)
            if (b.Intent != SiegeIntent.None && b.IntentIssuer == _model.Player)
            {
                var label = "偏好:" + (b.Intent == SiegeIntent.Capture ? "占领" : "摧毁");
                GUI.Label(new Rect(sp.x - 60f, sp.y, 120f, 18f), label, SmallStyle());
            }
            var sel = new Rect(sp.x - 76f, sp.y - 30f, 152f, 26f);
            if (GUI.Button(new Rect(sel.x, sel.y, 74f, 26f), "占领"))
                ChoosePreference(id, BuildingCommandAction.Capture);
            if (GUI.Button(new Rect(sel.x + 78f, sel.y, 74f, 26f), "摧毁"))
                ChoosePreference(id, BuildingCommandAction.Destroy);
        }

        private void ChoosePreference(int id, BuildingCommandAction action)
        {
            var r = _model.SetPreference(id, action, _selectedIds);
            _feedbackOk = r.Success;
            _feedback = r.Message;
            _feedbackUntil = Time.unscaledTime + 2.5f;
#if UNITY_EDITOR
            InputAudit.Log("building_pref building=" + id + " action=" + action + " result=" + (r.Success ? "ok" : "deny")
                + " message=" + r.Message + " selected_ids=" + InputAudit.FormatIds(_selectedIds));
#endif
        }

        /// <summary>所有已设偏好的敌方/中立据点:据点上方常显偏好标签(制作人称:偏好看作据点属性)。</summary>
        private void DrawPreferenceMarkers()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var kv in _model.Sim.Entities)
            {
                var b = kv.Value;
                if (b == null || b.Kind != EntityKind.Building || b.Rubbled) continue;
                if (b.Side == _model.Player) continue;
                if (b.Intent == SiegeIntent.None || b.IntentIssuer != _model.Player) continue;
                var sp = WorldToScreenAbove(b, 2.2f);
                if (sp.z <= 0f) continue;
                var label = (b.Intent == SiegeIntent.Capture ? "占领" : "摧毁");
                GUI.Label(new Rect(sp.x - 40f, sp.y, 80f, 16f), label, SmallStyle());
            }
        }

        private Vector3 WorldToScreenAbove(Entity b, float y)
        {
            var cam = Camera.main;
            var wp = BoardView.WorldPos(b.Pos) + new Vector3(0f, y, 0f);
            var sp = cam.WorldToScreenPoint(wp);
            return new Vector3(sp.x, Screen.height - sp.y, sp.z);
        }

        private static GUIStyle _smallStyle;
        private static GUIStyle SmallStyle()
        {
            if (_smallStyle == null)
            {
                _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                _smallStyle.normal.textColor = new Color(1f, 0.9f, 0.5f);
            }
            return _smallStyle;
        }

        /// <summary>2026-09-09 制作人裁决:选中兵团点城=按现有偏好直接下令(兵随主将)。</summary>
        public bool TryOrderPreferred()
        {
            if (_targetId == 0 || _model == null || _selectedIds == null || _selectedIds.Count == 0) return false;
            var r = _model.OrderPreferred(_targetId, _selectedIds);
            _feedback = r.Message;
            _feedbackOk = r.Success;
            _feedbackUntil = UnityEngine.Time.unscaledTime + 2f;
#if UNITY_EDITOR
            InputAudit.Log("building_quick_order building=" + _targetId + " result=" + (r.Success ? "ok" : "deny")
                + " message=" + r.Message);
#endif
            return r.Success;
        }

        public void ClearTarget(string reason = null)
        {
            int previous = _targetId;
            _targetId = 0;
            _feedback = null;
            LastPanelRect = Rect.zero;
#if UNITY_EDITOR
            if (previous != 0) InputAudit.Log("building_target close building=" + previous + " reason=" + (reason ?? "unknown"));
#endif
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;

        private void OnGUI()
        {
            if (_model == null) { LastPanelRect = Rect.zero; return; }
            DrawPreferenceMarkers();   // 2026-09-10 制作人:已设偏好在据点上方常显小标签
            DrawHoverSelector();       // 悬停据点:偏好选择 UI(有选中兵团则选完即攻)
            if (_targetId == 0) { LastPanelRect = Rect.zero; return; }
            var info = _model.Describe(_targetId, _selectedIds);
            if (!info.Exists) { ClearTarget("invalid"); return; }

            const float width = 340f;
            const float height = 132f;
            LastPanelRect = new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height - 210f, width, height);
            GUI.Box(LastPanelRect, "");
            GUILayout.BeginArea(new Rect(LastPanelRect.x + 12f, LastPanelRect.y + 8f, width - 24f, height - 16f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(info.Title, TitleStyle());
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(28f))) ClearTarget("cancel");
            GUILayout.EndHorizontal();

            GUILayout.Label(info.Status);
            GUILayout.BeginHorizontal();
            if (info.CanCapture && GUILayout.Button("占领", GUILayout.Height(32f))) Execute(BuildingCommandAction.Capture);
            if (info.CanDestroy && GUILayout.Button("摧毁", GUILayout.Height(32f))) Execute(BuildingCommandAction.Destroy);
            if (info.CanRebuild && GUILayout.Button("重建 " + info.RebuildCost + " EP", GUILayout.Height(32f)))
                Execute(BuildingCommandAction.Rebuild);
            GUILayout.EndHorizontal();

            if (_feedback != null && Time.unscaledTime < _feedbackUntil)
            {
                var previous = GUI.color;
                GUI.color = _feedbackOk ? Color.green : new Color(1f, 0.35f, 0.3f);
                GUILayout.Label(_feedback);
                GUI.color = previous;
            }
            GUILayout.EndArea();
        }

        private void Execute(BuildingCommandAction action)
        {
#if UNITY_EDITOR
            InputAudit.Log("building_action building=" + _targetId + " action=" + action
                + " selected_ids=" + InputAudit.FormatIds(_selectedIds));
#endif
            var result = _model.Execute(_targetId, _selectedIds, action);
            _feedbackOk = result.Success;
            _feedback = result.Message;
            _feedbackUntil = Time.unscaledTime + 2.5f;
#if UNITY_EDITOR
            InputAudit.Log("building_result building=" + _targetId + " action=" + action
                + " result=" + (result.Success ? "ok" : "error") + " code=" + result.Error
                + " soldiers=" + result.SoldierCount + " message=\"" + result.Message + "\"");
#endif
        }

        private static GUIStyle _title;
        private static GUIStyle TitleStyle()
        {
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                _title.normal.textColor = Color.white;
            }
            return _title;
        }
    }
}
