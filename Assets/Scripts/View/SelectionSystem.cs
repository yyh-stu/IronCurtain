using System.Collections.Generic;
using System.Text;
using IronCurtain.Sim;
using UnityEngine;
namespace IronCurtain.View
{
    /// <summary>
    /// M8.6 点选/框选己方(蓝方)主将 + 右键移动命令(手动级)+ T 键自动布阵 + 淡出提示 + 选中圈/点击标记。
    /// M6.2 增:单选射程圈 / 全选中移动路径线 / 顶栏点击穿透。
    /// </summary>
    public sealed class SelectionSystem : MonoBehaviour
    {
        private const float PickRadius = 3f;      // 点选主将命中半径(米)
        private const float HintDuration = 2.5f;  // 提示淡出时长(秒)
        private const float RangeRingWidth = 0.075f; // 射程环半宽(米)→ 视觉 0.15m 细线
        private const float PathLineWidth = 0.10f;   // 移动路径线宽(米)
        private const string HintText = "按 T 键自动布阵";
        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);
        private SimCore _sim;
        private Camera _cam;
        private readonly List<int> _selected = new List<int>();
        private SelectionInteractionCoordinator _interaction;
        public IReadOnlyList<int> SelectedIds => _selected;
        private bool _dragging;
        private Vector2 _dragStart;
        private string _selKey = "";
        // 2026-09-13 制作人:选中表现改由单位脚下的阵营光环自身承担(常驻阵营色 → 选中变绿)。
        // 旧的 2.6m 亮绿方块(RingRadius=1.3 的 DecoBox)已删除:它比光环大 17 倍、把光环整个盖住,
        // 且实心色块不传达任何信息,是"脚下绿块看不懂"的直接原因。
        private BoardView _board;
        private readonly List<int> _ringed = new List<int>();
        private float _hintT; // 提示剩余显示时间(秒)
        private GameObject _marker;
        private Material _markerMat;
        private float _markerT;
        private Rect _retreatBtnRect; // M8.7 撤退按钮占位(兼做点击穿透过滤)
        // M6.2 单选射程圈
        private GameObject _rangeRing;
        private Material _rangeRingMat;
        private Material _pathMat;
        private GUIStyle _btnStyle, _hintStyle;
        private Texture2D _btnTex;
        // M6.2 移动路径线(单位 id → 线;Update 每帧自管建/刷/拆)
        private readonly Dictionary<int, LineRenderer> _paths = new Dictionary<int, LineRenderer>();
        private Side _player = Side.Blue;
        public void Init(SimCore sim, Camera cam, Side playerSide)
        {
            _sim = sim;
            _cam = cam;
            _player = playerSide;
            _interaction = new SelectionInteractionCoordinator(sim, playerSide, _selected);
        }
        /// <summary>选中态要落到单位脚下的光环上,故需要视图注册表(id → EntityView)。</summary>
        public void SetBoardView(BoardView board) => _board = board;
        public void SetBuildingCommandPanel(BuildingCommandPanel panel) => _interaction.AttachBuildingPanel(panel);
        public void SetToolCommandPanel(ToolCommandPanel panel) => _interaction.AttachToolPanel(panel);
        private bool MouseOverPanel => SelectionInteractionCoordinator.IsPointerOverUi(_selected.Count, _retreatBtnRect);
        private void Update()
        {
            if (_sim == null || _cam == null) return;
            PruneSelection();
            _hintT = Mathf.Max(0f, _hintT - Time.deltaTime);
            HandleLeftButton();
            HandleRightButton();
            HandleFormationKey();
            UpdateRings();
            UpdateMarker();
        }
        private void PruneSelection()
        {
            for (int i = _selected.Count - 1; i >= 0; i--)
            {
                var e = _sim.GetEntity(_selected[i]);
                if (e == null || e.State == EntityState.Dead || e.IsSoldier)
                    _selected.RemoveAt(i);
            }
        }
        private void HandleLeftButton()
        {
            if (Input.GetMouseButtonDown(0) && !MouseOverPanel)
            {
                _dragging = true;
                _dragStart = Input.mousePosition;
            }
            if (!Input.GetMouseButtonUp(0) || !_dragging) return;
            _dragging = false;
            if (MouseOverPanel) { SelectionInteractionCoordinator.LogBlockedLeftRelease(); return; }
            Vector2 m = Input.mousePosition;
            float dx = m.x - _dragStart.x, dy = m.y - _dragStart.y;
            bool click = dx * dx + dy * dy < 64f; // 屏幕位移平方和 <64px² = 点选
            if (click)
            {
                bool hasGroundPoint = RayGround(out var wp);
                _interaction.HandleWorldClick(PickGeneral(), hasGroundPoint,
                    hasGroundPoint ? new SimVec2(wp.x, 3f - wp.z) : default);
            }
            else
            {
                _interaction.CloseTargetForBoxSelection();
                var rect = Rect.MinMaxRect(
                    Mathf.Min(_dragStart.x, m.x), Mathf.Min(_dragStart.y, m.y),
                    Mathf.Max(_dragStart.x, m.x), Mathf.Max(_dragStart.y, m.y));
                _selected.Clear();
                foreach (var kv in _sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier || e.Side != _player
                        || e.State == EntityState.Dead) continue;
                    var sp = _cam.WorldToScreenPoint(BoardView.WorldPos(e.PosF));
                    if (rect.Contains(new Vector2(sp.x, sp.y), true)) _selected.Add(e.Id);
                }
                SortSelectionByRole();   // 框选排序:近战在前、射手在后
            }
            SelectionInteractionCoordinator.LogSelection(click, _selected);
            if (!click && _selected.Count > 0) UiSfx.Select();   // 框选命中:程序化 UI 音
        }
        /// <summary>
        /// 2026-09-13 制作人:"框选时自动按重装在前、战士中、射手后(近战前射手后)的顺序排列"。
        /// 原实现按 Sim.Entities 字典顺序乱收。数据里没有区分重装/战士/射手三档,故按**射程升序**
        /// 排(近战 2~3m 在前、射手 9m 在后),同射程按 Id 稳定排。排序决定 T 键布阵的槽位指派顺序
        /// 与选中列表顺序 → 前排先落位、后排在后。
        /// </summary>
        private void SortSelectionByRole()
        {
            if (_selected.Count < 2) return;
            _selected.Sort((a, b) =>
            {
                var ea = _sim.GetEntity(a);
                var eb = _sim.GetEntity(b);
                float ra = ea != null ? ea.Range : 0f;
                float rb = eb != null ? eb.Range : 0f;
                if (Mathf.Abs(ra - rb) > 0.01f) return ra.CompareTo(rb);
                return a.CompareTo(b);
            });
        }

        private int PickGeneral()
        {
            Vector3 wp;
            if (!RayGround(out wp)) return 0;
            var p = new SimVec2(wp.x, 3f - wp.z);
            int best = 0;
            float bestD = PickRadius * PickRadius;
            int soldierGeneral = 0;
            float soldierD = PickRadius * PickRadius;
            foreach (var kv in _sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Unit || e.Side != _player || e.State == EntityState.Dead) continue;
                float dx = e.PosF.X - p.X, dy = e.PosF.Y - p.Y;
                float d = dx * dx + dy * dy;
                if (e.IsSoldier)
                {
                    if (d < soldierD) { soldierD = d; soldierGeneral = e.GeneralId; }
                }
                else if (d < bestD) { bestD = d; best = e.Id; }
            }
            if (best != 0) return best;      // 主将优先
            return soldierGeneral;           // 命中士兵 → 其主将
        }
        private void HandleRightButton()
        {
            if (!Input.GetMouseButtonDown(1) || MouseOverPanel) return;
            if (_interaction.CancelToolTargeting("right_click") || _selected.Count == 0) return;
            Vector3 wp;
            if (!RayGround(out wp)) return;
            var simPoint = new SimVec2(wp.x, 3f - wp.z);
            // 2026-09-14 制作人:"框选后**右键**敌方建筑,直接进攻(**前提是有设置偏好**);**左键**点开信息面板"。
            // 右键命中**非己方建筑**→ 按该建筑已设偏好下兵团令(无偏好时反馈里会提示先设偏好);其余仍是移动令。
            int hitBuilding = _interaction != null
                ? BuildingCommandModel.PickBuildingAny(_sim, simPoint) : 0;
            if (hitBuilding != 0)
            {
                var hb = _sim.GetEntity(hitBuilding);
                if (hb != null && hb.Side != _player && !hb.Rubbled)
                {
                    bool ordered = _interaction.RightClickBuilding(hitBuilding);
#if UNITY_EDITOR
                    InputAudit.Log("right_click_building building=" + hitBuilding + " ordered=" + ordered);
#endif
                    return;   // 右键点建筑不再顺带下移动令(避免"一边进攻一边走过去"互相打架)
                }
            }
            var grid = simPoint.ToGrid();
            foreach (var id in _selected)
                _sim.SetMoveTarget(id, grid, true);
            SelectionInteractionCoordinator.LogMoveCommand(grid, _selected);
            SpawnMarker(wp);
        }
        private void HandleFormationKey()
        {
            if (!Input.GetKeyDown(KeyCode.T) || !_interaction.ShouldApplyFormation(MouseOverPanel)) return;
            RegimentFormationSystem.Apply(_sim, _selected);
            _interaction.LogFormationApplied();
        }
        private bool RayGround(out Vector3 wp)
        {
            wp = Vector3.zero;
            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            float enter;
            if (!GroundPlane.Raycast(ray, out enter)) return false;
            wp = ray.GetPoint(enter);
            return true;
        }
        private void UpdateRings()
        {
            var key = BuildKey();
            if (key != _selKey)
            {
                bool grew = _selected.Count >= 2 && !_selKey.StartsWith(key);
                _selKey = key;
                if (grew) _hintT = HintDuration; // 仅选择变化瞬间重置,之后自然淡出
                RebuildRings();
            }
            // M6.2 射程圈跟单位走(单选)
            if (_rangeRing != null && _selected.Count == 1)
            {
                var e = _sim.GetEntity(_selected[0]);
                if (e != null) _rangeRing.transform.position = BoardView.WorldPos(e.PosF);
            }
            // M6.2 移动路径线:字典 id→线。有目标无钱则建(右键下令即现线),有线则刷新两端;目标消失则拆线
            var stale = new List<int>();
            foreach (var kv in _paths)
            {
                var e = _sim.GetEntity(kv.Key);
                if (kv.Value == null || e == null || !e.HasMoveTarget) { stale.Add(kv.Key); continue; }
                var s = BoardView.WorldPos(e.PosF); s.y += 0.1f;
                var t = BoardView.WorldPos(e.MoveTarget); t.y += 0.1f;
                kv.Value.SetPosition(0, s);
                kv.Value.SetPosition(1, t);
            }
            foreach (var id in stale)
            {
                if (_paths[id] != null) Destroy(_paths[id].gameObject);
                _paths.Remove(id);
            }
            foreach (var id in _selected)
            {
                if (_paths.ContainsKey(id)) continue;
                var e = _sim.GetEntity(id);
                if (e == null || !e.HasMoveTarget) continue;
                var go = new GameObject("PathLine");
                go.transform.SetParent(PathRoot, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = PathLineWidth;
                lr.endWidth = PathLineWidth;
                lr.useWorldSpace = true;
                // 2026-09-13:原为不透明亮绿(与选中绿撞色、且每条线新建一个材质),改柔和冷白 + 共享材质
                if (_pathMat == null) _pathMat = ViewAssets.UnlitFade(new Color(0.86f, 0.94f, 1f, 0.55f));
                lr.material = _pathMat;
                _paths[id] = lr;
            }
        }
        private string BuildKey()
        {
            var sb = new StringBuilder();
            foreach (var id in _selected) sb.Append(id).Append(',');
            return sb.ToString();
        }
        private void RebuildRings()
        {
            // M6.2 销毁旧射程圈(连带运行时网格与实例材质,避免每次换选泄漏 Mesh)
            DestroyRangeRing();
            // M6.2 销毁旧路径线(选择变化时全拆,Update 按需重建)
            foreach (var kv in _paths) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _paths.Clear();
            ApplySelectionVisuals();
            if (_selected.Count != 1) return;
            // M6.2 单选射程圈。2026-09-13 制作人:"改正常 moba 那种很细的颜色深的细线"
            // —— 去自发光(原 emission 0.6 的亮青正是廉价感来源),改深色半透明贴地刻度线。
            // 半径仍是单位射程(坠日/星落 9m),这是攻击范围语义,不是光环。
            var e = _sim.GetEntity(_selected[0]);
            if (e == null) return;
            var verts = new List<Vector3>[1];
            var tris = new List<int>[1];
            verts[0] = new List<Vector3>();
            tris[0] = new List<int>();
            ViewAssets.AddRing(verts[0], tris[0], new Vector3(0f, 0.06f, 0f), e.Range, RangeRingWidth, 64);
            Mesh mesh = ViewAssets.CreateMesh(verts, tris);
            if (_rangeRingMat == null) _rangeRingMat = ViewAssets.UnlitFade(new Color(0.05f, 0.08f, 0.10f, 0.55f));
            _rangeRing = ViewAssets.AttachMesh("RangeRing", transform, mesh, new[] { _rangeRingMat });
            _rangeRing.transform.position = BoardView.WorldPos(e.PosF);
        }

        private void DestroyRangeRing()
        {
            if (_rangeRing != null)
            {
                var mf = _rangeRing.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(_rangeRing);
            }
            _rangeRing = null;
        }

        /// <summary>
        /// 把选中态落到各单位脚下的阵营光环上:未选中=阵营色细环,选中=绿色加粗环。
        /// 一个环两种状态,不再额外画第二个环 —— 也就不会再出现"环叠环/环被色块盖住"。
        /// </summary>
        private void ApplySelectionVisuals()
        {
            for (int i = 0; i < _ringed.Count; i++)
            {
                var v = _board != null ? _board.GetView(_ringed[i]) : null;
                if (v != null) v.SetSelected(false);
            }
            _ringed.Clear();
            foreach (var id in _selected)
            {
                var v = _board != null ? _board.GetView(id) : null;
                if (v == null) continue;
                v.SetSelected(true);
                _ringed.Add(id);
            }
        }

        private void SpawnMarker(Vector3 wp)
        {
            ClearMarker();
            // 2026-09-13:原来的 2m 白色**方块**(DecoBox)改成细圆环,与射程环同一套贴地语言
            _markerMat = ViewAssets.UnlitFade(new Color(0.92f, 0.96f, 1f, 0.85f));
            _marker = new GameObject("ClickMarker");
            ViewAssets.DecoRing(_markerMat, new Vector3(0f, 0.05f, 0f), 1.8f, 0.12f, _marker.transform, "Ring");
            _marker.transform.position = wp;
            _markerT = 1f;
        }

        private void ClearMarker()
        {
            if (_marker != null) Destroy(_marker);
            if (_markerMat != null) Destroy(_markerMat); // 实例材质回收(原实现每次点击漏一个)
            _marker = null;
            _markerMat = null;
        }

        private void UpdateMarker()
        {
            if (_marker == null) return;
            _markerT -= Time.deltaTime;
            if (_markerT <= 0f) { ClearMarker(); return; }
            // 材质已是 Transparent 队列,alpha 淡出真的生效(原实现用不透明材质,淡出是空写,只能靠缩放兜底)
            if (_markerMat != null && _markerMat.HasProperty("_BaseColor"))
                _markerMat.SetColor("_BaseColor", new Color(0.92f, 0.96f, 1f, 0.85f * Mathf.Clamp01(_markerT)));
        }

        /// <summary>IMGUI 两处(撤退按钮/布阵提示)此前走 Unity 默认皮肤(灰底 Arial),与木皮 UITK 两个风格。</summary>
        private void EnsureGuiStyles()
        {
            if (_btnStyle != null) return;
            _btnTex = new Texture2D(1, 1);
            _btnTex.SetPixel(0, 0, new Color(0.10f, 0.13f, 0.18f, 0.92f));
            _btnTex.Apply();
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(0, 0, 0, 0),
                normal = { background = _btnTex, textColor = new Color(0.92f, 0.95f, 1f) },
                hover = { background = _btnTex, textColor = Color.white },
                active = { background = _btnTex, textColor = new Color(1f, 0.85f, 0.55f) },
            };
            _hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        private void OnGUI()
        {
            EnsureGuiStyles();
            if (_selected.Count >= 1)
            {
                // 2026-09-13 制作人"撤退和那几个按钮叠在一起"根因:IMGUI 的 Rect 是**屏幕像素**,
                // 而底部入口条是 UITK 的**面板单位** —— 缩放模式下二者不是一回事,居中摆放必然错位相撞。
                // 改摆**左下角**(底部条居中、小地图在右下,左下恒空),两边不再互相打架。
                _retreatBtnRect = new Rect(20f, Screen.height - 60f, 120f, 40f);
                if (GUI.Button(_retreatBtnRect, "撤退", _btnStyle))
                    foreach (var id in new List<int>(_selected))
                        RetreatSystem.OrderRetreat(_sim, id);
            }
            if (_selected.Count < 2 || _hintT <= 0f) return;
            var r = new Rect(Screen.width * 0.5f - 160f, 24f, 320f, 36f);
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_hintT / HintDuration)); // alpha 从 1 线性降到 0
            // IMGUI 无 text-outline,用深色偏移垫一层当描边(白字直接压战场会糊)
            _hintStyle.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), HintText, _hintStyle);
            GUI.Label(new Rect(r.x - 1.5f, r.y - 1.5f, r.width, r.height), HintText, _hintStyle);
            _hintStyle.normal.textColor = new Color(1f, 0.98f, 0.92f);
            GUI.Label(r, HintText, _hintStyle);
            GUI.color = prev;
        }

        private void OnDestroy()
        {
            DestroyRangeRing();
            ClearMarker();
            // 2026-09-14 换边残留:行军路径线原来直接挂在 bootstrap 下,组件拆了线还在(位置冻结、
            // 材质已销毁)。现在统一挂在 "SelPaths" 根下,换边按名字整棵拆;这里再兜一道。
            foreach (var kv in _paths) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _paths.Clear();
            if (_pathRoot != null) Destroy(_pathRoot.gameObject);
            if (_rangeRingMat != null) Destroy(_rangeRingMat);
            if (_pathMat != null) Destroy(_pathMat);
            if (_btnTex != null) Destroy(_btnTex);
            for (int i = 0; i < _ringed.Count; i++)
            {
                var v = _board != null ? _board.GetView(_ringed[i]) : null;
                if (v != null) v.SetSelected(false);
            }
            _ringed.Clear();
        }

        private GameObject _pathRoot;
        /// <summary>路径线容器(换边整棵拆;避免挂在 bootstrap 下变成孤儿)。</summary>
        private Transform PathRoot
        {
            get
            {
                if (_pathRoot == null)
                {
                    _pathRoot = new GameObject("SelPaths");
                    _pathRoot.transform.SetParent(transform, false);
                }
                return _pathRoot.transform;
            }
        }
    }
}
