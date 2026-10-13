using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>D19 全局小地图:右下角常驻,点击/拖动跳转镜头;雾叠加 + 敌方信息过滤 + 相机取景框。
    /// 2026-09-11 demo→成品迁移:IMGUI OnGUI → UI Toolkit。
    /// 结构:静态地形底图 UITK Image 一张 + **动态合成 overlay 贴图**(270×160,每帧重画:
    /// 雾/单位点/建筑点/取景框全部画进这一张,顺带消化 GUI 时代的 v 翻转与 texcoords 坑)。
    /// LastPanelRect 语义保留(worldBound 回填);点击/拖动改 UITK 指针事件。</summary>
    public sealed class MinimapPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = new Rect(0f, 0f, 0f, 0f); // 无内容时 w/h=0

        private SimCore _sim;
        private Side _player;
        private CameraRig _rig;
        private FogView _fog;
        private Camera _cam;
        private Texture2D _bg;

        private VisualElement _root;
        private Texture2D _overlay;
        private Color32[] _ovPx;
        private float _w, _h;

        public void Init(SimCore sim, Side player, CameraRig rig, FogView fog, Camera cam)
        {
            _sim = sim; _player = player; _rig = rig; _fog = fog; _cam = cam;
            // 底图:行序直填(row0=北=UITK 顶部,无需 GUI 时代的行翻转)
            _bg = new Texture2D(LaneGrid.Cols, LaneGrid.Rows, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point };
            var px = new Color32[LaneGrid.Rows * LaneGrid.Cols];
            for (int row = 0; row < LaneGrid.Rows; row++)
                for (int col = 0; col < LaneGrid.Cols; col++)
                {
                    var c = new SimVec2(col + 0.5f, row + 0.5f);
                    Color32 color;
                    if (!TerrainMap.IsInside(c)) color = new Color32(18, 20, 26, 255);
                    else if (TerrainMap.IsBridge(c)) color = new Color32(154, 122, 82, 255);
                    else if (TerrainMap.IsWater(c)) color = new Color32(63, 143, 214, 255);
                    else if (TerrainMap.IsIsland(c)) color = new Color32(95, 129, 73, 255);
                    else color = new Color32(120, 122, 126, 255);
                    px[row * LaneGrid.Cols + col] = color;
                }
            _bg.SetPixels32(px);
            _bg.Apply();

            _overlay = new Texture2D(LaneGrid.Cols, LaneGrid.Rows, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point };
            _ovPx = new Color32[LaneGrid.Rows * LaneGrid.Cols];

            if (UiRoot.Current != null && UiRoot.Current.Screen != null) Build(UiRoot.Current.Screen);
        }

        private void Build(VisualElement parent)
        {
            MinimapLayout.MiniSize(440f, 90f, out _w, out _h);
            _root = new VisualElement { name = "Minimap" };
            _root.style.position = Position.Absolute;
            _root.style.right = 12;
            _root.style.bottom = 12;
            _root.style.width = Mathf.RoundToInt(_w);
            _root.style.height = Mathf.RoundToInt(_h);
            _root.AddToClassList("map-frame");

            var bgEl = new VisualElement();
            bgEl.style.position = Position.Absolute;
            bgEl.style.left = 0; bgEl.style.right = 0; bgEl.style.top = 0; bgEl.style.bottom = 0;
            bgEl.style.backgroundImage = Background.FromTexture2D(_bg);
            _root.Add(bgEl);

            var ovEl = new VisualElement();
            ovEl.style.position = Position.Absolute;
            ovEl.style.left = 0; ovEl.style.right = 0; ovEl.style.top = 0; ovEl.style.bottom = 0;
            ovEl.style.backgroundImage = Background.FromTexture2D(_overlay);
            _root.Add(ovEl);

            // 点击/拖动跳转(旧 Event.current 语义 → UITK 指针)
            _root.RegisterCallback<PointerDownEvent>(evt =>
            {
                // 2026-09-14 制作人:"点背包里的道具 → 再点敌方建筑(世界或小地图)即可使用"。
                // 道具目标模式激活时,小地图这一下是**选目标**,不再兼做"跳视野"(否则目标和镜头一起动,没法瞄准)。
                if (Tool != null && Tool.HasPendingTarget && Tool.TryHandleMinimapClick(ToSimPoint(evt.localPosition)))
                    return;
                Jump(evt.localPosition);
            }, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.isPrimary && evt.pressure > 0f || (evt.pressedButtons & 1) != 0)
                    Jump(evt.localPosition);
            }, TrickleDown.TrickleDown);

            parent.Add(_root);
        }

        /// <summary>道具系统(小地图选目标用)。GameBootstrap 构建顺序保证 minimap 在 tools 之后。</summary>
        public ToolCommandPanel Tool;

        private SimVec2 ToSimPoint(Vector2 local)
        {
            float colM, rowM;
            MinimapLayout.MiniToWorld(local.x, local.y, _w, _h, out colM, out rowM);
            return new SimVec2(colM, rowM);
        }

        private void Jump(Vector2 local)
        {
            float colM, rowM;
            MinimapLayout.MiniToWorld(local.x, local.y, _w, _h, out colM, out rowM);
            if (_rig != null) _rig.SetFocus(colM, rowM);
        }

        private void Update()
        {
            if (_sim == null || _root == null) return;
            System.Array.Clear(_ovPx, 0, _ovPx.Length);

            // ① 雾(FogView.Tex 行序=row0 北,与 overlay 对齐,直接搬)
            // 2026-09-13 制作人"小地图几乎全黑":根因=世界迷雾色 (16,20,33) alpha≈255 被原样叠上来,
            // 未探索区把地形底图整个压黑。小地图只需表达"雾的形状",改小地图形专用浅雾(提亮 + 降不透明度)。
            if (_fog != null && _fog.Tex != null)
            {
                var fogPx = _fog.Tex.GetPixels32();
                int n = Mathf.Min(fogPx.Length, _ovPx.Length);
                for (int i = 0; i < n; i++)
                {
                    byte fa = fogPx[i].a;
                    if (fa == 0) continue;
                    _ovPx[i] = new Color32(46, 54, 72, (byte)(fa * 0.62f));
                }
            }

            // ② 建筑点(3×3 texel);敌方走迷雾口径(未知/无快照跳过)
            foreach (var kv in _sim.Entities)
            {
                var e = kv.Value;
                if (e == null) continue;
                if (e.Kind == EntityKind.Building)
                {
                    Color c;
                    if (e.Side == _player)
                    {
                        if (e.Rubbled) c = new Color(0.235f, 0.235f, 0.235f);
                        else c = SideColor(e.Side);
                    }
                    else
                    {
                        var st = FogRules.BuildingState(_sim, _player, e);
                        if (st == BuildingFogState.Unknown) continue;
                        var snap = VisionSystem.LastKnownBuilding(_sim, _player, e.Id);
                        if (snap == null) continue;
                        if (snap.Rubbled) c = new Color(0.235f, 0.235f, 0.235f);
                        else c = SideColor(snap.Side);
                    }
                    Dot(e.Pos.Col, e.Pos.Row, 1, c);
                }
                else if (e.Kind == EntityKind.Unit && e.State != EntityState.Dead)
                {
                    if (e.GarrisonBuildingId != 0) continue; // 驻军中,已含在建筑点内
                    bool visible = e.Side == _player || VisionSystem.IsUnitVisible(_sim, _player, e.Id);
                    if (!visible) continue;
                    Dot((int)e.PosF.X, (int)e.PosF.Y, e.IsSoldier ? 0 : 1, SideColor(e.Side));
                }
            }

            // ③ 相机取景框(白,1 texel 粗)
            DrawViewRect();

            _overlay.SetPixels32(_ovPx);
            _overlay.Apply();
        }

        private static Color SideColor(Side s)
        {
            return s == Side.Blue ? new Color(0.2f, 0.85f, 1f)
                : (s == Side.Red ? new Color(1f, 0.3f, 0.3f) : new Color(0.5f, 0.75f, 0.5f));
        }

        /// <summary>在 overlay 上画一个点;radius=0 单像素(士兵),1 三像素(将/建筑)。</summary>
        private void Dot(int col, int row, int radius, Color c)
        {
            var c32 = (Color32)c;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int y = row + dy, x = col + dx;
                    if (y < 0 || y >= LaneGrid.Rows || x < 0 || x >= LaneGrid.Cols) continue;
                    _ovPx[y * LaneGrid.Cols + x] = c32;
                }
        }

        private void DrawViewRect()
        {
            if (_cam == null) return;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            bool ok = true;
            var corners = new[]
            {
                new Vector3(0f, 0f), new Vector3(UnityEngine.Screen.width - 1f, 0f),
                new Vector3(UnityEngine.Screen.width - 1f, UnityEngine.Screen.height - 1f), new Vector3(0f, UnityEngine.Screen.height - 1f)
            };
            foreach (var sp in corners)
            {
                Ray ray = _cam.ScreenPointToRay(sp);
                float denom = Vector3.Dot(Vector3.up, ray.direction);
                if (Mathf.Abs(denom) < 1e-4f) { ok = false; break; }
                float t = -ray.origin.y / denom;
                if (t < 0f) { ok = false; break; }
                Vector3 p = ray.GetPoint(t);
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
            }
            if (!ok)
            {
                // 兜底:焦点 ± 半个视野宽度(透视下 orthographicSize 无意义,用 CameraRig 视野宽度语义)
                float hw = _rig != null ? _rig.ViewWidth * 0.5f : _cam.orthographicSize;
                Vector3 pos = _cam.transform.position;
                minX = pos.x - hw; maxX = pos.x + hw;
                minZ = pos.z - hw; maxZ = pos.z + hw;
            }
            float x0, y0, x1, y1;
            MinimapLayout.WorldToMini(minX, 3f - maxZ, LaneGrid.Cols, LaneGrid.Rows, out x0, out y0);
            MinimapLayout.WorldToMini(maxX, 3f - minZ, LaneGrid.Cols, LaneGrid.Rows, out x1, out y1);
            var white = new Color32(255, 255, 255, 255);
            int ix0 = Mathf.Clamp(Mathf.RoundToInt(x0), 0, LaneGrid.Cols - 1);
            int iy0 = Mathf.Clamp(Mathf.RoundToInt(y0), 0, LaneGrid.Rows - 1);
            int ix1 = Mathf.Clamp(Mathf.RoundToInt(x1), 0, LaneGrid.Cols - 1);
            int iy1 = Mathf.Clamp(Mathf.RoundToInt(y1), 0, LaneGrid.Rows - 1);
            for (int x = ix0; x <= ix1; x++)
            {
                _ovPx[iy0 * LaneGrid.Cols + x] = white;
                _ovPx[iy1 * LaneGrid.Cols + x] = white;
            }
            for (int y = iy0; y <= iy1; y++)
            {
                _ovPx[y * LaneGrid.Cols + ix0] = white;
                _ovPx[y * LaneGrid.Cols + ix1] = white;
            }
        }

        private void LateUpdate()
        {
            if (_root != null) LastPanelRect = UiKit.VisibleRectOrZero(_root);  // 拦截桥
            else LastPanelRect = new Rect(0f, 0f, 0f, 0f);
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;
    }
}
