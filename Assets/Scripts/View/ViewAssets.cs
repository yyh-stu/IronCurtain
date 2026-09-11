using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public static class ViewAssets
    {
        private static Font _font;
        private static Mesh _quadMesh;

        public static Material Mat(Color albedo, float metallic, float smoothness, Color emission, float emissionIntensity)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", albedo);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (emissionIntensity > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission * emissionIntensity);
            }
            return m;
        }

        // 2026-09-10 制作人目击"血条随摄像头变动出灰色杂质"根因=UI 材质受光照(Lit);
        // UI 元素(血条/光环/刻度)一律 Unlit 纯色,不受场景光影响
        public static Material Unlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        public static Material Flat(Color albedo, float smoothness = 0.4f)
        {
            return Mat(albedo, 0f, smoothness, Color.black, 0f);
        }

        public static Material TeamMat(Side side)
        {
            switch (side)
            {
                case Side.Blue: return Mat(new Color(0.15f, 0.20f, 0.28f), 0.55f, 0.6f, new Color(0.2f, 0.85f, 1f), 1.2f);
                case Side.Red: return Mat(new Color(0.28f, 0.15f, 0.15f), 0.55f, 0.6f, new Color(1f, 0.3f, 0.3f), 1.2f);
                default: return Mat(new Color(0.3f, 0.3f, 0.3f), 0.55f, 0.6f, new Color(0.85f, 0.85f, 0.85f), 0.8f);
            }
        }

        public static Color TeamColor(Side side)
        {
            switch (side)
            {
                case Side.Blue: return new Color(0.2f, 0.85f, 1f);
                case Side.Red: return new Color(1f, 0.3f, 0.3f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        public static Material GlowMat(Side side)
        {
            return Mat(new Color(0.02f, 0.02f, 0.02f), 0f, 0.9f, TeamColor(side), 2.5f);
        }

        public static Font UiFont(int size)
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _font;
        }

        // ---------- 自建网格构建 ----------
        private static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        public static void AddBox(List<Vector3> v, List<int> t, Vector3 c, Vector3 s)
        {
            float x = s.x * 0.5f, y = s.y * 0.5f, z = s.z * 0.5f;
            Quad(v, t, c + new Vector3(-x, -y, z), c + new Vector3(x, -y, z), c + new Vector3(x, y, z), c + new Vector3(-x, y, z));
            Quad(v, t, c + new Vector3(x, -y, -z), c + new Vector3(-x, -y, -z), c + new Vector3(-x, y, -z), c + new Vector3(x, y, -z));
            Quad(v, t, c + new Vector3(x, -y, z), c + new Vector3(x, -y, -z), c + new Vector3(x, y, -z), c + new Vector3(x, y, z));
            Quad(v, t, c + new Vector3(-x, -y, -z), c + new Vector3(-x, -y, z), c + new Vector3(-x, y, z), c + new Vector3(-x, y, -z));
            Quad(v, t, c + new Vector3(-x, y, z), c + new Vector3(x, y, z), c + new Vector3(x, y, -z), c + new Vector3(-x, y, -z));
        }

        /// <summary>
        /// 单格顶面 + **显式 UV**(ART-P0 铺路用)。自动 UV 是按整块网格包围盒归一化的,
        /// 一整片地面会只剩一张贴图被拉伸;地面必须逐格给 UV 才能按模块拼贴。
        /// 绕序与 AddBox 的顶面一致(法线朝上),故 uv 的 v 轴对应世界 −z。
        /// </summary>
        public static void AddTopQuad(List<Vector3> v, List<int> t, List<Vector2> uv,
            float cx, float cz, float y, float size, float u0, float v0, float u1, float v1)
        {
            float h = size * 0.5f;
            int i0 = v.Count;
            v.Add(new Vector3(cx - h, y, cz + h)); uv.Add(new Vector2(u0, v0));
            v.Add(new Vector3(cx + h, y, cz + h)); uv.Add(new Vector2(u1, v0));
            v.Add(new Vector3(cx + h, y, cz - h)); uv.Add(new Vector2(u1, v1));
            v.Add(new Vector3(cx - h, y, cz - h)); uv.Add(new Vector2(u0, v1));
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
        }

        /// <summary>带显式 UV 的网格(逐 submesh 对齐顶点序)。</summary>
        public static Mesh CreateMesh(List<Vector3>[] verts, List<int>[] tris, List<Vector2>[] uvs)
        {
            var m = CreateMesh(verts, tris);
            var all = new List<Vector2>();
            for (int s = 0; s < uvs.Length; s++) all.AddRange(uvs[s]);
            if (all.Count == m.vertexCount) m.SetUVs(0, all);
            return m;
        }

        /// <summary>贴图材质(URP/Lit);tex 为空时退化为纯色。</summary>
        public static Material Textured(Texture2D tex, Color tint, float smoothness)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            return m;
        }

        // 底面中心 c,向上 h;botHalf/topHalf 为 x/z 半宽;seg 边数;angOff 起始角偏移
        public static void AddPrism(List<Vector3> v, List<int> t, Vector3 c, Vector2 botHalf, Vector2 topHalf, float h, int seg, float angOff = 0f)
        {
            float step = Mathf.PI * 2f / seg;
            for (int i = 0; i < seg; i++)
            {
                float a0 = angOff + step * i, a1 = angOff + step * (i + 1);
                var b0 = new Vector3(c.x + Mathf.Cos(a0) * botHalf.x, c.y, c.z + Mathf.Sin(a0) * botHalf.y);
                var b1 = new Vector3(c.x + Mathf.Cos(a1) * botHalf.x, c.y, c.z + Mathf.Sin(a1) * botHalf.y);
                var t0 = new Vector3(c.x + Mathf.Cos(a0) * topHalf.x, c.y + h, c.z + Mathf.Sin(a0) * topHalf.y);
                var t1 = new Vector3(c.x + Mathf.Cos(a1) * topHalf.x, c.y + h, c.z + Mathf.Sin(a1) * topHalf.y);
                Quad(v, t, b0, b1, t1, t0);
                int tc = v.Count;
                v.Add(new Vector3(c.x, c.y + h, c.z));
                t.Add(tc); t.Add(tc + 1); t.Add(tc + 2);
                v.Add(t0); v.Add(t1);
                t.Add(tc); t.Add(tc + 3); t.Add(tc + 4);
                int bc = v.Count;
                v.Add(new Vector3(c.x, c.y, c.z));
                t.Add(bc); t.Add(bc + 2); t.Add(bc + 1);
                v.Add(b0); v.Add(b1);
                t.Add(bc); t.Add(bc + 4); t.Add(bc + 3);
            }
        }

        public static void AddCylinder(List<Vector3> v, List<int> t, Vector3 c, float rBot, float rTop, float h, int seg, float angOff = 0f)
        {
            AddPrism(v, t, c, new Vector2(rBot, rBot), new Vector2(rTop, rTop), h, seg, angOff);
        }

        public static void AddRing(List<Vector3> v, List<int> t, Vector3 c, float radius, float width, int seg)
        {
            float step = Mathf.PI * 2f / seg, ri = radius - width, ro = radius + width;
            for (int i = 0; i < seg; i++)
            {
                float a0 = step * i, a1 = step * (i + 1);
                Quad(v, t,
                    new Vector3(c.x + Mathf.Cos(a0) * ri, c.y, c.z + Mathf.Sin(a0) * ri),
                    new Vector3(c.x + Mathf.Cos(a1) * ri, c.y, c.z + Mathf.Sin(a1) * ri),
                    new Vector3(c.x + Mathf.Cos(a1) * ro, c.y, c.z + Mathf.Sin(a1) * ro),
                    new Vector3(c.x + Mathf.Cos(a0) * ro, c.y, c.z + Mathf.Sin(a0) * ro));
            }
        }

        public static Mesh CreateMesh(List<Vector3>[] verts, List<int>[] tris)
        {
            var m = new Mesh();
            // 2026-09-11 铺路:边界子格细分后单网格顶点数会突破 65535(16 位索引上限),
            // 超限会静默截断 → 整片地面消失。必须开 32 位索引。
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var all = new List<Vector3>();
            for (int s = 0; s < verts.Length; s++) all.AddRange(verts[s]);
            m.SetVertices(all);
            m.subMeshCount = verts.Length;
            for (int s = 0; s < verts.Length; s++)
            {
                int off = 0;
                for (int p = 0; p < s; p++) off += verts[p].Count;
                var idx = new List<int>(tris[s].Count);
                for (int i = 0; i < tris[s].Count; i++) idx.Add(tris[s][i] + off);
                m.SetTriangles(idx, s);
            }
            m.RecalculateNormals();
            // 2026-09-10 血条贴图化:补平面 UV(取顶点 xy 归一)——对血条 quad 精确,对 box 粗略无碍
            if (m.uv == null || m.uv.Length == 0)
            {
                var uvs = new Vector2[all.Count];
                if (all.Count > 0)
                {
                    float minX = all[0].x, maxX = all[0].x, minY = all[0].y, maxY = all[0].y;
                    foreach (var v3 in all) { minX = Mathf.Min(minX, v3.x); maxX = Mathf.Max(maxX, v3.x); minY = Mathf.Min(minY, v3.y); maxY = Mathf.Max(maxY, v3.y); }
                    float sx = maxX - minX, sy = maxY - minY;
                    for (int i2 = 0; i2 < all.Count; i2++)
                        uvs[i2] = sx > 0.0001f && sy > 0.0001f
                            ? new Vector2((all[i2].x - minX) / sx, (all[i2].y - minY) / sy)
                            : new Vector2(0.5f, 0.5f);
                }
                m.SetUVs(0, new List<Vector2>(uvs));
            }
            return m;
        }

        public static GameObject AttachMesh(string name, Transform parent, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        // 极简小装饰(血条/驻军光点等),单 submesh 小网格
        public static GameObject DecoBox(Material mat, Vector3 lpos, Vector3 size, Transform parent, string name = "deco")
        {
            var bv = new List<Vector3>(); var bt = new List<int>();
            AddBox(bv, bt, Vector3.zero, size);
            var mesh = CreateMesh(new List<Vector3>[] { bv }, new List<int>[] { bt });
            var go = AttachMesh(name, parent, mesh, new Material[] { mat });
            go.transform.localPosition = lpos;
            return go;
        }

        // 真圆盘(俯视正圆;2026-09-10 修"方形光环"观感 bug,替代 DecoBox 冒充的圆盘)
        private static Mesh _discMesh;
        public static GameObject DecoDisc(Material mat, Vector3 lpos, float diameter, Transform parent, string name = "decoDisc")
        {
            if (_discMesh == null)
            {
                var v = new List<Vector3>(); var t = new List<int>();
                const int seg = 24;
                const float y = 0f;
                v.Add(new Vector3(0f, y, 0f)); // 中心
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * 0.5f, y, Mathf.Sin(a) * 0.5f));
                }
                for (int i = 1; i <= seg; i++)
                {
                    t.Add(0); t.Add(i + 1); t.Add(i); // 单面朝上
                }
                _discMesh = CreateMesh(new List<Vector3>[] { v }, new List<int>[] { t });
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lpos;
            go.transform.localScale = new Vector3(diameter, 1f, diameter);
            go.AddComponent<MeshFilter>().sharedMesh = _discMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // 贴图化平面(2026-09-10 血条质感升级:木框+渐变贴图条体)
        public static GameObject DecoTextured(Material mat, Vector3 lpos, Vector2 size, Transform parent, string name = "decoTex")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lpos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static Mesh QuadMesh()
        {
            if (_texQuad == null)
            {
                var v = new List<Vector3>(); var t = new List<int>();
                Quad(v, t, new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0));
                _texQuad = CreateMesh(new List<Vector3>[] { v }, new List<int>[] { t });
            }
            return _texQuad;
        }
        private static Mesh _texQuad;

        // 圆环(平躺;2026-09-10 王者荣耀式阵营环光环)
        public static GameObject DecoRing(Material mat, Vector3 lpos, float radius, float width, Transform parent, string name = "decoRing")
        {
            var v = new List<Vector3>(); var t = new List<int>();
            AddRing(v, t, Vector3.zero, radius * 0.5f, width * 0.5f, 32);
            var mesh = CreateMesh(new List<Vector3>[] { v }, new List<int>[] { t });
            var go = AttachMesh(name, parent, mesh, new Material[] { mat });
            go.transform.localPosition = lpos;
            return go;
        }

        public static GameObject DecoQuad(Material mat, Vector3 lpos, Vector2 size, Transform parent, string name = "decoQ")
        {
            if (_quadMesh == null)
            {
                var v = new List<Vector3>(); var t = new List<int>();
                Quad(v, t, new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0));
                _quadMesh = CreateMesh(new List<Vector3>[] { v }, new List<int>[] { t });
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lpos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = _quadMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
