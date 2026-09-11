using System;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>M6.3 英雄升级入口；状态与购买结果全部来自 UpgradeCommandModel。
    /// 2026-09-11 demo→成品迁移:IMGUI OnGUI → UI Toolkit(theme.uss 三态按钮/面板皮肤);
    /// 公开契约(Expanded/SetExpanded/SetOpenedCallback/LastPanelRect)与 InputAudit 审计行全部保留,
    /// CommandPanelCoordinator 互斥链与 SelectionInteractionCoordinator 拦截链零改动。</summary>
    public sealed class UpgradeCommandPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = Rect.zero;

        private UpgradeCommandModel _model;
        private bool _expanded;
        private string _feedback;
        private bool _feedbackOk;
        private float _feedbackUntil;
        private Action _openedCallback;

        private VisualElement _root, _body;
        private Button _header;
        private Label _ep, _feedbackLabel;

        public bool Expanded => _expanded;

        public void Init(SimCore sim, Side player)
        {
            _model = new UpgradeCommandModel(sim, player);
            if (UiRoot.Current != null && UiRoot.Current.Right != null) Build(UiRoot.Current.Right);
            // 取证开关:IC_UI_EXPAND=1 展开面板拍按钮列表(成品观感证据)
            if (System.Environment.GetEnvironmentVariable("IC_UI_EXPAND") == "1")
                SetExpanded(true);
        }

        public void SetOpenedCallback(Action callback) => _openedCallback = callback;

        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            if (expanded) _openedCallback?.Invoke();
            ApplyExpanded();
        }

        /// <summary>IMGUI 时代的布局参考(屏幕右上);保留供兼容引用,实际布局改由 UITK 弹性完成。</summary>
        public static Rect PanelRectFor(int screenWidth, int screenHeight, bool expanded)
        {
            float width = Mathf.Max(0f, Mathf.Min(320f, screenWidth - 20f));
            float top = Mathf.Min(50f, Mathf.Max(0f, screenHeight - 42f));
            float available = Mathf.Max(0f, screenHeight - top - 10f);
            float height = Mathf.Min(expanded ? 248f : 42f, available);
            return new Rect(Mathf.Max(0f, screenWidth - width - 10f), top, width, height);
        }

        private void Build(VisualElement parent)
        {
            _root = new VisualElement { name = "UpgradePanel" };
            _root.AddToClassList("panel");
            _root.style.marginBottom = 8;   // 右侧列内堆叠(列容器负责定位)

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            _header = UiKit.MakeButton("英雄升级 ▼", () =>
            {
                SetExpanded(!_expanded);
#if UNITY_EDITOR
                InputAudit.Log("upgrade_panel expanded=" + _expanded);
#endif
            });
            _header.style.width = 132;
            head.Add(_header);
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; head.Add(spacer);
            _ep = UiKit.MakeLabel("", "label-dim");
            head.Add(_ep);
            _root.Add(head);

            _body = new VisualElement();
            _body.style.marginTop = 6;
            _root.Add(_body);

            _feedbackLabel = UiKit.MakeLabel("", "feedback-ok");
            _feedbackLabel.style.marginTop = 4;
            _root.Add(_feedbackLabel);

            parent.Add(_root);
            ApplyExpanded();
        }

        private void ApplyExpanded()
        {
            if (_header == null) return;
            _header.text = _expanded ? "英雄升级 ▲" : "英雄升级 ▼";
            if (_body != null) _body.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
            if (_feedbackLabel != null) _feedbackLabel.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Update()
        {
            if (_model == null || _root == null) return;
            var state = _model.Describe();
            _ep.text = state.Entries.Count > 0 ? "EP " + state.Ep.ToString("0.#") : "状态不可用";

            if (_expanded) RebuildEntries(state);

            if (_feedback != null && Time.unscaledTime < _feedbackUntil)
            {
                _feedbackLabel.text = _feedback;
                _feedbackLabel.EnableInClassList("feedback-ok", _feedbackOk);
                _feedbackLabel.EnableInClassList("feedback-err", !_feedbackOk);
            }
            else if (_feedbackLabel != null) _feedbackLabel.text = "";
        }

        private void RebuildEntries(UpgradeCommandState state)
        {
            _body.Clear();
            if (state.Entries.Count == 0)
            {
                var l = UiKit.MakeLabel("升级状态不可用", "feedback-err");
                _body.Add(l);
                return;
            }
            foreach (var entry in state.Entries)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 3;
                string action = entry.Upgraded ? "已升级" : entry.Cost + "EP";
                var btn = UiKit.MakeButton(entry.Name + "  " + action, () => Execute(entry.Hero), !entry.Upgraded);
                btn.style.width = 168;
                row.Add(btn);
                var desc = UiKit.MakeLabel(entry.Description, "label-dim");
                desc.style.marginLeft = 8;
                desc.style.flexGrow = 1;
                desc.style.whiteSpace = WhiteSpace.Normal;
                row.Add(desc);
                _body.Add(row);
            }
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

        private void LateUpdate()
        {
            if (_root != null) LastPanelRect = UiKit.WorldRect(_root);   // 输入拦截桥
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;
    }
}
