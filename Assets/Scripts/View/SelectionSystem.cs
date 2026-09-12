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
        private const float RingRadius = 1.3f;    // 选中圈半径(米)
        private const float HintDuration = 2.5f;  // 提示淡出时长(秒)
        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);
        private SimCore _sim;
        private Camera _cam;
        private readonly List<int> _selected = new List<int>();
        private SelectionInteractionCoordinator _interaction;
        public IReadOnlyList<int> SelectedIds => _selected;
        private bool _dragging;
        private Vector2 _dragStart;
        private string _selKey = "";
        private readonly List<GameObject> _rings = new List<GameObject>();
        private GameObject _ringRoot;
        private float _hintT; // 提示剩余显示时间(秒)
        private GameObject _marker;
        private Material _markerMat;
        private float _markerT;
        private Rect _retreatBtnRect; // M8.7 撤退按钮占位(兼做点击穿透过滤)
        // M6.2 单选射程圈
        private GameObject _rangeRing;
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
            }
            SelectionInteractionCoordinator.LogSelection(click, _selected);
            if (!click && _selected.Count > 0) UiSfx.Select();   // 框选命中:程序化 UI 音
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
            var grid = new SimVec2(wp.x, 3f - wp.z).ToGrid();
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
            for (int i = 0; i < _rings.Count && i < _selected.Count; i++)
            {
                var e = _sim.GetEntity(_selected[i]);
                if (e == null || _rings[i] == null) continue;
                _rings[i].transform.position = BoardView.WorldPos(e.PosF);
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
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = 0.15f;
                lr.endWidth = 0.15f;
                lr.useWorldSpace = true;
                lr.material = ViewAssets.Flat(new Color(0.3f, 1f, 0.5f, 0.5f));
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
            foreach (var r in _rings) if (r != null) Destroy(r);
            _rings.Clear();
            if (_ringRoot != null) { Destroy(_ringRoot); _ringRoot = null; }
            // M6.2 销毁旧射程圈
            if (_rangeRing != null) { Destroy(_rangeRing); _rangeRing = null; }
            // M6.2 销毁旧路径线(选择变化时全拆,Update 按需重建)
            foreach (var kv in _paths) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _paths.Clear();
            if (_selected.Count == 0) return;
            Material ringMat = ViewAssets.Mat(new Color(0.2f, 1f, 0.35f) * 0.5f, 0f, 0.5f, new Color(0.2f, 1f, 0.35f), 0.45f);
            _ringRoot = new GameObject("SelectionRings");
            foreach (var id in _selected)
            {
                var e = _sim.GetEntity(id);
                if (e == null) continue;
                ViewAssets.DecoBox(ringMat, new Vector3(0f, 0.03f, 0f),
                    new Vector3(RingRadius * 2f, 0.03f, RingRadius * 2f), _ringRoot.transform, "Ring" + id);
                _rings.Add(_ringRoot.transform.GetChild(_ringRoot.transform.childCount - 1).gameObject);
                _rings[_rings.Count - 1].transform.position = BoardView.WorldPos(e.PosF);
            }
            // M6.2 单选射程圈
            if (_selected.Count == 1)
            {
                var e = _sim.GetEntity(_selected[0]);
                if (e != null)
                {
                    var verts = new List<Vector3>[1];
                    var tris = new List<int>[1];
                    verts[0] = new List<Vector3>();
                    tris[0] = new List<int>();
                    ViewAssets.AddRing(verts[0], tris[0], new Vector3(0f, 0.06f, 0f), e.Range, 0.35f, 48);
                    Mesh mesh = ViewAssets.CreateMesh(verts, tris);
                    Material rm = ViewAssets.Mat(new Color(0.35f, 0.85f, 1f) * 0.4f, 0f, 0.5f, new Color(0.35f, 0.85f, 1f), 0.6f);
                    _rangeRing = ViewAssets.AttachMesh("RangeRing", transform, mesh, new[] { rm });
                    _rangeRing.transform.position = BoardView.WorldPos(e.PosF);
                }
            }
        }
        private void SpawnMarker(Vector3 wp)
        {
            if (_marker != null) Destroy(_marker);
            Material baseMat = ViewAssets.Flat(new Color(1f, 1f, 1f, 0.45f));
            _markerMat = Instantiate(baseMat); // 独立实例,可调 alpha
            _marker = new GameObject("ClickMarker");
            ViewAssets.DecoBox(_markerMat, new Vector3(0f, 0.04f, 0f), new Vector3(2f, 0.02f, 2f), _marker.transform, "Disc");
            _marker.transform.position = wp;
            _markerT = 1f;
        }

        private void UpdateMarker()
        {
            if (_marker == null) return;
            _markerT -= Time.deltaTime;
            if (_markerT <= 0f) { Destroy(_marker); _marker = null; _markerMat = null; return; }
            float a = Mathf.Clamp01(_markerT); // 1s 线性淡出
            if (_markerMat != null)
            {
                if (_markerMat.HasProperty("_BaseColor"))
                    _markerMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.45f * a));
                else _markerMat.color = new Color(1f, 1f, 1f, 0.45f * a);
            }
            _marker.transform.localScale = Vector3.one * Mathf.Max(0.05f, a); // 不透明材质下的可见消退兜底
        }

        private void OnGUI()
        {
            if (_selected.Count >= 1)
            {
                _retreatBtnRect = new Rect(Screen.width * 0.5f - 60f, Screen.height - 60f, 120f, 40f);
                if (GUI.Button(_retreatBtnRect, "撤退"))
                    foreach (var id in new List<int>(_selected))
                        RetreatSystem.OrderRetreat(_sim, id);
            }
            if (_selected.Count < 2 || _hintT <= 0f) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_hintT / HintDuration)); // alpha 从 1 线性降到 0
            GUI.Label(new Rect(Screen.width * 0.5f - 160f, 24f, 320f, 36f), "按 T 键自动布阵", style);
            GUI.color = prev;
        }
    }
}
