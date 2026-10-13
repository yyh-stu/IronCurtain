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
    ///
    /// 2026-09-14 制作人"点开有内容但点了没用" + "面板下面加道具背包,点道具→点敌方建筑使用":
    /// 三个真缺陷一起修掉 ——
    /// ① **布局**:本面板是唯一给自己写 `position:absolute / left / top` 的中央面板,而它的父级
    ///    (UiRoot 的 `.center-frame`)高度只由在流子元素撑开 → 绝对定位的子元素不撑父级,
    ///    框高≈50px 而面板从框顶往下 60px 起画,**画到框外面去了**(所以"有内容"却看不见/点不到);
    /// ② **输入拦截**:`LateUpdate` 每帧无条件把 `LastPanelRect` 设成自己的 worldBound,于是屏幕正中央
    ///    凭空多出一块"UI 区",后续点建筑的那一下被判定为"点在 UI 上"而**被吞掉** → 道具永远用不出去;
    /// ③ **不重绘**:正文只在 `_expanded && !_contentBuilt` 时构建一次,选定道具后"取消当前目标"那行
    ///    和背包高亮永远不会出现。改为按状态键重绘。
    /// </summary>
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
            Show(mode == ToolTargetMode.Ram ? "请点击可见敌方建筑（世界或小地图）" : "请点击地图内空格", true);
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

        /// <summary>
        /// 小地图选目标(2026-09-14 制作人:"再点敌方建筑(世界或小地图)")。
        /// 小地图点的是**像素点**,可点住的敌方建筑比世界点击小得多 —— 所以这里按
        /// "小地图上的建筑点位"就近取(1 格内),而不是把像素点换算成世界点再去撞拾取容差。
        /// 返回 true = 这次点击被道具目标模式消费掉了(小地图不再执行"跳转视野")。
        /// </summary>
        public bool TryHandleMinimapClick(SimVec2 mapPoint)
        {
            if (_pending == ToolTargetMode.None) return false;
            var mode = _pending;
            ToolCommandFeedback result;
            if (mode == ToolTargetMode.Ram)
            {
                int target = PickEnemyBuildingNear(mapPoint, 2);
                result = _model.UseRam(target);
            }
            else if (!TerrainMap.IsInside(mapPoint) || !TerrainMap.IsInside(mapPoint.ToGrid()))
                result = new ToolCommandFeedback(false, ToolCommandError.InvalidTarget, "临时阵地需要地图内合法空格");
            else
                result = _model.UseFort(mapPoint.ToGrid());
            Report("use_minimap", mode, result);
            if (result.Success) _pending = ToolTargetMode.None;
            return true;
        }

        /// <summary>小地图取点:离该格最近的可选敌方建筑(rows/Cols 容差内)。0=没取到。</summary>
        private int PickEnemyBuildingNear(SimVec2 p, float cellTolerance)
        {
            if (_sim == null) return 0;
            int best = 0; float bestD = float.MaxValue;
            foreach (var kv in _sim.Entities)
            {
                var b = kv.Value;
                if (b == null || b.Kind != EntityKind.Building || b.Rubbled) continue;
                if (b.Side == _player || b.Side == Side.Neutral) continue;
                float d = SimVec2.Distance(new SimVec2(b.Pos.Col, b.Pos.Row), p);
                if (d > cellTolerance) continue;
                if (d < bestD) { bestD = d; best = b.Id; }
            }
            return best;
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
        private string _stateKey = "";
        public bool Expanded => _expanded;
        public void SetOpenedCallback(Action callback) => _openedCallback = callback;
        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            if (!expanded) _stateKey = "";   // 收起即作废内容,下次展开重绘
            if (expanded) _openedCallback?.Invoke();
            ApplyExpanded();
        }

        private void Build(VisualElement parent)
        {
            _root = new VisualElement { name = "ToolPanel" };
            _root.AddToClassList("panel");
            // 2026-09-14:去掉 position/left/top —— 中央框的尺寸靠**在流子元素**撑开,
            // 绝对定位的子元素既不撑框、又会被画到框外(根因见类注释①)。
            _root.style.minWidth = 320;

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

            _body = new VisualElement();
            _body.style.marginTop = 6;
            _root.Add(_body);

            _feedbackLabel = UiKit.MakeLabel("", "feedback-ok");
            _feedbackLabel.style.marginTop = 4;
            _root.Add(_feedbackLabel);

            UiRoot.Current.RegisterCenterEntry("道具", _root, () => SetExpanded(true), () => SetExpanded(false));
            ApplyExpanded();
        }

        private void ApplyExpanded()
        {
            if (_header == null) return;
            // 入口在屏幕底部、内容向屏幕中央(上方)展开 → 折叠态用 ▲,展开态用 ▼
            _header.text = _expanded ? "道具 ▼" : "道具 ▲";
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
            _ep.text = state.Valid ? "EP " + state.Ep.ToString("0.#") : "状态不可用";
            // 状态键:可抽性/EP/目标态 任一变化就重绘(原来只在展开时构建一次 → 选定后没有任何可视反馈)
            string key = state.Valid
                // 2026-09-14:键里去掉 `(int)EP`(持续累积 → 每秒整块重绘 = 制作人报的"面板不稳定/点一下不算数");
                // EP 文本逐帧原地更新,重绘只跟"结构/阈值"走。
                ? (state.RamCost + "|" + state.FortCost + "|" + (int)_pending
                    + "|" + (state.CanAffordRam ? 1 : 0) + (state.CanAffordFort ? 1 : 0))
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

        private void RebuildContents(ToolCommandState state)
        {
            Rebuilds++;
            _body.Clear();
            // 2026-09-13 临时诊断("道具点开无字符"):打印走了哪个分支
            Debug.Log("[ToolPanel] rebuild valid=" + state.Valid + " ep=" + state.Ep
                + " ram=" + state.RamCost + " fort=" + state.FortCost + " pending=" + _pending);
            if (!CanShowActions(state))
            {
                _body.Add(UiKit.MakeLabel("道具状态不可用", "feedback-err"));
                return;
            }

            // ===== 道具背包(制作人 2026-09-13:"道具面板下面加一个道具背包,点背包里的道具 →
            //       再点敌方建筑(世界或小地图)即可使用") =====
            _body.Add(UiKit.MakeLabel("道具背包", "label-title"));
            _body.Add(BackpackSlot(ToolTargetMode.Ram, "攻城锥", state.RamCost, state.CanAffordRam, "点选后点敌方建筑"));
            _body.Add(BackpackSlot(ToolTargetMode.Fort, "临时阵地", state.FortCost, state.CanAffordFort, "点选后点地图空格"));

            if (_pending != ToolTargetMode.None)
            {
                var targetRow = new VisualElement();
                targetRow.style.flexDirection = FlexDirection.Row;
                targetRow.style.alignItems = Align.Center;
                targetRow.style.marginTop = 4;
                targetRow.Add(UiKit.MakeLabel(
                    _pending == ToolTargetMode.Ram ? "目标：点敌方建筑(世界/小地图)" : "目标：点地图内空格", "label-dim"));
                var cancel = UiKit.MakeButton("取消当前目标", () => CancelTargeting("button"));
                cancel.style.height = 26; cancel.style.marginLeft = 8;
                targetRow.Add(cancel);
                _body.Add(targetRow);
            }
        }

        /// <summary>背包一格:名字 + 消耗 + 可用性;已选中时高亮并提示"再点目标"。</summary>
        private VisualElement BackpackSlot(ToolTargetMode mode, string name, int cost, bool affordable, string hint)
        {
            bool armed = _pending == mode;
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3;
            row.style.paddingLeft = 6; row.style.paddingRight = 6;
            row.style.paddingTop = 4; row.style.paddingBottom = 4;
            row.style.borderTopLeftRadius = 6; row.style.borderTopRightRadius = 6;
            row.style.borderBottomLeftRadius = 6; row.style.borderBottomRightRadius = 6;
            row.style.backgroundColor = armed
                ? new Color(0.16f, 0.42f, 0.24f, 0.95f)
                : new Color(0.12f, 0.16f, 0.22f, 0.85f);

            var label = UiKit.MakeLabel((armed ? "▶ " : "") + name + "  ×" + cost + " EP", "label");
            label.style.flexGrow = 1;
            row.Add(label);
            string btnText = !affordable ? "EP 不足" : (armed ? "已选中 · 点目标" : "选中");
            var b = UiKit.MakeButton(btnText, () => Arm(mode), affordable);
            // 2026-09-16 三十七轮:宽度定死为最大态(132)—— 原来 armed 切换 64↔132,
            // 选中一下整行重排,属制作人报的"面板内部元素莫名微变"之一类。
            b.style.width = 132;
            row.Add(b);
            if (!affordable) row.Add(UiKit.MakeLabel("（" + hint + "）", "label-dim"));
            return row;
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            // 2026-09-14:只在**真的可见**时才上报拦截矩形。原来无条件上报,于是屏幕中央常驻一块
            // 假"UI 区",把"点建筑用道具"的那一下吞掉 —— 这正是"点了没用"的机器层原因。
            // 2026-09-15:手写判定并入 `UiKit.VisibleRectOrZero`(同一口径,且补上"祖先被隐藏"那一档)。
            LastPanelRect = UiKit.VisibleRectOrZero(_root);
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
            Debug.Log("[ToolPanel] " + action + " mode=" + mode + " ok=" + result.Success + " msg=" + result.Message);
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
