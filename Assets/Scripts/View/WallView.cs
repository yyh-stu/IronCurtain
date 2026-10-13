using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 城墙表现层 = **阵营色魔法屏障**(2026-09-11 制作人拍板)。
    /// 2026-09-11 深夜·外观定稿(制作人:"不是上下边缘可见,而是由下到上渐隐;高度矮了,可以再高一些,保持弧度"):
    /// - **由下到上渐隐**:底缘最实、一路向上淡到全透明 → 读作"从地面升起、向上消散的能量幕";
    /// - **加高并保持弧度**:Height 7.5→10.5 米,同时碗沿内收 9→12.6 米(等比放大,剖面斜率不变,
    ///   silhouette 与 `_barrier_bowl` 同形,只是整体变大;只加高不放大内收会让上段变成更陡的直墙);
    /// - **去掉六边格贴图**:密集六边在近景读成"网格布、影响观感"(制作人否掉),改**竖向渐隐**;
    /// - **去掉顶部亮边**:顶缘已渐隐到看不见,再挂一条亮边自相矛盾(等于撤回早先"顶部亮边"指令);
    ///   改为**贴地亮线**收口底边(与地面接触处给出清晰的一条实边)。
    /// 实现要点:渐隐坡度与底边都不依赖贴图资产,用**运行时生成的竖向 RGBA 渐变贴图**(按高度取 UV.y),
    /// 坡度形状即两条常量、可任意调,也不受导入贴图 alpha 设置影响。
    /// 其余口径不变:五段可击破面(各 200 血)、无大门(通行由 Sim 判定)、碗沿剖面、阵营色发光。
    /// </summary>
    public static class WallView
    {
        const float Height = 7.0f;         // 屏障高度(米;制作人:10.5 太高,至少削 1/3 → 7.0)
        const float LeanIn = 8.4f;         // 上沿向内收(米;LeanIn/Height=1.2 与上版等比,弧度不变)
        const float LeanPow = 2.2f;        // 幂指数:>1 → 下段近垂直、上段才弯
        const int Rows = 8;                // 竖向分段(渐隐要更细的竖剖才平滑)
        const int StepsPerPanel = 22;      // 每段沿弧细分(五段拼接成连续弧)
        const float BottomLineH = 0.32f;   // 贴地亮线高(米)

        // 渐隐坡度(幂次越大越靠下)。做成 static 字段以便出多档对照(见 SetFade);
        // 默认值=当前定稿值。
        static float BodyPow = 1.55f;       // 膜面:略偏下,中段更快淡出
        const float GlowPow = 2.40f;        // 辉光:能量集中在下段
        static float BodyAlpha = 0.40f;     // 贴地处膜面不透明度
        static float GlowGain = 0.19f;      // 辉光强度(加色)

        /// <summary>出多档对照用:调整渐隐参数(须在 Build 之前调用,材质在 Build 时成型)。</summary>
        public static void SetFade(float bodyAlpha, float bodyPow, float glowGain)
        {
            BodyAlpha = bodyAlpha; BodyPow = bodyPow; GlowGain = glowGain;
        }

        // 段号 → 该段的全部 renderer(膜面/辉光/底边);击碎时 HidePanel 整组隐藏
        private static readonly Dictionary<int, List<Renderer>> _panels = new Dictionary<int, List<Renderer>>();

        public static void Build(Transform parent)
        {
            _panels.Clear();
            foreach (var side in new[] { Side.Blue, Side.Red })
            {
                Color tint = side == Side.Blue
                    ? new Color(0.35f, 0.78f, 1f)      // 蓝方:冰蓝
                    : new Color(1f, 0.42f, 0.38f);     // 红方:赤红
                var c = WallSystem.BaseCenter(side);
                var center = new Vector2(c.X, 3f - c.Y);   // 世界系(Sim y=行,世界 z 反向)

                for (int panel = 0; panel < WallSystem.Panels; panel++)
                {
                    if (WallSystem.IsBroken(side, panel)) continue;  // 已击碎 → 该段不画(留出通路)
                    float a0 = WallSystem.PanelAngle(side, panel) - WallSystem.PanelHalf(side, panel);
                    float a1 = WallSystem.PanelAngle(side, panel) + WallSystem.PanelHalf(side, panel);
                    AddPanel(parent, side, panel, center, a0, a1, tint);
                }
            }
        }

        /// <summary>击碎一段:隐藏该段全部网格。WallBroken 事件驱动,运行中即时生效。</summary>
        public static void HidePanel(Side side, int panel)
        {
            List<Renderer> list;
            if (!_panels.TryGetValue(Key(side, panel), out list)) return;
            foreach (var r in list)
                if (r != null) r.enabled = false;
        }

        /// <summary>膜面(体相层)显隐开关 —— 审美 A/B 用:关掉只剩辉光+底边。</summary>
        public static void SetBodyVisible(bool visible)
        {
            foreach (var kv in _panels)
            {
                var list = kv.Value;
                if (list.Count > 0 && list[0] != null) list[0].enabled = visible;
            }
        }

        // ---- 曲面几何助手(涟漪贴面/血条定位/碎片发射共用,避免各处各算一套) ----

        /// <summary>屏障可见高度(米)。</summary>
        public static float HeightMeters { get { return Height; } }

        /// <summary>碗沿剖面在高度 y 处的实际半径(下段≈Radius,上段内收 LeanIn)。</summary>
        public static float SurfaceRadiusAt(float y)
        {
            float t = Mathf.Clamp01(y / Height);
            return WallSystem.Radius - LeanIn * Mathf.Pow(t, LeanPow);
        }

        /// <summary>段内角度比例 t∈[0,1]、高度 y 处的曲面点(世界坐标)。</summary>
        public static Vector3 SurfacePoint(Side side, int panel, float t, float y)
        {
            float a = Mathf.Lerp(WallSystem.PanelAngle(side, panel) - WallSystem.PanelHalf(side, panel),
                WallSystem.PanelAngle(side, panel) + WallSystem.PanelHalf(side, panel), Mathf.Clamp01(t));
            float rad = SurfaceRadiusAt(y);
            var c = WallSystem.BaseCenter(side);
            // 与 AddPanel 同构:世界 z = (3−c.Y) − sin·rad(Sim y=行向下,世界 z 反向)
            return new Vector3(c.X + Mathf.Cos(a) * rad, y, (3f - c.Y) - Mathf.Sin(a) * rad);
        }

        /// <summary>曲面法线(碗沿倾斜:上段法线朝上偏)。涟漪环贴面用。</summary>
        public static Vector3 SurfaceNormal(Side side, int panel, float t, float y)
        {
            float a = Mathf.Lerp(WallSystem.PanelAngle(side, panel) - WallSystem.PanelHalf(side, panel),
                WallSystem.PanelAngle(side, panel) + WallSystem.PanelHalf(side, panel), Mathf.Clamp01(t));
            var radial = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));   // 水平外向
            // 剖面斜率 d(半径)/dy = −LeanIn·LeanPow/Height · t^(LeanPow−1) → 法线抬起
            float tf = Mathf.Clamp01(y / Height);
            float slope = -LeanIn * LeanPow / Height * Mathf.Pow(Mathf.Max(tf, 0.001f), LeanPow - 1f);
            return (radial - Vector3.up * slope).normalized;
        }

        static int Key(Side side, int panel) { return (int)side * 8 + panel; }

        // ---- 网格构建 ----

        /// <summary>一段屏障幕:沿弧的碗沿条带(下近垂直上内弯),竖向渐隐(底实顶透)+贴地亮线。</summary>
        static void AddPanel(Transform parent, Side side, int lane, Vector2 center,
            float a0, float a1, Color tint)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            for (int i = 0; i <= StepsPerPanel; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)StepsPerPanel);
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float fu = i / (float)StepsPerPanel;                 // 弧向采样轴(渐隐只随高度变)
                for (int r = 0; r <= Rows; r++)
                {
                    float rt = r / (float)Rows;
                    float rad = WallSystem.Radius - LeanIn * Mathf.Pow(rt, LeanPow);
                    float x = center.x + ca * rad;
                    float z = center.y - sa * rad;                   // Sim→世界 z 反向
                    v.Add(new Vector3(x, Height * rt, z));
                    uv.Add(new Vector2(fu, rt));                     // uv.y = 高度比例 → 采样渐隐贴图
                }
            }
            int stride = Rows + 1;
            for (int i = 0; i < StepsPerPanel; i++)
            {
                for (int r = 0; r < Rows; r++)
                {
                    int b0 = i * stride + r;
                    int b1 = (i + 1) * stride + r;
                    // 双面(屏障两面都要看得见;URP 默认背面剔除)
                    t.Add(b0); t.Add(b0 + 1); t.Add(b1 + 1);
                    t.Add(b0); t.Add(b1 + 1); t.Add(b1);
                    t.Add(b0); t.Add(b1 + 1); t.Add(b0 + 1);
                    t.Add(b0); t.Add(b1); t.Add(b1 + 1);
                }
            }
            var mesh = ViewAssets.CreateMesh(new List<Vector3>[] { v }, new List<int>[] { t },
                new List<Vector2>[] { uv });

            // ① 膜面(体相层):Alpha 混合,底实顶透 —— 渐隐本体
            var bodyMat = AlphaMat(new Color(tint.r * 0.62f, tint.g * 0.62f, tint.b * 0.62f, 1f),
                GradTex(false, BodyPow, BodyAlpha), 3000);
            var bodyGo = ViewAssets.AttachMesh("BarrierBody_" + side + "_" + lane, parent, mesh,
                new Material[] { bodyMat });

            // ② 辉光层(加色):坡度更陡,能量集中在下段(配 Bloom)
            // 加色层的渐隐走 RGB(rgbRamp=true)——alpha 对加色无效
            var glowMat = AdditiveMat(tint, GradTex(true, GlowPow, GlowGain), 3001);
            var glowGo = ViewAssets.AttachMesh("BarrierGlow_" + side + "_" + lane, parent, mesh,
                new Material[] { glowMat });

            // ③ 贴地亮线:与地面接触处一条清晰实边,收口底边
            var edgeGo = ViewAssets.AttachMesh("BarrierBase_" + side + "_" + lane, parent,
                BuildBottomLine(center, a0, a1), new Material[] { AlphaMat(EdgeColor(tint), null, 3002) });

            var list = new List<Renderer>();
            var br = bodyGo.GetComponent<MeshRenderer>(); if (br != null) list.Add(br);
            var gr = glowGo.GetComponent<MeshRenderer>(); if (gr != null) list.Add(gr);
            var er = edgeGo.GetComponent<MeshRenderer>(); if (er != null) list.Add(er);
            _panels[Key(side, lane)] = list;
        }

        /// <summary>贴地亮线网格(碗沿最下段,几乎垂直)。</summary>
        static Mesh BuildBottomLine(Vector2 center, float a0, float a1)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            for (int i = 0; i <= StepsPerPanel; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)StepsPerPanel);
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float fu = i / (float)StepsPerPanel;
                const float outPush = 0.006f;                        // 略外偏,防与膜面 z-fight
                for (int r = 0; r <= 1; r++)
                {
                    float y = r == 0 ? 0f : BottomLineH;
                    float rad = WallSystem.Radius - LeanIn * Mathf.Pow(y / Height, LeanPow) + outPush;
                    v.Add(new Vector3(center.x + ca * rad, y, center.y - sa * rad));
                    uv.Add(new Vector2(fu, r));
                }
            }
            for (int i = 0; i < StepsPerPanel; i++)
            {
                int b = i * 2;
                t.Add(b); t.Add(b + 1); t.Add(b + 3);
                t.Add(b); t.Add(b + 3); t.Add(b + 2);
                t.Add(b); t.Add(b + 3); t.Add(b + 1);
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
            return ViewAssets.CreateMesh(new List<Vector3>[] { v }, new List<int>[] { t },
                new List<Vector2>[] { uv });
        }

        static Color EdgeColor(Color tint)
        {
            return new Color(Mathf.Clamp01(tint.r * 0.45f + 0.55f),
                Mathf.Clamp01(tint.g * 0.45f + 0.55f), Mathf.Clamp01(tint.b * 0.45f + 0.55f), 0.85f);
        }

        // ---- 渐变贴图(运行时生成,免贴图资产;坡度形状即两条常量) ----

        private static readonly Dictionary<int, Texture2D> _grads = new Dictionary<int, Texture2D>();

        /// <summary>竖向渐变贴图。**关键**:渐隐量放 alpha 还是放 RGB,取决于混合方式——
        /// 加色混合(One/One)只取 `贴图RGB × _BaseColor`,**贴图 alpha 不参与**,
        /// 所以加色层的渐隐必须做进 RGB;Alpha 混合层才用 alpha。
        /// (2026-09-11 收尾实测:把渐隐只做在 alpha 上 → 辉光层变成整片均匀加色,
        ///  俯视读成"实心青色扇面"、且三个渐隐旋钮全部失灵。)
        /// uv.y=0(贴地)最实,向上按幂次衰减到 0。</summary>
        static Texture2D GradTex(bool rgbRamp, float power, float amount)
        {
            int id = (rgbRamp ? 1 : 0) * 100000000 + Mathf.RoundToInt(power * 100f) * 1000 + Mathf.RoundToInt(amount * 100f);
            Texture2D cached;
            if (_grads.TryGetValue(id, out cached)) return cached;
            const int H = 256;
            var tex = new Texture2D(4, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < H; y++)
            {
                float f = y / (float)(H - 1);                        // 0=贴地, 1=顶
                float v = Mathf.Pow(1f - f, power) * amount;
                Color c = rgbRamp ? new Color(v, v, v, 1f) : new Color(1f, 1f, 1f, v);
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
            }
            tex.Apply();
            _grads[id] = tex;
            return tex;
        }

        // ---- 材质 ----

        /// <summary>Alpha 混合 Unlit(膜面/底边):色走 _BaseColor,alpha 走贴图 × _BaseColor.a。</summary>
        static Material AlphaMat(Color c, Texture2D tex, int queue)
        {
            var m = new Material(ViewAssets.FindShader("Universal Render Pipeline/Unlit"));
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);   // 透明 + Alpha
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = queue;
            return m;
        }

        /// <summary>加色自发光(辉光层)。注意:加色在亮背景上会被推向过曝,故必须与膜面层配合。</summary>
        static Material AdditiveMat(Color c, Texture2D tex, int queue)
        {
            var m = new Material(ViewAssets.FindShader("Universal Render Pipeline/Unlit"));
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1f));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);   // 透明 + 叠加
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = queue;
            return m;
        }
    }
}
