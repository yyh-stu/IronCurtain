using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>D19 全局小地图:右下角常驻,点击/拖动跳转镜头;雾叠加 + 敌方信息过滤 + 相机取景框。</summary>
    public sealed class MinimapPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = new Rect(0f, 0f, 0f, 0f); // 无内容时 w/h=0

        private SimCore _sim;
        private Side _player;
        private CameraRig _rig;
        private FogView _fog;
        private Camera _cam;
        private Texture2D _bg;

        public void Init(SimCore sim, Side player, CameraRig rig, FogView fog, Camera cam)
        {
            _sim = sim; _player = player; _rig = rig; _fog = fog; _cam = cam;
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
                    // GUI 纹理 v=0 画在 rect 底部=南方,而 row0=北 → 行序翻转
                    px[(LaneGrid.Rows - 1 - row) * LaneGrid.Cols + col] = color;
                }
            _bg.SetPixels32(px);
            _bg.Apply();
        }

        private static void DrawDot(Rect rect, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void OnGUI()
        {
            if (_sim == null || _bg == null) { LastPanelRect = new Rect(0f, 0f, 0f, 0f); return; }
            float w, h;
            MinimapLayout.MiniSize(440f, 90f, out w, out h);
            var rect = new Rect(Screen.width - w - 12f, Screen.height - h - 12f, w, h);
            LastPanelRect = rect;

            GUI.Box(rect, "");
            GUI.DrawTexture(rect, _bg, ScaleMode.StretchToFill);
            if (_fog != null && _fog.Tex != null)
                // 纹理 row0(北)在 v=0=rect 底部 → 垂直翻转绘制(与背景行序翻转同理)
                GUI.DrawTextureWithTexCoords(rect, _fog.Tex, new Rect(0f, 1f, 1f, -1f));

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
                        else c = e.Side == Side.Blue ? new Color(0.2f, 0.85f, 1f)
                            : (e.Side == Side.Red ? new Color(1f, 0.3f, 0.3f) : new Color(0.5f, 0.75f, 0.5f));
                    }
                    else
                    {
                        var st = FogRules.BuildingState(_sim, _player, e);
                        if (st == BuildingFogState.Unknown) continue;
                        var snap = VisionSystem.LastKnownBuilding(_sim, _player, e.Id);
                        if (snap == null) continue;
                        if (snap.Rubbled) c = new Color(0.235f, 0.235f, 0.235f);
                        else c = snap.Side == Side.Blue ? new Color(0.2f, 0.85f, 1f)
                            : (snap.Side == Side.Red ? new Color(1f, 0.3f, 0.3f) : new Color(0.5f, 0.75f, 0.5f));
                    }
                    DrawBuildingDot(rect, e, c);
                }
                else if (e.Kind == EntityKind.Unit && e.State != EntityState.Dead)
                {
                    if (e.GarrisonBuildingId != 0) continue; // 驻军中,已含在建筑点内
                    bool visible = e.Side == _player || VisionSystem.IsUnitVisible(_sim, _player, e.Id);
                    if (!visible) continue;
                    float size = e.IsSoldier ? 2f : 4f;
                    float mx, my;
                    MinimapLayout.WorldToMini(e.PosF.X, e.PosF.Y, w, h, out mx, out my);
                    DrawDot(new Rect(rect.x + mx - size * 0.5f, rect.y + my - size * 0.5f, size, size),
                        e.Side == Side.Blue ? new Color(0.2f, 0.85f, 1f)
                        : (e.Side == Side.Red ? new Color(1f, 0.3f, 0.3f) : new Color(0.5f, 0.75f, 0.5f)));
                }
            }

            DrawViewRect(rect, w, h);

            // 点击/拖动跳转
            var ev = Event.current;
            if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag) && rect.Contains(ev.mousePosition))
            {
                float colM, rowM;
                MinimapLayout.MiniToWorld(ev.mousePosition.x - rect.x, ev.mousePosition.y - rect.y, w, h, out colM, out rowM);
                if (_rig != null) _rig.SetFocus(colM, rowM);
                ev.Use();
            }
        }

        private void DrawBuildingDot(Rect rect, Entity e, Color c)
        {
            float mx, my;
            MinimapLayout.WorldToMini(e.Pos.Col, e.Pos.Row, rect.width, rect.height, out mx, out my);
            DrawDot(new Rect(rect.x + mx - 2.5f, rect.y + my - 2.5f, 5f, 5f), c);
        }

        private void DrawViewRect(Rect rect, float w, float h)
        {
            if (_cam == null) return;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            bool ok = true;
            var corners = new[]
            {
                new Vector3(0f, 0f), new Vector3(Screen.width - 1f, 0f),
                new Vector3(Screen.width - 1f, Screen.height - 1f), new Vector3(0f, Screen.height - 1f)
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
                // 兜底:焦点 ± 半个视野宽度。ART-P0 相机改透视后 orthographicSize 已无意义
                // (不再被写入,恒为 Unity 默认 5),改用 CameraRig 的视野宽度语义(正交/透视通用)。
                float hw = _rig != null ? _rig.ViewWidth * 0.5f : _cam.orthographicSize;
                Vector3 pos = _cam.transform.position;
                minX = pos.x - hw; maxX = pos.x + hw;
                minZ = pos.z - hw; maxZ = pos.z + hw;
            }
            float x0, y0, x1, y1;
            MinimapLayout.WorldToMini(minX, 3f - maxZ, w, h, out x0, out y0);
            MinimapLayout.WorldToMini(maxX, 3f - minZ, w, h, out x1, out y1);
            x0 += rect.x; y0 += rect.y; x1 += rect.x; y1 += rect.y;
            float t0 = 1.5f;
            var prev = GUI.color; GUI.color = Color.white;
            GUI.DrawTexture(new Rect(x0, y0 - t0 * 0.5f, x1 - x0, t0), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x0, y1 - t0 * 0.5f, x1 - x0, t0), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x0 - t0 * 0.5f, y0, t0, y1 - y0), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x1 - t0 * 0.5f, y0, t0, y1 - y0), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
