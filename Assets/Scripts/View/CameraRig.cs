using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>D18 镜头边缘滑动:鼠标贴屏幕边缘 → 匀速平移焦点;速度为公开可配置参数。</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public float EdgeScrollSpeed = 28f;  // 平移速度(米/秒),公开可配置
        public float EdgeMarginPx = 12f;     // 边缘带宽(像素)

        private Camera _cam;
        private Vector3 _offset;             // 相机位置 - 焦点(保持既有构图)
        private float _focusCol, _focusRow;

        public void Init(Camera cam, float focusColM, float focusRowM)
        {
            _cam = cam;
            _offset = cam.transform.position - WorldOf(focusColM, focusRowM);
            SetFocus(focusColM, focusRowM);
        }

        public void SetFocus(float colM, float rowM)
        {
            _focusCol = MinimapLayout.ClampFocusX(colM);
            _focusRow = MinimapLayout.ClampFocusY(rowM);
            if (_cam != null)
                _cam.transform.position = WorldOf(_focusCol, _focusRow) + _offset; // 旋转不动,偏移保构图
        }

        private static Vector3 WorldOf(float colM, float rowM)
        {
            return new Vector3(colM, 0f, 3f - rowM);
        }

        private static bool OverRect(Rect r, Vector2 mouse)
        {
            // GUI 坐标 y 从顶起,鼠标 y 从底起,需翻转
            return r.width > 0f && r.height > 0f && r.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        }

        private void Update()
        {
            if (_cam == null) return;
            Vector2 m = Input.mousePosition;
            // 悬停任何 IMGUI 面板时跳过(防误滚)
            if (OverRect(BarracksOverviewPanel.LastPanelRect, m) || OverRect(MinimapPanel.LastPanelRect, m)) return;

            float dx = 0f, dy = 0f; // dy 为行米增量(北=行增)
            if (m.x <= EdgeMarginPx) dx = -1f;
            else if (m.x >= Screen.width - EdgeMarginPx) dx = 1f;
            if (m.y >= Screen.height - EdgeMarginPx) dy = 1f;          // 屏幕上边缘 → 北
            else if (m.y <= EdgeMarginPx) dy = -1f;                     // 屏幕下边缘 → 南(rowY 减小)
            if (dx == 0f && dy == 0f) return;
            float step = EdgeScrollSpeed * Time.deltaTime;
            SetFocus(_focusCol + dx * step, _focusRow + dy * step);
        }
    }
}
