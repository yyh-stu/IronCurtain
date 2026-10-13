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
        private int _forcedHover;   // 抓帧钩子:非 0 时强制悬停该建筑
        private int _garrisonOrder; // 入城令:非 0 时行军中,抵达后登记入城
        private readonly List<int> _garrisonOrderIds = new List<int>();   // 入城令自带名单(不依赖实时框选)
        private Button _btnGarrison;
        private Button _hoverGarrison;
        private VisualElement _garrisonHost;
        private string _feedback;
        private bool _feedbackOk;
        private float _feedbackUntil;

        private VisualElement _root, _hoverHost, _markerHost;
        private Label _title, _status, _feedbackLabel;
        private Button _btnCapture, _btnDestroy, _btnRebuild, _btnClose;
        private VisualElement _hoverBox, _garrisonTip;
        private Label _hoverPref, _tipPower, _tipGenerals, _tipSoldiers, _tipEnemy;
        private int _hoverId;

        public bool HasTarget => _targetId != 0;
        public int TargetId => _targetId;
        /// <summary>最近一次反馈文案(自检/取证读取用;与 ToolCommandPanel.LastFeedback 同口径)。</summary>
        public string LastFeedback => _feedback;

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
            // 悬停/点选建筑:建筑按既有口径"永远可见可点",不做迷雾过滤(情报新旧由面板切片标注)
            return BuildingCommandModel.PickBuildingAny(_model.Sim, new SimVec2(wp.x, 3f - wp.z));
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
            _root.style.maxHeight = Length.Percent(58);   // 2026-09-13:驻军多时面板曾长到铺满全屏

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

            // 2026-09-13 制作人:"点开自己建筑后应该有建筑的信息,上面功能下面驻军标签,左键释放出城"。
            // Sim 侧接口早就有(GarrisonSystem.EnterFormal / Exit),缺的只是视图入口 —— 这里补上。
            _btnGarrison = UiKit.MakeButton("驻军", DoGarrison);
            _btnGarrison.style.height = 34;
            _btnGarrison.style.marginTop = 6;
            _btnGarrison.style.display = DisplayStyle.None;
            _root.Add(_btnGarrison);

            _garrisonHost = new VisualElement { name = "GarrisonList" };
            _garrisonHost.style.marginTop = 6;
            _garrisonHost.style.display = DisplayStyle.None;
            _root.Add(_garrisonHost);

            _feedbackLabel = UiKit.MakeLabel("", "feedback-ok");
            _feedbackLabel.style.marginTop = 4;
            _root.Add(_feedbackLabel);
            _root.style.display = DisplayStyle.None;
            // 2026-09-14 ESC 由 UiRoot 统一路由:这里只登记"有没有要关的东西"
            UiRoot.Current.RegisterEscClaim(() =>
            {
                if (_targetId == 0) return false;
                ClearTarget("esc");
                return true;
            });
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
            _hoverPref.style.top = 34;                       // 标签在锚点处,选择按钮在其上方
            _hoverPref.style.width = 120;
            _hoverPref.style.translate = new Translate(Length.Percent(-50), Length.Percent(0));
            var hoverRow = new VisualElement();
            hoverRow.style.flexDirection = FlexDirection.Row;
            hoverRow.style.position = Position.Absolute;
            hoverRow.style.top = 16;                         // 与下方"驻军信息"拉开,避免叠在一处
            hoverRow.style.translate = new Translate(Length.Percent(-50), Length.Percent(0));
            // 2026-09-14 制作人:"当点击敌方建筑时显示信息面板…显示占领/摧毁ui,删除现在的占领/摧毁ui,不要删多了"
            // → 悬停的 [占领]/[摧毁] 两颗按钮**已删除**,占领/摧毁改在"点开的信息面板"里操作。
            // 悬停其余内容一律保留:自家[入驻]入口、偏好标签、驻军 tooltip(不要删多了)。
            // 2026-09-13 制作人:"框选军队后鼠标在自己建筑的入驻没看到" → 悬停自家建筑给"入驻"入口
            _hoverGarrison = UiKit.MakeButton("入驻", () => DoGarrisonInto(_hoverId));
            _hoverGarrison.style.width = 76;
            _hoverGarrison.style.marginRight = 6;
            _hoverGarrison.style.display = DisplayStyle.None;
            hoverRow.Add(_hoverGarrison);
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
        /// <summary>入城令:全部抵达后登记入城(不瞬移)。建筑没了/选择空了则取消。</summary>
        private void TickGarrisonOrder()
        {
            var b = _model.Sim.GetEntity(_garrisonOrder);
            if (b == null || b.Rubbled || b.Side != _model.Player)
            {
                _garrisonOrder = 0;
                _garrisonOrderIds.Clear();
                Debug.Log("[Garrison] 取消:建筑没了/废墟/易主");
                return;
            }
            // ★ 用**令自带的名单**判定,不再看实时框选 —— 否则"点[驻军]的同一帧框选被清空"
            // (点击穿透/再点一下建筑等)会把入城令一起取消 → 军队走到了却不入城(制作人报的老 bug)。
            if (_garrisonOrderIds.Count == 0) { _garrisonOrder = 0; return; }
            float arrive = SimCore.BuildingCollisionRadius(b) + 1.5f;
            bool near = true, moving = false, alive = false;
            for (int i = 0; i < _garrisonOrderIds.Count; i++)
            {
                var u = _model.Sim.GetEntity(_garrisonOrderIds[i]);
                if (u == null || u.State == EntityState.Dead) continue;
                alive = true;
                if (u.IsSoldier) continue;                // 到位判定只看主将
                if (SimVec2.Distance(u.PosF, SimVec2.FromGrid(b.Pos)) > arrive) near = false;
                if (u.HasMoveTarget) moving = true;
            }
            if (!alive) { _garrisonOrder = 0; _garrisonOrderIds.Clear(); Debug.Log("[Garrison] 取消:名单内单位全没了"); return; }
            if (near)
            {
                var ids = new List<int>(_garrisonOrderIds);
                _garrisonOrder = 0;
                _garrisonOrderIds.Clear();
                string err;
                bool ok = GarrisonSystem.EnterFormal(_model.Sim, ids, b.Id, out err);
                Debug.Log("[Garrison] 抵达入城 building=" + b.Id + " ids=" + ids.Count
                    + " ok=" + ok + " err=" + (err ?? "-") + " 城内=" + b.GarrisonFormal.Count);
                SetFeedback(ok, ok ? ("已入驻 " + ids.Count + " 人") : "入驻失败");
                if (!ok && !string.IsNullOrEmpty(err)) _feedback = err;
                _garrisonKey = "";
                return;
            }
            // 兜底:主将已停下(无移动目标)却仍在半径外 → 取消,别让"行军中"永远挂着
            if (!moving)
            {
                _garrisonOrder = 0;
                _garrisonOrderIds.Clear();
                SetFeedback(false, "未抵达城下,已取消");
                Debug.Log("[Garrison] 取消:主将已停下但仍在半径外(可能被卡住/目的地不可达)");
            }
        }

        /// <summary>抓帧/自检:直接执行一次入驻(等价于点面板上的[入驻])。</summary>
        public void ForceGarrisonForTest() => DoGarrisonInto(_targetId);

        private void Update()
        {
            if (_garrisonOrder != 0) TickGarrisonOrder();
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
            var bld = _model.Sim.GetEntity(_targetId);
            bool own = bld != null && bld.Side == _model.Player;
            // 2026-09-14 制作人:"点击敌方建筑显示信息面板…显示占领/摧毁ui"。且沿用他对[驻军]的口径
            // ——"隐藏按钮会被判成'不行',要**灰显**"。所以敌方建筑上这两颗**常显**,能不能点由 SetEnabled
            // 表达(没框选军队 → 灰显,状态行已写明"请先选择己方主将")。Sim 侧可执行判定完全不变
            // (Execute/SetPreference 仍走 Describe 的 CanCapture/CanDestroy)。
            bool enemyTarget = bld != null && !own && !bld.Rubbled;
            _btnCapture.style.display = (info.CanCapture || enemyTarget) ? DisplayStyle.Flex : DisplayStyle.None;
            _btnDestroy.style.display = (info.CanDestroy || enemyTarget) ? DisplayStyle.Flex : DisplayStyle.None;
            _btnCapture.SetEnabled(info.CanCapture);
            _btnDestroy.SetEnabled(info.CanDestroy);
            _btnRebuild.style.display = info.CanRebuild ? DisplayStyle.Flex : DisplayStyle.None;
            if (info.CanRebuild) _btnRebuild.text = "重建 " + info.RebuildCost + " EP";
            // 2026-09-13 修:不可驻军的建筑(大本营/资源点 GarrisonFormal=0)不该显示[入驻]
            // —— 否则点了必被 Sim 拒("该建筑不可驻军"),看着就是"入驻没反应"。
            // 2026-09-13 制作人:"除了大本营都可以驻军"
            bool garrisonable = own && !bld.IsFort && bld.Building != BuildingId.Base;
            bool canEnter = garrisonable && !bld.Rubbled && !bld.IsFort
                && _selectedIds != null && _selectedIds.Count > 0;
            // 2026-09-13 制作人口径:
            //   不框选时左键建筑 → [驻军]**灰色**;框选时 → [驻军]**亮起**;点它把框选军队注入。
            // 此前是"无选中就隐藏按钮"→ 制作人看到按钮根本不出现,判定"驻军还是不行"。改为常显 + 灰显。
            _btnGarrison.style.display = own && !bld.Rubbled && !bld.IsFort
                ? DisplayStyle.Flex : DisplayStyle.None;
            _btnGarrison.SetEnabled(canEnter);
            _btnGarrison.text = !garrisonable ? "此建筑不可驻军"
                : (canEnter ? "驻军" : "驻军（先框选军队）");
            if (own) RebuildGarrisonList(bld);
            else RebuildEnemyGarrisonList(bld);   // 2026-09-14 制作人:敌方建筑显示驻军情报切片(带获取时间/未侦察显示"？")

            if (_feedback != null && Time.unscaledTime < _feedbackUntil)
            {
                _feedbackLabel.text = _feedback;
                _feedbackLabel.EnableInClassList("feedback-ok", _feedbackOk);
                _feedbackLabel.EnableInClassList("feedback-err", !_feedbackOk);
            }
            else _feedbackLabel.text = "";
        }

        // ===== 2026-09-13 驻军标签 + 释放出城(制作人第四条) =====
        private static readonly string[] HeroNames = { "磐石", "镇岳", "断峰", "逐风", "坠日", "星落" };
        private static string HeroNameOf(HeroId h)
        {
            int idx = (int)h;
            return idx >= 0 && idx < HeroNames.Length ? HeroNames[idx] : h.ToString();
        }

        private string _garrisonKey = "";
        private SimCore _simForBump;   // Bump 的静态取实体入口(RebuildGarrisonList 里赋值)

        private void SetFeedback(bool ok, string msg)
        {
            _feedbackOk = ok;
            _feedback = msg;
            _feedbackUntil = UnityEngine.Time.unscaledTime + 2.5f;
        }

        private void DoGarrison() => DoGarrisonInto(_targetId);

        private void DoGarrisonInto(int buildingId)
        {
            if (buildingId == 0) return;
            // 2026-09-13 制作人"入驻还是不行"的真因:GarrisonSystem.EnterFormal 只收**士兵**
            // (当时 Sim 里还有一句 `if (!u.IsSoldier) { error = "仅士兵可驻军"; }` 的门禁,早已删除),而框选选出来的**全是主将**
            // (框选明确排除士兵)→ 传进去的每个 id 都被挡掉,一个也进不去。
            // 正确做法:把选中主将**麾下的士兵**传进去(判定口径同 ReinforceSystem/RegimentFormation/Retreat)。
            var ids = new List<int>();
            var sim = _model.Sim;
            foreach (var id in _selectedIds)
            {
                var g = sim.GetEntity(id);
                if (g == null || g.State == EntityState.Dead) continue;
                if (g.IsSoldier) { ids.Add(id); continue; }
                ids.Add(id);   // 主将本人一起进城(2026-09-13 制作人口径:主将可和士兵一起驻军)
                foreach (var kv in sim.Entities)
                {
                    var u = kv.Value;
                    if (u != null && u.Kind == EntityKind.Unit && u.IsSoldier && u.GeneralId == id
                        && u.State != EntityState.Dead && u.GarrisonBuildingId == 0)
                        ids.Add(u.Id);
                }
            }
            if (ids.Count == 0)
            {
                SetFeedback(false, "该主将无随行士兵可入驻");
                return;
            }
            // 2026-09-13 制作人:"怎么瞬移过去了" —— 根因:DoEnterFormal 只改登记(GarrisonBuildingId),
            // 没有任何位移过程,于是全图任意位置的军队都会被直接塞进建筑 = 瞬移。
            // 改为**行军至城下再入城**:先给选中单位下令走到该建筑,抵达(进入建筑接触半径)后自动登记。
            var b = _model.Sim.GetEntity(buildingId);
            if (b == null) return;
            // 2026-09-13 修"军队过去了但不驻军":到达半径只看**主将** ——
            // 原来要求每个选中单位(含 12 个按阵型散开的士兵)都进 `碰撞半径+0.5`,士兵阵型天然更宽,
            // 于是永远判不到"到齐"→ 每帧重新下令 → 军队在城下打转、永不入城。
            float arrive = SimCore.BuildingCollisionRadius(b) + 1.5f;
            bool allNear = true;
            var dest = new GridPos(b.Pos.Row, b.Pos.Col);
            foreach (var id in _selectedIds)
            {
                var u = _model.Sim.GetEntity(id);
                if (u == null || u.IsSoldier) continue;   // 士兵随团,不单独判定
                if (SimVec2.Distance(u.PosF, SimVec2.FromGrid(b.Pos)) > arrive) { allNear = false; break; }
            }
            Debug.Log("[Garrison] 点驻军 building=" + buildingId + " 判定到齐=" + allNear + " 待入城=" + ids.Count);
            if (!allNear)
            {
                foreach (var id in ids) _model.Sim.SetMoveTarget(id, dest, true);
                _garrisonOrder = buildingId;
                _garrisonOrderIds.Clear();
                _garrisonOrderIds.AddRange(ids);   // ★ 令自带名单
                SetFeedback(true, "行军中 · 抵达后入城");
                Debug.Log("[Garrison] 发令 行军中 building=" + buildingId + " ids=" + ids.Count
                    + " (主将 " + _selectedIds.Count + " 选中)");
                return;
            }
            string err;
            bool ok = GarrisonSystem.EnterFormal(sim, ids, buildingId, out err);
            SetFeedback(ok, ok ? ("已入驻 " + ids.Count + " 人") : "入驻失败");
            if (!ok && !string.IsNullOrEmpty(err)) _feedback = err;
            _garrisonKey = "";   // 强制下次重建列表
        }

        private void DoExitFromGarrison(HeroId hero)
        {
            string err;
            int released;
            bool ok = GarrisonSystem.ExitRegiment(_model.Sim, _targetId, hero, out err, out released);
            // 2026-09-14:如实报数 —— 原来无论放出几个都写"已释放出城",
            // 只出一个主将时看着像"功能正常",其实士兵还锁在城里。
            SetFeedback(ok, ok ? ("已释放出城 " + released + " 人") : "释放失败");
            if (!string.IsNullOrEmpty(err)) _feedback = err;
            Debug.Log("[Garrison] 释放出城 building=" + _targetId + " hero=" + hero
                + " ok=" + ok + " released=" + released + " err=" + (err ?? "-"));
            _garrisonKey = "";
        }

        /// <summary>
        /// 驻军列表。**结构未变不重建** —— 每帧 Clear+重建会把"释放出城"按钮每帧销毁,
        /// 点击永远落不进去(总览"将领生产点不动"就是同一个坑)。
        /// </summary>
        private void RebuildGarrisonList(Entity b)
        {
            // 结构键:按英雄计数聚合(个体 id 变了但计数没变时不必重建)
            _simForBump = _model.Sim;
            var sb = new System.Text.StringBuilder("g");
            var kf = new Dictionary<HeroId, int>();
            for (int i = 0; i < b.GarrisonFormal.Count; i++) Bump(null, kf, b.GarrisonFormal[i]);
            var kt = new Dictionary<HeroId, int>();
            for (int i = 0; i < b.GarrisonTemp.Count; i++) Bump(null, kt, b.GarrisonTemp[i]);
            foreach (var kv in kf) sb.Append((int)kv.Key).Append('=').Append(kv.Value).Append(',');
            sb.Append('|');
            foreach (var kv in kt) sb.Append((int)kv.Key).Append('=').Append(kv.Value).Append(',');
            var key = sb.ToString();
            if (key == _garrisonKey) return;
            _garrisonKey = key;

            _garrisonHost.Clear();
            _garrisonHost.style.display = DisplayStyle.Flex;
            int total = b.GarrisonFormal.Count + b.GarrisonTemp.Count;
            _garrisonHost.Add(UiKit.MakeLabel("驻军 " + total, "label-title"));
            if (total == 0)
            {
                _garrisonHost.Add(UiKit.MakeLabel("  空(选中军队后点『入驻』)", "label-dim"));
                return;
            }
            // 2026-09-13:驻军可达 30+(兵营容量),逐个平铺会把面板撑到铺满全屏(抓帧实测),
            // 而 UITK 的 ScrollView 只给 maxHeight 建立不起滚动视口(试过,行被压成一坨)。
            // 改为**按英雄聚合**:一行 N 人 + 一个"释放出城"(Sim 的 Exit 每次释放一名,语义一致)。
            var order = new List<HeroId>();
            var formal = new Dictionary<HeroId, int>();
            var temp = new Dictionary<HeroId, int>();
            for (int i = 0; i < b.GarrisonFormal.Count; i++) Bump(order, formal, b.GarrisonFormal[i]);
            for (int i = 0; i < b.GarrisonTemp.Count; i++) Bump(order, temp, b.GarrisonTemp[i]);
            for (int i = 0; i < order.Count; i++)
            {
                var h = order[i];
                formal.TryGetValue(h, out int nf);
                temp.TryGetValue(h, out int nt);
                if (nf > 0) _garrisonHost.Add(GarrisonRow(h, "正式", nf));
                if (nt > 0) _garrisonHost.Add(GarrisonRow(h, "临时", nt));
            }
        }

        /// <summary>
        /// 2026-09-14 制作人口径:"当点击敌方建筑时,显示信息面板:**驻军切片(标记信息获取时间)**,
        /// 如果没得到过信息,显示"？"。” —— 敌方建筑读的是**侦察快照**(`VisionSystem.LastKnownBuilding`),
        /// 而不是实时驻军(那是迷雾信息,不该给玩家)。从未侦察过 → "？"。
        /// </summary>
        private void RebuildEnemyGarrisonList(Entity b)
        {
            var snap = b == null ? null : VisionSystem.LastKnownBuilding(_model.Sim, _model.Player, b.Id);
            string key = snap == null ? "e:?" 
                : ("e:" + snap.GarrisonCount + ":" + snap.Tick + ":" + (int)snap.Side + (snap.Rubbled ? "R" : ""));
            if (key == _garrisonKey) return;
            _garrisonKey = key;
            _garrisonHost.Clear();
            _garrisonHost.style.display = DisplayStyle.Flex;
            _garrisonHost.Add(UiKit.MakeLabel("驻军（情报）", "label-title"));
            if (snap == null)
            {
                _garrisonHost.Add(UiKit.MakeLabel("  驻军：？（未侦察到该建筑）", "label-dim"));
                return;
            }
            bool fresh = _model.Sim.Tick - snap.Tick < VisionSystem.FreshTicks;
            _garrisonHost.Add(UiKit.MakeLabel("  驻军：" + snap.GarrisonCount
                + "  ·  情报时间 " + ClockOf(snap.Tick), "label"));
            _garrisonHost.Add(UiKit.MakeLabel(fresh ? "  （视野内 · 实时）" : "  （可能已过期 · 靠近可刷新）",
                "label-dim"));
        }

        /// <summary>sim tick → 对局时钟 mm:ss(情报时间展示用)。</summary>
        private string ClockOf(long tick)
        {
            var sim = _model.Sim;
            double sec = tick / (double)sim.Config.TickRate;
            int m = (int)(sec / 60.0), s2 = (int)(sec % 60.0);
            return m.ToString("00") + ":" + s2.ToString("00");
        }

        private void Bump(List<HeroId> order, Dictionary<HeroId, int> counts, int unitId)
        {
            var u = _simForBump.GetEntity(unitId);
            var h = u != null && u.Hero.HasValue ? u.Hero.Value : HeroId.PanShi;
            if (counts != null)
            {
                int n; counts.TryGetValue(h, out n); counts[h] = n + 1;
            }
            if (order != null && !order.Contains(h)) order.Add(h);
        }

        private VisualElement GarrisonRow(HeroId hero, string kind, int count)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3;
            var lab = UiKit.MakeLabel(kind + " " + HeroNameOf(hero) + " ×" + count);
            lab.style.flexGrow = 1;
            row.Add(lab);
            var release = UiKit.MakeButton("释放出城", () => DoExitFromGarrison(hero));
            release.style.width = 96;
            row.Add(release);
            return row;
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

        /// <summary>鼠标是否落在该元素内。Input.mousePosition 是屏幕像素,y 向上;worldBound 是面板单位。</summary>
        private static bool MouseIn(VisualElement e)
        {
            if (e == null || e.resolvedStyle.display == DisplayStyle.None) return false;
            var b = e.worldBound;
            if (b.width <= 0.5f && b.height <= 0.5f) return false;
            float k = 1f;   // 屏幕像素 / 面板单位
            var ui = UiRoot.Current;
            if (ui != null && ui.Screen != null)
            {
                float pw = ui.Screen.resolvedStyle.width;
                if (!float.IsNaN(pw) && pw > 1f && UnityEngine.Screen.width > 0)
                    k = UnityEngine.Screen.width / pw;
            }
            var m = Input.mousePosition;
            return b.Contains(new Vector2(m.x * k, (UnityEngine.Screen.height - m.y) * k));
        }

        /// <summary>抓帧/自检:强制悬停指定建筑(传 0 解除)。</summary>
        public void ForceHover(int buildingId) => _forcedHover = buildingId;

        private void UpdateHoverSelector()
        {
            // 抓帧/自检钩子:强制悬停某建筑(正常游戏里恒为 0,不影响交互)
            int id = _forcedHover != 0 ? _forcedHover : HoveredBuilding(Input.mousePosition);
            // 2026-09-13 制作人"鼠标去点入驻,入驻直接消失了":悬停框只在鼠标压住建筑时显示,
            // 鼠标一移向按钮就离开建筑 → 框立刻隐藏 → 永远点不到。加**宽限**:鼠标停在悬停框/提示框
            // 内时保留上一次的悬停对象,让按钮点得着。
            if (id == 0 && _hoverId != 0 && (MouseIn(_hoverBox) || MouseIn(_garrisonTip)))
                id = _hoverId;
            var hovered = id != 0 ? _model.Sim.GetEntity(id) : null;
            // 自家建筑:仍然**不弹"占领/摧毁偏好"**(2026-09-10 制作人口径),
            // 但 2026-09-13 制作人要求悬停自家建筑能看到"入驻"入口 → 保留 id,只按阵营裁剪按钮。
            bool ownHover = hovered != null && hovered.Side == _model.Player;
            UpdateGarrisonTip(hovered);
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

            // 2026-09-13 二修:上轮"删除悬停 UI"把**敌方**那对[占领][摧毁]也一起删了,
            // 制作人报"鼠标放在对面的建筑上没有 UI" → 敌方建筑恢复悬停动作;自家仍只走"点开面板"。
            _hoverBox.style.display = ownHover ? DisplayStyle.None : DisplayStyle.Flex;   // 敌方只剩偏好标签(占领/摧毁按钮已移到信息面板)
            // 偏好按钮只对非己方建筑;自家建筑给"入驻"
            bool canEnter = ownHover && !bb.Rubbled && !bb.IsFort
                && _selectedIds != null && _selectedIds.Count > 0;
            _hoverGarrison.style.display = canEnter ? DisplayStyle.Flex : DisplayStyle.None;
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
                // 2026-09-14:按与 Sim 同一张交换力表算(主将 10/镇岳 12,士兵 1/镇岳兵 2)。
                // 旧写法只把"士兵的主将"(GeneralId != 0)算作将,于是**驻在城里的主将**被当成 1 点兵,
                // 兵员计数也虚增 1 —— 与 SiegeSystem.ExchangeWorth 的口径不一致。
                int genPower = 0, soldierPower = 0, genCount = 0, soldierCount = 0;
                foreach (var listName in new[] { 0, 1 })
                {
                    var list = listName == 0 ? b.GarrisonFormal : b.GarrisonTemp;
                    foreach (var sid in list)
                    {
                        var u = _model.Sim.GetEntity(sid);
                        if (u == null) continue;
                        bool zhenYue = u.Hero == HeroId.ZhenYue;
                        if (u.IsSoldier) { soldierCount++; soldierPower += zhenYue ? 2 : 1; }
                        else { genCount++; genPower += zhenYue ? 12 : 10; }
                    }
                }
                _tipPower.text = "兵力：" + (genPower + soldierPower);
                _tipGenerals.text = "将：" + genCount;
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
        /// <summary>
        /// 2026-09-14 制作人:"框选后**右键**敌方建筑,直接进攻(**前提是有设置偏好**);**左键**点开信息面板"。
        /// 右键走这里:不需要先开面板(所以显式传建筑 id),只对**已设偏好**的建筑下兵团令;
        /// 没设偏好时 OrderPreferred 会回"请先设置该据点进攻偏好"并显示在反馈行。
        /// </summary>
        public bool TryOrderPreferredOn(int buildingId)
        {
            if (buildingId == 0 || _model == null || _selectedIds == null || _selectedIds.Count == 0) return false;
            var r = _model.OrderPreferred(buildingId, _selectedIds);
            _feedback = r.Message;
            _feedbackOk = r.Success;
            _feedbackUntil = UnityEngine.Time.unscaledTime + 2f;
#if UNITY_EDITOR
            InputAudit.Log("building_right_order building=" + buildingId + " result=" + (r.Success ? "ok" : "deny")
                + " message=" + r.Message);
#endif
            return r.Success;
        }

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
            if (_root != null && _targetId != 0) LastPanelRect = UiKit.VisibleRectOrZero(_root);  // 2026-09-15:隐藏/关闭的面板不再留幽灵拦截区
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;
    }
}
