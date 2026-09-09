using System;
using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>M6.3 海克斯抽选存用入口；所有状态和结果由 HexCommandModel 提供。</summary>
    public sealed class HexCommandPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = Rect.zero;

        private HexCommandModel _model;
        private IReadOnlyList<int> _selectedIds;
        private bool _expanded;
        private Vector2 _scroll;
        private string _feedback;
        private bool _feedbackOk;
        private float _feedbackUntil;
        private Action _openedCallback;

        public bool Expanded => _expanded;

        public void Init(SimCore sim, Side player, IReadOnlyList<int> selectedIds)
        {
            _model = new HexCommandModel(sim, player);
            _selectedIds = selectedIds;
        }

        public void SetOpenedCallback(Action callback) => _openedCallback = callback;
        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            if (expanded) _openedCallback?.Invoke();
        }

        public static Rect PanelRectFor(int screenWidth, int screenHeight,
            float reservedTop, float reservedBottom, bool expanded)
        {
            float width = Mathf.Max(0f, Mathf.Min(320f, screenWidth - 20f));
            float screenBottom = Mathf.Max(0f, screenHeight - 10f);
            float top = Mathf.Min(screenBottom, Mathf.Max(50f, reservedTop));
            float bottom = reservedBottom > top
                ? Mathf.Min(screenBottom, reservedBottom) : screenBottom;
            float available = Mathf.Max(0f, bottom - top);
            float height = Mathf.Min(expanded ? 310f : 42f, available);
            return new Rect(Mathf.Max(0f, screenWidth - width - 10f), top, width, height);
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;

        private void OnGUI()
        {
            if (_model == null) { LastPanelRect = Rect.zero; return; }
            GUI.skin.font = ViewAssets.UiFont(14);
            float top = Mathf.Max(100f, UpgradeCommandPanel.LastPanelRect.yMax + 8f);
            float bottom = MinimapPanel.LastPanelRect.height > 0f
                ? MinimapPanel.LastPanelRect.yMin - 8f : Screen.height - 10f;
            LastPanelRect = PanelRectFor(Screen.width, Screen.height, top, bottom, _expanded);
            GUI.Box(LastPanelRect, "");
            GUILayout.BeginArea(new Rect(LastPanelRect.x + 8f, LastPanelRect.y + 6f,
                Mathf.Max(0f, LastPanelRect.width - 16f), Mathf.Max(0f, LastPanelRect.height - 12f)));
            DrawHeader();
            if (_expanded) DrawContents();
            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_expanded ? "海克斯 ▲" : "海克斯 ▼", GUILayout.Width(100f)))
            {
                SetExpanded(!_expanded);
#if UNITY_EDITOR
                InputAudit.Log("hex_panel expanded=" + _expanded);
#endif
            }
            GUILayout.FlexibleSpace();
            var state = _model.Describe();
            GUILayout.Label(state.Valid
                ? "EP " + state.Ep.ToString("0.#") + " / 抽取 " + state.DrawCost
                : "状态不可用");
            GUILayout.EndHorizontal();
        }

        private void DrawContents()
        {
            var state = _model.Describe();
            if (!state.Valid)
            {
                DrawFeedback("海克斯状态不可用", false);
                return;
            }
            string active = state.Active.HasValue ? _model.NameOf(state.Active.Value) : "无";
            if (state.Active.HasValue && state.ActiveSeconds > 0f)
                active += " " + state.ActiveSeconds.ToString("0") + "s";
            GUILayout.Label("当前效果：" + active);
            if (GUILayout.Button("抽取三张（" + state.DrawCost + " EP）", GUILayout.Height(28f)))
                Report("draw", null, _model.Draw());

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (state.PendingChoices.Count > 0)
            {
                GUILayout.Label("本次三选一");
                foreach (var card in state.PendingChoices)
                    if (GUILayout.Button("选择「" + _model.NameOf(card) + "」", GUILayout.Height(26f)))
                        Report("choose", card, _model.Choose(card));
            }
            GUILayout.Label("手牌" + (state.Hand.Count == 0 ? "：空" : ""));
            foreach (var card in state.Hand)
            {
                string suffix = card == HexId.Sandbag ? "（需单选己方单位）" : "";
                if (GUILayout.Button("释放「" + _model.NameOf(card) + "」" + suffix,
                    GUILayout.Height(26f)))
                    Report("use", card, _model.Use(card, _selectedIds));
            }
            GUILayout.EndScrollView();

            if (_feedback != null && Time.unscaledTime < _feedbackUntil)
                DrawFeedback(_feedback, _feedbackOk);
        }

        private void Report(string action, HexId? card, HexCommandFeedback result)
        {
            _feedbackOk = result.Success;
            _feedback = result.Message;
            _feedbackUntil = Time.unscaledTime + 2.5f;
#if UNITY_EDITOR
            InputAudit.Log("hex_action action=" + action
                + (card.HasValue ? " card=" + card.Value : "")
                + " selected_ids=" + InputAudit.FormatIds(_selectedIds));
            InputAudit.Log("hex_result action=" + action + " result="
                + (result.Success ? "ok" : "error") + " code=" + result.Code
                + " message=\"" + result.Message + "\"");
#endif
        }

        private static void DrawFeedback(string message, bool success)
        {
            var previous = GUI.color;
            GUI.color = success ? Color.green : new Color(1f, 0.35f, 0.3f);
            GUILayout.Label(message);
            GUI.color = previous;
        }
    }
}
