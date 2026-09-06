using IronCurtain.Sim;

namespace IronCurtain.View
{
    /// <summary>小地图/镜头焦点共享纯逻辑:尺寸 fit、焦点钳制、世界↔小地图映射。禁 UnityEngine。</summary>
    public static class MinimapLayout
    {
        public const float FocusMargin = 6f;
        const float Rows = LaneGrid.Rows;   // 30
        const float Cols = LaneGrid.Cols;   // 286

        /// <summary>按 Cols:Rows 纵横比 fit 进 maxW×maxH。</summary>
        public static void MiniSize(float maxW, float maxH, out float w, out float h)
        {
            float aspect = Cols / Rows;
            w = maxW;
            h = w / aspect;
            if (h > maxH) { h = maxH; w = h * aspect; }
        }

        public static float ClampFocusX(float colM)
        {
            return colM < FocusMargin ? FocusMargin : (colM > Cols - FocusMargin ? Cols - FocusMargin : colM);
        }

        public static float ClampFocusY(float rowM)
        {
            return rowM < FocusMargin ? FocusMargin : (rowM > Rows - FocusMargin ? Rows - FocusMargin : rowM);
        }

        /// <summary>世界(列米,行米)→ 小地图像素;my 顶起(row0=北=顶部)。</summary>
        public static void WorldToMini(float colM, float rowM, float w, float h, out float mx, out float my)
        {
            mx = colM / Cols * w;
            my = rowM / Rows * h;
        }

        /// <summary>小地图像素(相对左上)→ 世界(列米,行米)。</summary>
        public static void MiniToWorld(float mx, float my, float w, float h, out float colM, out float rowM)
        {
            colM = mx / w * Cols;
            rowM = my / h * Rows;
        }
    }
}
