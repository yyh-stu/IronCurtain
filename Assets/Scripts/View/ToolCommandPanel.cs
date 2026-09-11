using System;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    public enum ToolTargetMode : byte { None, Ram, Fort }

    /// <summary>M6.3 道具入口；只负责目标模式和反馈，结算由 ToolCommandModel 完成。
    /// 2026-09-11 demo→成品迁移:IMGUI OnGUI → UI Toolkit。逻辑方法(Arm/TryHandleWorldClick/
    /// CancelTargeting/CanShowActions)与公开契约(PendingTarget/HasPendingTarget/Expanded/
    /// SetExpanded/SetOpenedCallback/LastPanelRect)全部原样保留,仅绘制层更换。
    /// 位置:左上、跟随兵营总览(暂仍为 IMGUI)底边,每帧对齐(LateUpdate)。</summary>
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

        private VisualElement _root, _body;
        private Button _header;
        private Label _ep, _feedbackLabel;

        public void Init(SimCore sim, Side player)
        {
            _sim = sim;
            _model = new ToolCommandModel(sim, player);
            _player = player;
            if (UiRoot.Current != null && UiRoot.Current.Screen != null) Build(UiRoot.Current.Screen);
            if (System.Environment.GetEnvironmentVariable("IC_UI_EXPAND") == "1")
                SetExpanded(true);   // 取证开关:展开拍按钮列表
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

        /// <summary>IMGUI 时代的布局参考;保留供兼容引用,实际布局改由 UITK 完成。</summary>
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

        private bool _expanded;
        public bool Expanded => _expanded;
        public void SetOpenedCallback(Action callback) => _openedCallback = callback;
        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            if (expanded) _openedCallback?.Invoke();
            ApplyExpanded();
        }

        private void Build(VisualElement parent)
        {
            _root = new VisualElement { name = "ToolPanel" };
            _root.AddToClassList("panel");
            _root.style.position = Position.Absolute;
            _root.style.left = 12;
            _root.style.top = 60;
            _root.style.width = 348;

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            _header = UiKit.MakeButton("道具 ▼", () =>
            {
                SetExpanded(!_expanded);
#if UNITY_EDITOR
                InputAudit.Log("tool_panel expanded=" + _expanded);
#endif
            });
            _header.style.width = 110;
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
            _header.text = _expanded ? "道具 ▲" : "道具 ▼";
            if (_body != null) _body.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
            if (_feedbackLabel != null) _feedbackLabel.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Update()
        {
            if (_model == null || _root == null) return;
            var state = _model.Describe();
            _ep.text = state.Valid ? "EP " + state.Ep.ToString("0.#") : "状态不可用";
            if (_expanded) RebuildContents(state);

            if (_feedback != null && Time.unscaledTime < _feedbackUntil)
            {
                _feedbackLabel.text = _feedback;
                _feedbackLabel.EnableInClassList("feedback-ok", _feedbackOk);
                _feedbackLabel.EnableInClassList("feedback-err", !_feedbackOk);
            }
            else if (_feedbackLabel != null) _feedbackLabel.text = "";
        }

        private void RebuildContents(ToolCommandState state)
        {
            _body.Clear();
            if (!CanShowActions(state))
            {
                _body.Add(UiKit.MakeLabel("道具状态不可用", "feedback-err"));
                return;
            }
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var ram = UiKit.MakeButton("攻城锥 " + state.RamCost + "EP", () => Arm(ToolTargetMode.Ram));
            ram.style.height = 32; ram.style.flexGrow = 1;
            var fort = UiKit.MakeButton("临时阵地 " + state.FortCost + "EP", () => Arm(ToolTargetMode.Fort));
            fort.style.height = 32; fort.style.flexGrow = 1; fort.style.marginLeft = 8;
            row.Add(ram); row.Add(fort);
            _body.Add(row);

            if (_pending != ToolTargetMode.None)
            {
                var targetRow = new VisualElement();
                targetRow.style.flexDirection = FlexDirection.Row;
                targetRow.style.alignItems = Align.Center;
                targetRow.style.marginTop = 4;
                targetRow.Add(UiKit.MakeLabel(
                    _pending == ToolTargetMode.Ram ? "目标：可见敌方建筑" : "目标：地图内空格", "label-dim"));
                var cancel = UiKit.MakeButton("取消当前目标", () => CancelTargeting("button"));
                cancel.style.height = 26; cancel.style.marginLeft = 8;
                targetRow.Add(cancel);
                _body.Add(targetRow);
            }
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            // 跟随兵营总览(暂仍 IMGUI)底边;它迁完后此跟随逻辑自然失效可删
            var b = BarracksOverviewPanel.LastPanelRect;
            float top = b.height > 0f ? b.yMax + 8f : 60f;
            _root.style.top = top;
            LastPanelRect = UiKit.WorldRect(_root);   // 输入拦截桥
        }

        private void OnDisable() { LastPanelRect = Rect.zero; _pending = ToolTargetMode.None; }
        private void OnDestroy() { LastPanelRect = Rect.zero; _pending = ToolTargetMode.None; }

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
