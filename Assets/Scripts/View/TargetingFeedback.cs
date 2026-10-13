using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>
    /// 2026-09-16 三十七轮(制作人 2026-09-15:"攻城锥不知道怎么用"):
    /// 道具目标模式的**战场可发现性**。机械链路此前就是通的(面板→点道具→点目标),
    /// 坏的是进入目标模式后战场零反馈 —— 不高亮合法目标、光标不变、无引导文案,
    /// 不知道"要点建筑"的人就是用不了。本组件三件事(提案已入交接文档):
    /// ① 攻城锥模式:所有**点得中的**敌方完好建筑脚下加红色脉冲目标环
    ///    (与 BuildingCommandModel.PickVisibleBuilding 同口径:敌方 + 非废墟 + 迷雾 Fresh);
    /// ② 目标模式期间战场常驻一行提示「选择目标 · 右键取消」(此前只有面板内 2.5 秒瞬时提示);
    /// ③ 光标换程序化准星,退出目标模式即还原。临时阵地模式:提示行 + 准星
    ///    (空格合法性逐格判定,没有可枚举的"合法目标集",不摆环)。
    /// 纯表现层:不碰 Sim、不拦截输入(提示行 PickingMode.Ignore)。
    /// </summary>
    public sealed class TargetingFeedback : MonoBehaviour
    {
        private SimCore _sim;
        private Side _player;
        private ToolCommandPanel _panel;
        private BoardView _board;

        private readonly Dictionary<int, GameObject> _marks = new Dictionary<int, GameObject>();
        private float _nextRefresh;
        private Label _hint;
        private Texture2D _cursor;
        private bool _cursorSet;

        // 实机自检读取口(IC_ACCEPT 判据用;非线程安全但只在主线程读写)
        public static int MarkCount { get; private set; }
        public static string HintText { get; private set; }
        public static bool CursorActive { get; private set; }
        public static bool GhostValid { get; private set; }   // T03:放置预览合法性

        private static readonly Color TargetRingColor = new Color(0.95f, 0.28f, 0.20f);
        private static readonly Color GhostOkColor = new Color(0.2f, 0.8f, 0.3f, 0.5f);
        private static readonly Color GhostBadColor = new Color(0.9f, 0.2f, 0.15f, 0.5f);

        private GameObject _ghost;   // T03:放置预览 Ghost(地面前置圆盘)

        public void Init(SimCore sim, Side player, ToolCommandPanel panel, BoardView board)
        {
            _sim = sim; _player = player; _panel = panel; _board = board;
        }

        private void Update()
        {
            if (_panel == null) return;
            bool active = _panel.HasPendingTarget;
            var mode = _panel.PendingTarget;

            // ① 目标环:只攻城锥模式摆(每 0.25s 重算合法集,脉冲每帧)
            if (active && mode == ToolTargetMode.Ram)
            {
                if (Time.unscaledTime >= _nextRefresh) { RefreshMarks(); _nextRefresh = Time.unscaledTime + 0.25f; }
                PulseMarks();
            }
            else if (_marks.Count > 0) ClearMarks();
            MarkCount = _marks.Count;

            // ①b T03:临时阵地放置预览(Ghost 圆盘 + 合法性着色)
            if (active && mode == ToolTargetMode.Fort) UpdateGhost();
            else HideGhost();

            // ② 常驻提示行:目标模式期间一直在(不是 2.5 秒闪一下)
            EnsureHint();
            if (active)
            {
                HintText = mode == ToolTargetMode.Ram
                    ? "攻城锥:点击敌方建筑(世界/小地图) · 右键取消"
                    : "临时阵地:点击地图内空格(不可水域/敌方建筑附近) · 右键取消";
                if (_hint.text != HintText) _hint.text = HintText;
                _hint.style.display = DisplayStyle.Flex;
            }
            else
            {
                HintText = null;
                _hint.style.display = DisplayStyle.None;
            }

            // ③ 准星光标
            if (active && !_cursorSet)
            {
                if (_cursor == null) _cursor = BuildCrosshair();
                UnityEngine.Cursor.SetCursor(_cursor, new Vector2(16f, 16f), CursorMode.Auto);
                _cursorSet = true;
            }
            else if (!active && _cursorSet)
            {
                UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                _cursorSet = false;
            }
            CursorActive = _cursorSet;
        }

        /// <summary>合法目标集 = 点得中的集合(敌方完好 + 迷雾 Fresh,与 PickVisibleBuilding 同一判定)。</summary>
        private void RefreshMarks()
        {
            if (_sim == null) return;
            // 先摘掉不再合法的
            var gone = new List<int>();
            foreach (var kv in _marks)
            {
                var b = _sim.GetEntity(kv.Key);
                if (b == null || b.Rubbled || kv.Value == null
                    || FogRules.BuildingState(_sim, _player, b) != BuildingFogState.Fresh)
                    gone.Add(kv.Key);
            }
            foreach (var id in gone) { Destroy(_marks[id]); _marks.Remove(id); }

            foreach (var kv in _sim.Entities)
            {
                var b = kv.Value;
                if (b == null || b.Kind != EntityKind.Building || b.Rubbled) continue;
                if (b.Side == _player || b.Side == Side.Neutral) continue;
                if (FogRules.BuildingState(_sim, _player, b) != BuildingFogState.Fresh) continue;
                if (_marks.ContainsKey(kv.Key)) continue;
                var view = _board != null ? _board.GetView(kv.Key) : null;
                if (view == null) continue;

                float radius = Mathf.Max(BuildingFootprint.HalfW(b), BuildingFootprint.HalfH(b)) + 0.6f;
                var ring = ViewAssets.DecoRing(ViewAssets.Unlit(TargetRingColor),
                    new Vector3(0f, 0.04f, 0f), radius, 0.12f, view.transform, "TargetMark");
                _marks[kv.Key] = ring;
            }
        }

        private void PulseMarks()
        {
            float s = 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 5f);
            foreach (var kv in _marks)
                if (kv.Value != null) kv.Value.transform.localScale = new Vector3(s, 1f, s);
        }

        private void ClearMarks()
        {
            foreach (var kv in _marks)
                if (kv.Value != null) Destroy(kv.Value);
            _marks.Clear();
        }

        private void EnsureHint()
        {
            if (_hint != null) return;
            var screen = UiRoot.Current != null ? UiRoot.Current.Screen : null;
            if (screen == null) return;
            _hint = new Label { name = "TargetingHint", pickingMode = PickingMode.Ignore };
            _hint.style.position = Position.Absolute;
            _hint.style.top = 58;                       // 顶栏之下、屏幕水平居中
            _hint.style.left = 0; _hint.style.right = 0;
            _hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hint.style.color = new Color(1f, 0.86f, 0.78f);
            _hint.style.backgroundColor = new Color(0.10f, 0.08f, 0.07f, 0.82f);
            _hint.style.fontSize = 15;
            _hint.style.paddingTop = 4; _hint.style.paddingBottom = 4;
            _hint.style.display = DisplayStyle.None;
            screen.Add(_hint);
        }

        /// <summary>32×32 程序化准星:红十字线 + 空心四角,无需美术资源。</summary>
        private static Texture2D BuildCrosshair()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var clear = new Color32(0, 0, 0, 0);
            var red = new Color32(238, 66, 52, 255);
            var dark = new Color32(40, 12, 8, 255);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color32 c = clear;
                    bool midX = x >= 14 && x <= 17, midY = y >= 14 && y <= 17;
                    bool ring = (x == 8 || x == 23) && y >= 8 && y <= 23 || (y == 8 || y == 23) && x >= 8 && x <= 23;
                    bool cross = (midX && (y >= 4 && y <= 11 || y >= 20 && y <= 27))
                              || (midY && (x >= 4 && x <= 11 || x >= 20 && x <= 27));
                    if (cross) c = red;
                    else if (ring && !(x > 11 && x < 20 && y > 11 && y < 20)) c = dark;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return tex;
        }

        // ================= T03:放置预览 Ghost =================

        /// <summary>每帧更新 Ghost 位置与合法性着色(射线→Sim 格→PlacementBlocked)。</summary>
        private void UpdateGhost()
        {
            if (_ghost == null) CreateGhost();
            if (_ghost == null) return;

            // 射线到地面平面(y=0)
            var cam = Camera.main;
            if (cam == null) { HideGhost(); return; }
            var ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            float t;
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out t)) { HideGhost(); return; }
            var hit = ray.GetPoint(t);

            // Unity → Sim 坐标(BoardView.WorldPos 的逆:SimVec2(x, 3-z))
            var simPos = new SimVec2(hit.x, 3f - hit.z);
            var cell = new GridPos((int)simPos.Y, (int)simPos.X);

            // 合法性(PlacementBlocked 契约已批,public)
            string reason;
            bool blocked = ToolsSystem.PlacementBlocked(_sim, _player, cell, out reason);
            GhostValid = !blocked;

            // 位置与着色
            _ghost.transform.position = new Vector3(hit.x, 0.06f, hit.z);
            _ghost.SetActive(true);
            var mr = _ghost.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var block = new MaterialPropertyBlock();
                mr.GetPropertyBlock(block);
                block.SetColor("_BaseColor", blocked ? GhostBadColor : GhostOkColor);
                mr.SetPropertyBlock(block);
            }
        }

        private void CreateGhost()
        {
            _ghost = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _ghost.name = "FX_PlacementGhost";
            var col = _ghost.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _ghost.transform.localScale = new Vector3(3.2f, 0.02f, 3.2f);   // 3×3m 占地
            var mr = _ghost.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) mr.sharedMaterial = new Material(shader);
        }

        private void HideGhost()
        {
            GhostValid = false;
            if (_ghost != null) _ghost.SetActive(false);
        }

        private void OnDisable() => ResetAll();
        private void OnDestroy() => ResetAll();

        private void ResetAll()
        {
            ClearMarks();
            MarkCount = 0; HintText = null; GhostValid = false;
            if (_cursorSet) { UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); _cursorSet = false; }
            CursorActive = false;
            if (_hint != null) _hint.style.display = DisplayStyle.None;
            if (_ghost != null) Destroy(_ghost);
        }
    }
}
