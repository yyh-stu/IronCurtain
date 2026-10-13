using System;
using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>M6.3 海克斯抽选存用入口；所有状态和结果由 HexCommandModel 提供。
    /// 2026-09-11 demo→成品迁移:IMGUI OnGUI → UI Toolkit;住 UiRoot.Right 右侧列(与升级面板自动堆叠)。
    /// 公开契约(Expanded/SetExpanded/SetOpenedCallback/LastPanelRect)与 InputAudit 审计行全部保留。</summary>
    public sealed class HexCommandPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = Rect.zero;

        private HexCommandModel _model;
        private IReadOnlyList<int> _selectedIds;
        private bool _expanded;
        private string _stateKey = "";
        private string _feedback;
        private bool _feedbackOk;
        private float _feedbackUntil;
        private Action _openedCallback;

        private VisualElement _root, _body, _scrollRoot;
        private Button _header, _drawBtn;
        private Label _ep, _feedbackLabel, _hintLabel;

        public bool Expanded => _expanded;

        public void Init(SimCore sim, Side player, IReadOnlyList<int> selectedIds)
        {
            _model = new HexCommandModel(sim, player);
            _selectedIds = selectedIds;
            if (UiRoot.Current != null && UiRoot.Current.Right != null) Build(UiRoot.Current.Right);
            if (System.Environment.GetEnvironmentVariable("IC_UI_EXPAND") == "1")
                SetExpanded(true);   // 取证开关:展开拍按钮列表
        }

        public void SetOpenedCallback(Action callback) => _openedCallback = callback;
        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            if (!expanded) _stateKey = "";   // 收起即作废内容,下次展开重绘
            if (expanded) _openedCallback?.Invoke();
            ApplyExpanded();
        }

        /// <summary>IMGUI 时代的布局参考;保留供兼容引用,实际布局改由 UITK 右侧列完成。</summary>
        public static Rect PanelRectFor(int screenWidth, int screenHeight,
            float reservedTop, float reservedBottom, bool expanded)
        {
            float width = Mathf.Max(0f, Mathf.Min(320f, screenWidth - 20f));
            float screenBottom = Mathf.Max(0f, screenHeight - 10f);
            float top = Mathf.Min(screenBottom, Mathf.Max(50f, reservedTop));
            float bottom = reservedBottom > top ? Mathf.Min(screenBottom, reservedBottom) : screenBottom;
            float available = Mathf.Max(0f, bottom - top);
            float height = Mathf.Min(expanded ? 310f : 42f, available);
            return new Rect(Mathf.Max(0f, screenWidth - width - 10f), top, width, height);
        }

        private void Build(VisualElement parent)
        {
            _root = new VisualElement { name = "HexPanel" };
            _root.AddToClassList("panel");
            _root.style.marginBottom = 8;

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            _header = UiKit.MakeButton("海克斯 ▼", () =>
            {
                SetExpanded(!_expanded);
#if UNITY_EDITOR
                InputAudit.Log("hex_panel expanded=" + _expanded);
#endif
            });
            _header.style.width = 118;
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

            UiRoot.Current.RegisterCenterEntry("海克斯", _root, () => SetExpanded(true), () => SetExpanded(false));   // 折叠=底部居中入口条,展开=屏幕中央
            ApplyExpanded();
        }

        private void ApplyExpanded()
        {
            if (_header == null) return;
            // 入口在屏幕底部、内容向屏幕中央(上方)展开 → 折叠态用 ▲,展开态用 ▼
            _header.text = _expanded ? "海克斯 ▼" : "海克斯 ▲";
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
            _ep.text = state.Valid
                ? "EP " + state.Ep.ToString("0.#") + " / 抽取 " + state.DrawCost
                : "状态不可用";
            // 2026-09-14 制作人"海克斯能看不能抽" —— 根因:内容只在 `_expanded && !_contentBuilt`
            // 时构建一次,而 `_contentBuilt` 只在收起时清零。于是"抽取成功 → 该出三选一按钮"这一刻
            // **面板不会再绘**,玩家只看到一句"已抽取，请三选一"却没有可点的卡,只能关掉再打开。
            // 改为按**状态键**重绘:三选一/手牌/当前效果/可抽性 任一变化就重建。
            string key = state.Valid
                ? (state.PendingChoices.Count + "|" + state.Hand.Count + "|" + (state.Active.HasValue ? (int)state.Active.Value : -1)
                    // 2026-09-14:去掉 `(int)EP`(持续累积 → 每秒整块重绘);EP 文本逐帧原地更新。
                    + "|" + (state.CanDraw ? 1 : 0) + "|" + state.NextDrawMinute)
                : "inv";
            if (_expanded && key != _stateKey) { _stateKey = key; RebuildContents(state); }

            if (_feedback != null && Time.unscaledTime < _feedbackUntil)
            {
                _feedbackLabel.text = _feedback;
                _feedbackLabel.EnableInClassList("feedback-ok", _feedbackOk);
                _feedbackLabel.EnableInClassList("feedback-err", !_feedbackOk);
            }
            else if (_feedbackLabel != null) _feedbackLabel.text = "";
        }

        private void RebuildContents(HexCommandState state)
        {
            Rebuilds++;
            _body.Clear();
            _scrollRoot = null;
            if (!state.Valid)
            {
                _body.Add(UiKit.MakeLabel("海克斯状态不可用", "feedback-err"));
                return;
            }
            string active = state.Active.HasValue ? _model.NameOf(state.Active.Value) : "无";
            if (state.Active.HasValue && state.ActiveSeconds > 0f)
                active += " " + state.ActiveSeconds.ToString("0") + "s";
            _body.Add(UiKit.MakeLabel("当前效果：" + active));

            string nodes = "";
            if (state.DrawNodes != null)
                for (int i = 0; i < state.DrawNodes.Length; i++)
                    nodes += (i > 0 ? "/" : "") + state.DrawNodes[i];
            string why;
            if (state.PendingChoices.Count > 0) why = "请先完成下面的三选一";
            else if (state.FreeDraw) why = "训练场：不限时不限次抽取";   // 2026-09-17 制作人拍板
            else if (state.InWindow) why = "当前窗口：第 " + state.WindowMinute + " 分钟（已开）";
            else if (state.NextDrawMinute > 0) why = "不在抽取窗口 · 下一窗口：第 " + state.NextDrawMinute + " 分钟（全程 " + nodes + "，各开 " + state.WindowSeconds + " 秒）";
            else why = "本局抽取窗口已全部结束";
            bool canDraw = state.PendingChoices.Count == 0;   // 三选一未完成时按钮灰显,避免"点了没反应"
            _drawBtn = UiKit.MakeButton("抽取三张（" + state.DrawCost + " EP）",
                () => Report("draw", null, _model.Draw()), canDraw);
            _drawBtn.style.height = 30;
            _drawBtn.style.marginTop = 4;
            _body.Add(_drawBtn);
            _hintLabel = UiKit.MakeLabel(why, state.InWindow ? "label-dim" : "feedback-err");
            _hintLabel.style.marginTop = 2;
            _body.Add(_hintLabel);

            // 滚动区:三选一 + 手牌(旧 IMGUI ScrollView 语义)
            var scroll = new ScrollView();
            scroll.style.maxHeight = 200;
            scroll.style.marginTop = 4;
            if (state.PendingChoices.Count > 0)
            {
                scroll.Add(UiKit.MakeLabel("本次三选一", "label-title"));
                foreach (var card in state.PendingChoices)
                {
                    var c = card;
                    var b = UiKit.MakeButton("选择「" + _model.NameOf(c) + "」", () => Report("choose", c, _model.Choose(c)));
                    b.style.height = 26; b.style.marginTop = 2;
                    scroll.Add(b);
                }
            }
            scroll.Add(UiKit.MakeLabel(state.Hand.Count == 0 ? "手牌：空" : "手牌", "label-dim"));
            foreach (var card in state.Hand)
            {
                var c = card;
                string suffix = c == HexId.Sandbag ? "（需单选己方单位）" : "";
                var b = UiKit.MakeButton("释放「" + _model.NameOf(c) + "」" + suffix,
                    () => Report("use", c, _model.Use(c, _selectedIds)));
                b.style.height = 26; b.style.marginTop = 2;
                scroll.Add(b);
            }
            _body.Add(scroll);
            _scrollRoot = scroll;
            Debug.Log("[HexPanel] rebuild pending=" + state.PendingChoices.Count + " hand=" + state.Hand.Count
                + " inWindow=" + state.InWindow + " next=" + state.NextDrawMinute);
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

        private void LateUpdate()
        {
            if (_root != null) LastPanelRect = UiKit.VisibleRectOrZero(_root);   // 输入拦截桥
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;
    }
}
