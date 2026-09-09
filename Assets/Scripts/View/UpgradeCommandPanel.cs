using System;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>M6.3 英雄升级入口；状态与购买结果全部来自 UpgradeCommandModel。</summary>
    public sealed class UpgradeCommandPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = Rect.zero;

        private UpgradeCommandModel _model;
        private bool _expanded;
        private string _feedback;
        private bool _feedbackOk;
        private float _feedbackUntil;
        private Action _openedCallback;

        public bool Expanded => _expanded;

        public void Init(SimCore sim, Side player)
        {
            _model = new UpgradeCommandModel(sim, player);
        }

        public void SetOpenedCallback(Action callback) => _openedCallback = callback;

        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            if (expanded) _openedCallback?.Invoke();
        }

        public static Rect PanelRectFor(int screenWidth, int screenHeight, bool expanded)
        {
            float width = Mathf.Max(0f, Mathf.Min(320f, screenWidth - 20f));
            float top = Mathf.Min(50f, Mathf.Max(0f, screenHeight - 42f));
            float available = Mathf.Max(0f, screenHeight - top - 10f);
            float height = Mathf.Min(expanded ? 248f : 42f, available);
            return new Rect(Mathf.Max(0f, screenWidth - width - 10f), top, width, height);
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;

        private void OnGUI()
        {
            if (_model == null) { LastPanelRect = Rect.zero; return; }
            GUI.skin.font = ViewAssets.UiFont(14);
            LastPanelRect = PanelRectFor(Screen.width, Screen.height, _expanded);
            GUI.Box(LastPanelRect, "");
            GUILayout.BeginArea(new Rect(LastPanelRect.x + 8f, LastPanelRect.y + 6f,
                Mathf.Max(0f, LastPanelRect.width - 16f), Mathf.Max(0f, LastPanelRect.height - 12f)));
            DrawHeader();
            if (_expanded) DrawEntries();
            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_expanded ? "英雄升级 ▲" : "英雄升级 ▼", GUILayout.Width(112f)))
            {
                SetExpanded(!_expanded);
#if UNITY_EDITOR
                InputAudit.Log("upgrade_panel expanded=" + _expanded);
#endif
            }
            GUILayout.FlexibleSpace();
            var state = _model.Describe();
            GUILayout.Label(state.Entries.Count > 0 ? "EP " + state.Ep.ToString("0.#") : "状态不可用");
            GUILayout.EndHorizontal();
        }

        private void DrawEntries()
        {
            var state = _model.Describe();
            if (state.Entries.Count == 0)
            {
                DrawFeedback("升级状态不可用", false);
                return;
            }
            foreach (var entry in state.Entries)
            {
                GUILayout.BeginHorizontal();
                string action = entry.Upgraded ? "已升级" : entry.Cost + "EP";
                if (GUILayout.Button(entry.Name + "  " + action, GUILayout.Width(132f), GUILayout.Height(25f)))
                    Execute(entry.Hero);
                GUILayout.Label(entry.Description, GUILayout.Height(25f));
                GUILayout.EndHorizontal();
            }
            if (_feedback != null && Time.unscaledTime < _feedbackUntil)
                DrawFeedback(_feedback, _feedbackOk);
        }

        private void Execute(HeroId hero)
        {
#if UNITY_EDITOR
            InputAudit.Log("upgrade_action hero=" + hero);
#endif
            var result = _model.Buy(hero);
            _feedbackOk = result.Success;
            _feedback = result.Message;
            _feedbackUntil = Time.unscaledTime + 2.5f;
#if UNITY_EDITOR
            InputAudit.Log("upgrade_result hero=" + hero + " result="
                + (result.Success ? "ok" : "error") + " code=" + result.Error
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
