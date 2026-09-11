using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 地面 tile 材质仓(ART-P0 铺路)。贴图放在 Resources/Ground/,按 **4 米模块**铺:
    /// 一格(1 米)取模块内 1/4 的子区间,16 格拼成一张完整贴图,相邻模块自然续接。
    /// 贴图缺失时回退纯色,不影响既有观感与 294 条测试。
    /// </summary>
    public static class GroundTiles
    {
        public const int Module = 4;   // 一个模块 = 4×4 格(米)

        public const int KindStone = 0, KindStoneDark = 1, KindGrass = 2, KindGrassB = 3,
                         KindSand = 4, KindWaterShallow = 5, KindWaterDeep = 6, KindBridge = 7;

        private static readonly string[] Files =
        {
            "Ground/stone_a", "Ground/stone_b", "Ground/grass_a", "Ground/grass_b",
            "Ground/sand", "Ground/water_shallow", "Ground/water_deep", "Ground/bridge"
        };

        private static readonly Color[] Fallback =
        {
            new Color32(0x86, 0x88, 0x8d, 0xff), new Color32(0x78, 0x7a, 0x7e, 0xff),
            new Color32(0x5f, 0x81, 0x49, 0xff), new Color32(0x6d, 0x8f, 0x54, 0xff),
            new Color32(0xe8, 0xd2, 0x98, 0xff), new Color32(0x8e, 0xcf, 0xd6, 0xff),
            new Color32(0x4f, 0xbf, 0xcc, 0xff), new Color32(0xb0, 0xa8, 0x9a, 0xff)
        };

        private static Material[] _mats;
        private static Material[] _fringe;

        /// <summary>渐隐边缘材质(边界柔化用):贴图带 alpha 竖向渐变,按透明混合渲染。</summary>
        public static Material Fringe(int kind)
        {
            if (_fringe == null) _fringe = new Material[8];
            if (kind < 0 || kind >= 8) kind = KindGrass;
            if (_fringe[kind] != null) return _fringe[kind];
            var tex = Resources.Load<Texture2D>("Ground/fringe_" + FringeNames[kind]);
            var m = ViewAssets.Textured(tex, Color.white, 0.25f);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _fringe[kind] = m;
            return m;
        }

        // 2026-09-11:边界过渡材质(两地表 50/50 预混)。用素材解决"边界太清晰",
        // 不再依赖透明度/几何技巧(那两条路已两次翻车)。
        static readonly string[] Surfaces = { "road", "grass", "sand", "water", "bridge" };
        private static readonly Material[][] _blends = new Material[5][];

        /// <summary>边界过渡强度:50=两地表各半(默认);25=本色为主、轻过渡。制作人挑。</summary>
        public static int BlendStrength =
            System.Environment.GetEnvironmentVariable("IC_BLEND25") == "1" ? 25 : 50;

        /// <summary>kindA/kindB 的 50/50 过渡材质(总取字典序小的在前,保证同一对只建一份)。</summary>
        public static Material Blend(int kindA, int kindB)
        {
            if (kindA == kindB) return Mat(kindA);
            int lo = Mathf.Min(kindA, kindB), hi = Mathf.Max(kindA, kindB);
            if (_blends[lo] == null) _blends[lo] = new Material[5];
            if (_blends[lo][hi] != null) return _blends[lo][hi];
            string prefix = BlendStrength == 25 ? "Ground/blend25_" : "Ground/blend_";
            var tex = Resources.Load<Texture2D>(prefix + Surfaces[lo] + "_" + Surfaces[hi]);
            var m = ViewAssets.Textured(tex, Color.white, 0.25f);
            if (tex == null) m.SetColor("_BaseColor", new Color(0.5f, 0.5f, 0.5f));
            _blends[lo][hi] = m;
            return m;
        }

        static readonly string[] FringeNames =
        {
            "road", "road", "grass", "grass", "sand", "water", "water", "bridge"
        };

        public static Material Mat(int kind)
        {
            if (_mats == null) _mats = new Material[Files.Length];
            if (kind < 0 || kind >= Files.Length) kind = KindGrass;
            if (_mats[kind] != null) return _mats[kind];
            var tex = Resources.Load<Texture2D>(Files[kind]);
            bool wet = kind == KindWaterShallow || kind == KindWaterDeep;
            // 桥面/石路/草/沙:表面粗糙;水面略光;均为平铺贴图(4 米模块)
            // 河水:贴图偏白,材质基色压一点青色,避免在编辑器里读成"冰面"
            Color tint = wet ? new Color(0.72f, 0.90f, 0.98f) : Color.white;
            var m = ViewAssets.Textured(tex, tint, wet ? 0.75f : 0.25f);
            if (tex == null) m.SetColor("_BaseColor", Fallback[kind]);   // 贴图缺失回退
            _mats[kind] = m;
            return m;
        }

        /// <summary>格 (row,col) 在 4 米模块内的 UV 子区间(1/4 宽)。</summary>
        public static void UvRect(int row, int col, out float u0, out float v0, out float u1, out float v1)
        {
            float inv = 1f / Module;
            u0 = Mathf.Abs(col % Module) * inv;
            v0 = Mathf.Abs(row % Module) * inv;
            u1 = u0 + inv;
            v1 = v0 + inv;
        }
    }
}
