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
        private bool _contentBuilt;
        private string _stateKey = "";

        /// <summary>状态键:EP + 每个英雄的(等级/已购/可买) —— 变了才重绘。</summary>
        private static string BuildStateKey(UpgradeCommandState state)
        {
            // 2026-09-14 制作人:"下方按钮展开的面板还是不稳定,边框变动、点一下才恢复,而且这一下不算数" ——
            // 真因:键里含 `(int)EP`,而 EP 持续累积 → **整块正文(含购买按钮)每秒被 Clear+重建一次**,
            // 重建瞬间的高度变化就是"边框变动",点在重建瞬间的按钮上则"不算数"。
            // EP 文本本就在 Update 里逐帧原地更新(见上面 `_ep.text = ...`),键里**不需要**它;
            // 只保留"结构/阈值"类量(条目身份、已升级、是否买得起)。
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < state.Entries.Count; i++)
            {
                var e = state.Entries[i];
                sb.Append((int)e.Hero).Append(':').Append(e.Cost)
                    .Append(e.Upgraded ? '1' : '0').Append(e.Affordable ? '1' : '0').Append(',');
            }
            return sb.ToString();
        }
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
            if (!expanded) _stateKey = "";
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

            _body = new VisualElement();
            _body.style.marginTop = 6;
            _root.Add(_body);

            _feedbackLabel = UiKit.MakeLabel("", "feedback-ok");
            _feedbackLabel.style.marginTop = 4;
            _root.Add(_feedbackLabel);

            UiRoot.Current.RegisterCenterEntry("英雄升级", _root, () => SetExpanded(true), () => SetExpanded(false));   // 折叠=底部居中入口条,展开=屏幕中央
            ApplyExpanded();
        }

        private void ApplyExpanded()
        {
            if (_header == null) return;
            // 入口在屏幕底部、内容向屏幕中央(上方)展开 → 折叠态用 ▲(可向上展开),展开态用 ▼(可收起)
            _header.text = _expanded ? "英雄升级 ▼" : "英雄升级 ▲";
            if (_body != null) _body.style.display = DisplayStyle.Flex;   // 中央框里始终展开(显隐由 UiRoot 管)
            if (_feedbackLabel != null) _feedbackLabel.style.display = DisplayStyle.Flex;
        }

        /// <summary>2026-09-14 供实机自检:本面板累计重建正文的次数(正常应为 0 或极少)。
        /// 制作人报"面板不稳定、点一下才恢复、这一下不算数"的根因就是"每帧/每秒重建正文"。</summary>
        public static int Rebuilds;

        private void Update()
        {
            if (_model == null || _root == null) return;
            var state = _model.Describe();
            _ep.text = state.Entries.Count > 0 ? "EP " + state.Ep.ToString("0.#") : "状态不可用";

            // 2026-09-14 与道具/海克斯同源的坑:正文只在展开时构建一次 → 买完一个升级后
            // "已升级/EP 变化"都不会重绘。改按状态键重绘。
            string key = state.Entries.Count == 0 ? "inv" : BuildStateKey(state);
            if (_expanded && key != _stateKey) { _stateKey = key; RebuildEntries(state); }

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
            Rebuilds++;
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
            if (_root != null) LastPanelRect = UiKit.VisibleRectOrZero(_root);   // 输入拦截桥
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;
    }
}
