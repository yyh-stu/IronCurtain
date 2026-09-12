using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// D19 镜头控制。2026-09-09 用户拍板:视野宽度制——默认宽=1/5 地图宽(约57米),
    /// 滚轮缩放(下缩上放);平移 = WASD/方向键 + 小地图点选;鼠标边缘滚动已删(干扰屏缘框选)。
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public float EdgeScrollSpeed = 28f;  // 键盘平移速度(米/秒),公开可配置

        public const float MapWidthM = 270f;
        public const float DefaultViewWidth = MapWidthM / 5f; // 57.2m
        public const float MinViewWidth = 22f;                // 近景上限(单位足够大)
        public const float MaxViewWidth = 400f;               // 2026-09-12 制作人:拉到能把整张地图框进来(270×160)

        // 初始焦点单一来源(flash 审查:两处硬编码易再发 bug2)
        public const float InitFocusCol = 40f;
        public const float InitFocusRow = 80f;   // 三路地图:中路居中(2026-09-11)

        private Camera _cam;
        private Vector3 _offset;             // 相机位置 - 焦点(保持既有构图)
        private Vector3 _offsetDir;          // 视线偏移单位向量(缩放改距离时方向不变 → 焦点恒在屏幕中心)
        private float _baseDist;             // Init 时的机位距离,透视 FOV 换算基准
        private float _focusCol, _focusRow;
        private float _viewWidth = DefaultViewWidth;

        // 透视缩放时 FOV 允许区间:超出即改由拉远/拉近补偿,避免超广角畸变与超长焦压平
        private const float MinFov = 22f;
        private const float MaxFov = 50f;

        /// <summary>当前视野宽度(米,横向)。</summary>
        public float ViewWidth { get { return _viewWidth; } }

        /// <summary>取证/工具用:直接设定视野宽度(米),与滚轮同语义同夹取。</summary>
        public void SetViewWidth(float w)
        {
            _viewWidth = Mathf.Clamp(w, MinViewWidth, MaxViewWidth);
            ApplyProjection();
        }

        public void Init(Camera cam, float focusColM, float focusRowM)
        {
            _cam = cam;
            _offset = cam.transform.position - WorldOf(focusColM, focusRowM);
            _offsetDir = _offset.normalized;
            _baseDist = _offset.magnitude;
            SetFocus(focusColM, focusRowM);
            ApplyProjection();
        }

        /// <summary>重算投影。改过 Camera.aspect 或切换正交/透视后必须调用,否则取景范围与实际不符
        /// (ART-P0 截图通道:批处理窗口 aspect 与实际不符,截图前先定死 16:9 再重算)。</summary>
        public void RefreshProjection()
        {
            ApplyProjection();
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
            // 面板悬停:只禁鼠标类输入(滚轮/边缘),键盘平移不受影响(flash 审查修正)
            bool overUi = OverRect(BarracksOverviewPanel.LastPanelRect, m) || OverRect(MinimapPanel.LastPanelRect, m)
                || OverRect(TopBarPanel.LastPanelRect, m);

            if (!overUi)
            {
                // 滚轮缩放:上=视野放大(拉远),下=视野缩小(拉近)
                float wheel = Input.GetAxis("Mouse ScrollWheel");
                if (wheel != 0f)
                    _viewWidth = Mathf.Clamp(_viewWidth * (1f + wheel * 0.18f), MinViewWidth, MaxViewWidth);
                ApplyProjection();
            }

            float dx = 0f, dy = 0f; // dy 为行米增量;北 = row 减(小地图 row0=北=顶部)
            // 2026-09-09 制作人拍板:删鼠标边缘滚动(干扰屏缘框选);平移 = WASD/方向键 + 小地图点选。
            // 同轮修复 W/S 方向反转(旧代码北=行增,方向写反,箭头同病)。
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) dx = -1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) dx = 1f;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) dy = -1f;   // 屏幕上 = 北 = row 减
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) dy = 1f; // 屏幕下 = 南 = row 增
            if (dx == 0f && dy == 0f) return;
            float step = EdgeScrollSpeed * Time.deltaTime;
            SetFocus(_focusCol + dx * step, _focusRow + dy * step);
        }

        // ART-P0 2026-09-10 相机改透视后,缩放不能再只写 orthographicSize(原实现只在正交分支生效,
        // 直接切透视会让滚轮静默失效)。统一语义仍是"焦点平面上的横向视野宽度 _viewWidth":
        // 正交用 orthographicSize 表达,透视用 FOV 表达;FOV 越界时改由沿视线拉远/拉近补偿。
        private void ApplyProjection()
        {
            if (_cam == null || _cam.aspect <= 0.01f) return;
            float halfV = _viewWidth / (2f * _cam.aspect);
            if (_cam.orthographic)
            {
                _cam.orthographicSize = halfV;
                return;
            }
            float fov = 2f * Mathf.Atan(halfV / _baseDist) * Mathf.Rad2Deg;
            float dist = _baseDist;
            if (fov < MinFov || fov > MaxFov)
            {
                fov = Mathf.Clamp(fov, MinFov, MaxFov);
                dist = halfV / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            }
            _cam.fieldOfView = fov;
            _offset = _offsetDir * dist;
            _cam.transform.position = WorldOf(_focusCol, _focusRow) + _offset;
        }
    }
}
