using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>M6.3-C1 紧凑建筑操作面板；动作与反馈全部由 BuildingCommandModel 提供。
    /// 2026-09-11 demo→成品迁移:IMGUI OnGUI → UI Toolkit。三件套:
    /// ①底部中央目标面板(标题/状态/占领/摧毁/重建/反馈/关闭);
    /// ②常显偏好标签(已设偏好的敌方据点上方,世界锚点);
    /// ③悬停选择器(悬停敌方/中立据点时其上方出现[占领][摧毁])。
    /// 世界锚点 = 世界→屏幕点 → RuntimePanelUtils.ScreenToPanel(处理 ScaleWithScreenSize 的坐标差)。
    /// 顺带修旧实现的隐患:OnGUI 的 ev.mousePosition(y 向下)被直接喂给 ScreenPointToRay(要 y 向上),
    /// 悬停拾取竖直方向镜像;现改用 Input.mousePosition(y 向上,口径正确)。
    /// 公开契约(HasTarget/TargetId/SetTarget/TryOrderPreferred/ClearTarget)与审计行原样保留。</summary>
    public sealed class BuildingCommandPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = Rect.zero;

        private BuildingCommandModel _model;
        private IReadOnlyList<int> _selectedIds;
        private int _targetId;
        private string _feedback;
        private bool _feedbackOk;
        private float _feedbackUntil;

        private VisualElement _root, _hoverHost, _markerHost;
        private Label _title, _status, _feedbackLabel;
        private Button _btnCapture, _btnDestroy, _btnRebuild, _btnClose;
        private VisualElement _hoverBox, _garrisonTip;
        private Label _hoverPref, _tipPower, _tipGenerals, _tipSoldiers, _tipEnemy;
        private Button _hoverCapture, _hoverDestroy;
        private int _hoverId;

        public bool HasTarget => _targetId != 0;
        public int TargetId => _targetId;

        public void Init(SimCore sim, Side player, IReadOnlyList<int> selectedIds)
        {
            _model = new BuildingCommandModel(sim, player);
            _selectedIds = selectedIds;
            if (UiRoot.Current != null && UiRoot.Current.Screen != null) Build(UiRoot.Current.Screen);
        }

        public void SetTarget(int buildingId)
        {
            _targetId = buildingId;
            _feedback = null;
        }

        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        private bool MouseOverOtherUi(Vector2 m)
        {
            Vector2 g = new Vector2(m.x, UnityEngine.Screen.height - m.y);
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

        // ---- UITK 构建 ----

        private void Build(VisualElement parent)
        {
            _root = new VisualElement { name = "BuildingPanel" };
            _root.AddToClassList("panel");
            _root.style.position = Position.Absolute;
            _root.style.bottom = 78;
            _root.style.left = Length.Percent(50);
            _root.style.translate = new Translate(Length.Percent(-50), Length.Percent(0));
            _root.style.width = 352;

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            _title = UiKit.MakeLabel("", "label-title");
            head.Add(_title);
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; head.Add(spacer);
            _btnClose = UiKit.MakeButton("×", () => ClearTarget("cancel"));
            _btnClose.style.width = 32;
            head.Add(_btnClose);
            _root.Add(head);

            _status = UiKit.MakeLabel("");
            _status.style.marginTop = 2;
            _root.Add(_status);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 6;
            _btnCapture = UiKit.MakeButton("占领", () => Execute(BuildingCommandAction.Capture));
            _btnDestroy = UiKit.MakeButton("摧毁", () => Execute(BuildingCommandAction.Destroy));
            _btnRebuild = UiKit.MakeButton("重建", () => Execute(BuildingCommandAction.Rebuild));
            foreach (var b in new[] { _btnCapture, _btnDestroy, _btnRebuild })
            {
                b.style.height = 34; b.style.flexGrow = 1;
                row.Add(b);
            }
            _root.Add(row);

            _feedbackLabel = UiKit.MakeLabel("", "feedback-ok");
            _feedbackLabel.style.marginTop = 4;
            _root.Add(_feedbackLabel);
            _root.style.display = DisplayStyle.None;
            parent.Add(_root);

            // 常显偏好标签容器(世界锚点)
            _markerHost = new VisualElement { name = "PrefMarkers" };
            _markerHost.style.position = Position.Absolute;
            _markerHost.style.left = 0; _markerHost.style.top = 0; _markerHost.style.right = 0; _markerHost.style.bottom = 0;
            _markerHost.pickingMode = PickingMode.Ignore;   // 纯标签,不吃指针
            parent.Add(_markerHost);

            // 悬停选择器(世界锚点):偏好小标签 + [占领][摧毁]
            _hoverBox = new VisualElement { name = "HoverSelector" };
            _hoverBox.style.position = Position.Absolute;
            _hoverBox.style.display = DisplayStyle.None;
            _hoverPref = UiKit.MakeLabel("", "marker");
            _hoverPref.style.position = Position.Absolute;
            _hoverPref.style.top = 30;                       // 标签在锚点处,选择按钮在其上方
            _hoverPref.style.width = 120;
            _hoverPref.style.translate = new Translate(Length.Percent(-50), Length.Percent(0));
            var hoverRow = new VisualElement();
            hoverRow.style.flexDirection = FlexDirection.Row;
            hoverRow.style.position = Position.Absolute;
            hoverRow.style.top = 0;                          // 锚点上方 30(盒子顶=锚点-30)
            hoverRow.style.translate = new Translate(Length.Percent(-50), Length.Percent(0));
            _hoverCapture = UiKit.MakeButton("占领", () => ChoosePreference(_hoverId, BuildingCommandAction.Capture));
            _hoverDestroy = UiKit.MakeButton("摧毁", () => ChoosePreference(_hoverId, BuildingCommandAction.Destroy));
            _hoverCapture.style.width = 76;
            _hoverDestroy.style.width = 76;
            _hoverDestroy.style.marginLeft = 6;
            hoverRow.Add(_hoverCapture); hoverRow.Add(_hoverDestroy);
            _hoverBox.Add(_hoverPref); _hoverBox.Add(hoverRow);
            _hoverBox.pickingMode = PickingMode.Ignore;    // 容器不吃指针,按钮自己吃
            parent.Add(_hoverBox);

            // 悬停驻军 tooltip(2026-09-11 制作人:鼠标放建筑上无需按下)
            _garrisonTip = new VisualElement { name = "GarrisonTip" };
            _garrisonTip.AddToClassList("tooltip-bg");
            _garrisonTip.style.position = Position.Absolute;
            _garrisonTip.style.display = DisplayStyle.None;
            _garrisonTip.pickingMode = PickingMode.Ignore;
            _tipPower = UiKit.MakeLabel("", "label");
            _tipGenerals = UiKit.MakeLabel("", "label-dim");
            _tipSoldiers = UiKit.MakeLabel("", "label-dim");
            _tipEnemy = UiKit.MakeLabel("", "label");
            _garrisonTip.Add(_tipPower); _garrisonTip.Add(_tipGenerals);
            _garrisonTip.Add(_tipSoldiers); _garrisonTip.Add(_tipEnemy);
            parent.Add(_garrisonTip);
        }

        private bool _demoDone;
        private void Update()
        {
            if (_model == null) return;
            // 取证开关 IC_UI_BUILDING=1:对一个已知敌方据点设偏好(常显标签)+对另一个开目标面板
            if (!_demoDone && System.Environment.GetEnvironmentVariable("IC_UI_BUILDING") == "1")
            {
                _demoDone = true;
                var ids = new List<int>();
                foreach (var kv in _model.Sim.Entities)
                {
                    var b = kv.Value;
                    if (b == null || b.Kind != EntityKind.Building || b.Rubbled) continue;
                    if (b.Side == _model.Player || b.Side == Side.Neutral) continue;
                    if (b.Building == BuildingId.Base) continue;
                    if (!VisionSystem.IsBuildingKnown(_model.Sim, _model.Player, b.Id)) continue;
                    ids.Add(b.Id);
                    if (ids.Count >= 2) break;
                }
                if (ids.Count >= 1) ChoosePreference(ids[0], BuildingCommandAction.Capture);   // 常显"占领"标签
                if (ids.Count >= 2) SetTarget(ids[1]);                                          // 底部目标面板
            }
            UpdateTargetPanel();
            UpdateMarkers();
            UpdateHoverSelector();
        }

        // ---- ① 目标面板 ----

        private void UpdateTargetPanel()
        {
            if (_targetId == 0)
            {
                _root.style.display = DisplayStyle.None;
                LastPanelRect = Rect.zero;
                return;
            }
            var info = _model.Describe(_targetId, _selectedIds);
            if (!info.Exists) { ClearTarget("invalid"); return; }

            _root.style.display = DisplayStyle.Flex;
            _title.text = info.Title;
            _status.text = info.Status;
            _btnCapture.style.display = info.CanCapture ? DisplayStyle.Flex : DisplayStyle.None;
            _btnDestroy.style.display = info.CanDestroy ? DisplayStyle.Flex : DisplayStyle.None;
            _btnRebuild.style.display = info.CanRebuild ? DisplayStyle.Flex : DisplayStyle.None;
            if (info.CanRebuild) _btnRebuild.text = "重建 " + info.RebuildCost + " EP";

            if (_feedback != null && Time.unscaledTime < _feedbackUntil)
            {
                _feedbackLabel.text = _feedback;
                _feedbackLabel.EnableInClassList("feedback-ok", _feedbackOk);
                _feedbackLabel.EnableInClassList("feedback-err", !_feedbackOk);
            }
            else _feedbackLabel.text = "";
        }

        // ---- ② 常显偏好标签(世界锚点) ----

        private void UpdateMarkers()
        {
            _markerHost.Clear();
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
                var l = UiKit.MakeLabel(b.Intent == SiegeIntent.Capture ? "占领" : "摧毁", "marker");
                PlaceAtPanel(l, sp, 80);
                _markerHost.Add(l);
            }
        }

        // ---- ③ 悬停选择器(世界锚点) ----

        private void UpdateHoverSelector()
        {
            int id = HoveredBuilding(Input.mousePosition);
            if (id != 0)
            {
                var b = _model.Sim.GetEntity(id);
                // 自家建筑不弹偏好选择(2026-09-10 制作人目击修复的口径)
                if (b != null && b.Side == _model.Player) id = 0;
            }
            UpdateGarrisonTip(id != 0 ? _model.Sim.GetEntity(id) : null);
            if (id == 0)
            {
                _hoverBox.style.display = DisplayStyle.None;
                _hoverId = 0;
                return;
            }
            _hoverId = id;
            var bb = _model.Sim.GetEntity(id);
            var sp = WorldToScreenAbove(bb, 2.6f);
            if (sp.z <= 0f) { _hoverBox.style.display = DisplayStyle.None; return; }

            _hoverBox.style.display = DisplayStyle.Flex;
            bool hasPref = bb.Intent != SiegeIntent.None && bb.IntentIssuer == _model.Player;
            _hoverPref.text = hasPref
                ? "偏好:" + (bb.Intent == SiegeIntent.Capture ? "占领" : "摧毁") : "";
            _hoverPref.style.display = hasPref ? DisplayStyle.Flex : DisplayStyle.None;

            // 标签在选择按钮上方
            var panelPt = ScreenToPanel(sp);
            _hoverBox.style.left = panelPt.x;
            _hoverBox.style.top = panelPt.y - 30;
        }

        /// <summary>悬停驻军信息(2026-09-11 制作人):无需按下。
        /// 己方=实时三行;敌方=侦察快照一行(快照只有总数,无将/兵拆分)。
        /// 兵力 = Σ(将交换值:镇岳12/其余10) + Σ(兵交换力:镇岳兵2/其余1)——制作人公式"将总数*将兵交换比+兵总数"。</summary>
        private bool _tipDemoDone;
        private void UpdateGarrisonTip(Entity b)
        {
            // 取证开关 IC_UI_TIP=1:无鼠标时对第一个己方建筑常显 tooltip
            if (b == null && !_tipDemoDone && System.Environment.GetEnvironmentVariable("IC_UI_TIP") == "1")
            {
                _tipDemoDone = true;
                foreach (var kv in _model.Sim.Entities)
                {
                    var cand = kv.Value;
                    if (cand == null || cand.Kind != EntityKind.Building || cand.Rubbled) continue;
                    if (cand.Side != _model.Player) continue;
                    b = cand;
                    break;
                }
            }
            if (b == null || b.Kind != EntityKind.Building || b.Rubbled)
            {
                _garrisonTip.style.display = DisplayStyle.None;
                return;
            }
            var sp = WorldToScreenAbove(b, 5.2f);
            if (sp.z <= 0f) { _garrisonTip.style.display = DisplayStyle.None; return; }

            if (b.Side == _model.Player)
            {
                int soldierPower = 0, soldierCount = 0;
                var gens = new HashSet<int>();
                foreach (var listName in new[] { 0, 1 })
                {
                    var list = listName == 0 ? b.GarrisonFormal : b.GarrisonTemp;
                    foreach (var sid in list)
                    {
                        var u = _model.Sim.GetEntity(sid);
                        if (u == null) continue;
                        soldierCount++;
                        soldierPower += u.Hero == HeroId.ZhenYue ? 2 : 1;
                        if (u.GeneralId != 0) gens.Add(u.GeneralId);
                    }
                }
                int genPower = 0;
                foreach (var gid in gens)
                {
                    var g = _model.Sim.GetEntity(gid);
                    if (g != null) genPower += g.Hero == HeroId.ZhenYue ? 12 : 10;
                }
                _tipPower.text = "兵力：" + (genPower + soldierPower);
                _tipGenerals.text = "将：" + gens.Count;
                _tipSoldiers.text = "兵：" + soldierCount;
                _tipEnemy.style.display = DisplayStyle.None;
                _tipPower.style.display = DisplayStyle.Flex;
                _tipGenerals.style.display = DisplayStyle.Flex;
                _tipSoldiers.style.display = DisplayStyle.Flex;
            }
            else
            {
                var snap = VisionSystem.LastKnownBuilding(_model.Sim, _model.Player, b.Id);
                if (snap == null) { _garrisonTip.style.display = DisplayStyle.None; return; }
                _tipEnemy.text = "驻军(侦察)：" + snap.GarrisonCount;
                _tipEnemy.style.display = DisplayStyle.Flex;
                _tipPower.style.display = DisplayStyle.None;
                _tipGenerals.style.display = DisplayStyle.None;
                _tipSoldiers.style.display = DisplayStyle.None;
            }
            var p = ScreenToPanel(sp);
            _garrisonTip.style.left = p.x;
            _garrisonTip.style.top = p.y;
            _garrisonTip.style.translate = new Translate(Length.Percent(-50), Length.Percent(0));
            _garrisonTip.style.display = DisplayStyle.Flex;
        }

        /// <summary>世界点(据点上方)→屏幕坐标(y 向下,已翻)。</summary>
        private Vector3 WorldToScreenAbove(Entity b, float y)
        {
            var cam = Camera.main;
            var wp = BoardView.WorldPos(b.Pos) + new Vector3(0f, y, 0f);
            var sp = cam.WorldToScreenPoint(wp);
            return new Vector3(sp.x, UnityEngine.Screen.height - sp.y, sp.z);
        }

        /// <summary>屏幕像素 → UITK 面板坐标(ScaleWithScreenSize 下两者不同)。</summary>
        private Vector2 ScreenToPanel(Vector3 sp)
        {
            if (_root.panel != null)
                return RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(sp.x, sp.y));
            return new Vector2(sp.x, sp.y);
        }

        /// <summary>把标签元素摆到世界锚点(中心对齐)。</summary>
        private void PlaceAtPanel(Label l, Vector3 sp, float width)
        {
            var p = ScreenToPanel(sp);
            l.style.position = Position.Absolute;
            l.style.left = p.x;
            l.style.top = p.y;
            l.style.width = width;
            l.style.translate = new Translate(Length.Percent(-50), Length.Percent(0));
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

        private void LateUpdate()
        {
            // 只有目标面板算拦截区(与旧口径一致;悬停选择器/常显标签不拦)
            if (_root != null && _targetId != 0) LastPanelRect = UiKit.WorldRect(_root);
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;
    }
}
