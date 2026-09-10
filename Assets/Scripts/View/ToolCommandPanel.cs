using System;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public enum ToolTargetMode : byte { None, Ram, Fort }

    /// <summary>M6.3 道具入口；只负责目标模式和反馈，结算由 ToolCommandModel 完成。</summary>
    public sealed class ToolCommandPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = Rect.zero;

        private SimCore _sim;
        private ToolCommandModel _model;
        private Side _player;
        private ToolTargetMode _pending;
        private string _feedback;
        private bool _feedbackOk;
        private float _feedbackUntil;
        private Action _openedCallback;

        public ToolTargetMode PendingTarget => _pending;
        public bool HasPendingTarget => _pending != ToolTargetMode.None;
        public string LastFeedback => _feedback;

        public void Init(SimCore sim, Side player)
        {
            _sim = sim;
            _model = new ToolCommandModel(sim, player);
            _player = player;
        }

        public bool Arm(ToolTargetMode mode)
        {
            if (mode != ToolTargetMode.Ram && mode != ToolTargetMode.Fort) return false;
            var state = _model?.Describe() ?? default;
            if (!state.Valid)
            {
                Report("arm", mode, new ToolCommandFeedback(false,
                    ToolCommandError.InvalidState, "道具状态无效"));
                return false;
            }
            bool affordable = mode == ToolTargetMode.Ram ? state.CanAffordRam : state.CanAffordFort;
            if (!affordable)
            {
                Report("arm", mode, new ToolCommandFeedback(false,
                    ToolCommandError.InsufficientEp, "EP 不足"));
                return false;
            }
            _pending = mode;
            Show(mode == ToolTargetMode.Ram ? "请点击可见敌方建筑" : "请点击地图内空格", true);
#if UNITY_EDITOR
            InputAudit.Log("tool_target arm=" + mode);
#endif
            return true;
        }

        public bool TryHandleWorldClick(bool hasGroundPoint, SimVec2 point)
        {
            if (_pending == ToolTargetMode.None) return false;
            var mode = _pending;
            ToolCommandFeedback result;
            if (!hasGroundPoint)
                result = new ToolCommandFeedback(false, ToolCommandError.InvalidTarget, "未命中地图");
            else if (mode == ToolTargetMode.Ram)
            {
                int target = BuildingCommandModel.PickVisibleBuilding(_sim, _player, point);
                result = _model.UseRam(target);
            }
            else if (!TerrainMap.IsInside(point) || !TerrainMap.IsInside(point.ToGrid()))
                result = new ToolCommandFeedback(false, ToolCommandError.InvalidTarget,
                    "临时阵地需要地图内合法空格");
            else
                result = _model.UseFort(point.ToGrid());
            Report("use", mode, result);
            if (result.Success) _pending = ToolTargetMode.None;
            return true;
        }

        public bool CancelTargeting(string reason)
        {
            if (_pending == ToolTargetMode.None) return false;
            var previous = _pending;
            _pending = ToolTargetMode.None;
            Show("已取消" + NameOf(previous), true);
#if UNITY_EDITOR
            InputAudit.Log("tool_target cancel=" + previous + " reason=" + (reason ?? "unknown"));
#endif
            return true;
        }

        public static Rect PanelRectFor(int screenWidth, int screenHeight, float reservedTop, bool expanded)
        {
            float width = Mathf.Max(0f, Mathf.Min(340f, screenWidth - 20f));
            float screenBottom = Mathf.Max(0f, screenHeight - 10f);
            float top = Mathf.Min(screenBottom, Mathf.Max(50f, reservedTop));
            float height = Mathf.Min(expanded ? 174f : 42f, Mathf.Max(0f, screenBottom - top));
            return new Rect(10f, top, width, height);
        }

        /// <summary>无效配置不得把零成本按钮呈现成可执行操作。</summary>
        public static bool CanShowActions(ToolCommandState state) => state.Valid;

        private void OnDisable() { LastPanelRect = Rect.zero; _pending = ToolTargetMode.None; }
        private void OnDestroy() { LastPanelRect = Rect.zero; _pending = ToolTargetMode.None; }

        private bool _expanded;
        public bool Expanded => _expanded;
        public void SetOpenedCallback(Action callback) => _openedCallback = callback;
        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            if (expanded) _openedCallback?.Invoke();
        }

        private void OnGUI()
        {
            if (_model == null) { LastPanelRect = Rect.zero; return; }
            GUI.skin.font = ViewAssets.UiFont(14);
            LastPanelRect = PanelRectFor(Screen.width, Screen.height,
                BarracksOverviewPanel.LastPanelRect.yMax + 8f, _expanded);
            GUI.Box(LastPanelRect, "", UiSkin.Panel); // 2026-09-10 木框白底皮肤
            GUILayout.BeginArea(new Rect(LastPanelRect.x + 8f, LastPanelRect.y + 6f,
                Mathf.Max(0f, LastPanelRect.width - 16f), Mathf.Max(0f, LastPanelRect.height - 12f)));
            DrawHeader();
            if (_expanded) DrawContents();
            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_expanded ? "道具 ▲" : "道具 ▼", GUILayout.Width(92f)))
            {
                SetExpanded(!_expanded);
#if UNITY_EDITOR
                InputAudit.Log("tool_panel expanded=" + _expanded);
#endif
            }
            GUILayout.FlexibleSpace();
            var state = _model.Describe();
            GUILayout.Label(state.Valid ? "EP " + state.Ep.ToString("0.#") : "状态不可用");
            GUILayout.EndHorizontal();
        }

        private void DrawContents()
        {
            var state = _model.Describe();
            if (!CanShowActions(state))
            {
                DrawFeedback("道具状态不可用", false);
                return;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("攻城锥 " + state.RamCost + "EP", GUILayout.Height(30f)))
                Arm(ToolTargetMode.Ram);
            if (GUILayout.Button("临时阵地 " + state.FortCost + "EP", GUILayout.Height(30f)))
                Arm(ToolTargetMode.Fort);
            GUILayout.EndHorizontal();
            if (_pending != ToolTargetMode.None)
            {
                GUILayout.Label(_pending == ToolTargetMode.Ram ? "目标：可见敌方建筑" : "目标：地图内空格");
                if (GUILayout.Button("取消当前目标", GUILayout.Height(26f))) CancelTargeting("button");
            }
            if (_feedback != null && Time.unscaledTime < _feedbackUntil)
                DrawFeedback(_feedback, _feedbackOk);
        }

        private static void DrawFeedback(string message, bool success)
        {
            var previous = GUI.color;
            GUI.color = success ? Color.green : new Color(1f, 0.35f, 0.3f);
            GUILayout.Label(message);
            GUI.color = previous;
        }

        private void Report(string action, ToolTargetMode mode, ToolCommandFeedback result)
        {
            Show(result.Message, result.Success);
#if UNITY_EDITOR
            InputAudit.Log("tool_action action=" + action + " mode=" + mode);
            InputAudit.Log("tool_result action=" + action + " mode=" + mode + " result="
                + (result.Success ? "ok" : "error") + " code=" + result.Error
                + " message=\"" + result.Message + "\"");
#endif
        }

        private void Show(string message, bool success)
        {
            _feedback = message;
            _feedbackOk = success;
            _feedbackUntil = Time.unscaledTime + 2.5f;
        }

        private static string NameOf(ToolTargetMode mode) =>
            mode == ToolTargetMode.Ram ? "攻城锥" : "临时阵地";
    }
}
