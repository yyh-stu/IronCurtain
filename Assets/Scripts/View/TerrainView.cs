using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>M6.6 椭圆大地图地形渲染:石路+草地/河/桥/虚空 + 环境撒布。</summary>
    public static class TerrainView
    {
        private static readonly int[] BuildingCols = { 15, 46, 77, 108, 143, 178, 209, 240, 271 };

        public static void Build(Transform parent)
        {
            var terrainGo = new GameObject("Terrain");
            terrainGo.transform.SetParent(parent, false);
            Transform t = terrainGo.transform;

            BuildSea(t);
            BuildGroundChunks(t);
            BuildPlazaDiscs(t);   // 主城广场=真圆网格(2026-09-11:圆角/干净弧线)
            WallView.Build(t);    // 城墙表现层(箭塔拼段+改色+折角烽火台)   // 石路/草地/沙地/河水/石桥一体成网(2026-09-11)
            BuildEnvironment(t);
        }

        /// <summary>地图外的海(2026-09-12 制作人:地图外全部变成有轻微海浪的海)。
        /// 旧实现在可玩区外铺 #0a0c12 黑底板 —— 对局里相机贴边或拉远时,屏幕两侧会出现上下贯穿的黑带
        /// (制作人目击的"透明黑框"),现改为两层水片错速滚动:远海深、近海浅,底下一层兜底防穿帮。</summary>
        private static void BuildSea(Transform t)
        {
            var seaGo = new GameObject("Sea");
            seaGo.transform.SetParent(t, false);
            float cx = LaneGrid.Cols * 0.5f, cz = (3f + (3f - LaneGrid.Rows)) * 0.5f;
            var far = GroundTiles.Mat(GroundTiles.KindWaterDeep);
            var near = GroundTiles.Mat(GroundTiles.KindWaterShallow);
            Material mFar = far != null ? new Material(far) : ViewAssets.Flat(new Color(0.20f, 0.44f, 0.66f));
            Material mNear = near != null ? new Material(near) : ViewAssets.Flat(new Color(0.30f, 0.58f, 0.78f));
            mFar.color = new Color(0.30f, 0.55f, 0.78f, 1f);    // 压青,别读成冰面(同河水口径)
            mNear.color = new Color(0.44f, 0.68f, 0.88f, 1f);
            // 1600×1600 足够大:任何拉远/平移都看不到边
            ViewAssets.DecoQuad(mFar, new Vector3(cx, -0.42f, cz), new Vector2(1600f, 1600f), seaGo.transform, "SeaFar");
            ViewAssets.DecoQuad(mNear, new Vector3(cx, -0.30f, cz), new Vector2(1600f, 1600f), seaGo.transform, "SeaNear");
            var fx = seaGo.AddComponent<SeaWaveFx>();
            fx.Far = mFar; fx.Near = mNear;
        }

        private static void BuildGroundChunks(Transform t)
        {
            // 2026-09-11 铺路 v2(制作人:"色块边界太清晰,不美观"):
            // 地表按 Sim 权威的 TerrainMap.SurfaceOf 决定;边界做**子格抖动过渡**——
            // 与邻格地表不同的格子拆成 2×2 子块(0.5 米),按确定哈希把一半子块换成邻格地表;
            // 距边界 2 米的格子换 1/4。1 米硬台阶因此被打散成约 2 米宽的过渡带。
            // 子网格序:0 Road / 1 Grass / 2 Sand / 3 Water / 4 Bridge(水/桥并入同网格,便于互相抖动)。
            // 材质槽 0-4:五种地表;5 起:每对地表的 50/50 过渡材质(边界带用)
            // !!! 槽位必须按"用途"取对应的 Kind,不能用 Mat(i) 顺推 ——
            // Kind 序列是 Stone/StoneB/Grass/GrassB/Sand/Water/WaterDeep/Bridge,
            // 槽位是 Road/Grass/Sand/Water/Bridge,顺推会把草地渲染成深灰石
            // (2026-09-11 实测:整片草地变灰,制作人反馈"草地怎么缩水这么多"的根因之一)。
            var mats = new List<Material>
            {
                GroundTiles.Mat(GroundTiles.KindStone),          // 0 Road
                GroundTiles.Mat(GroundTiles.KindGrass),          // 1 Grass
                GroundTiles.Mat(GroundTiles.KindSand),           // 2 Sand
                GroundTiles.Mat(GroundTiles.KindWaterShallow),   // 3 Water
                GroundTiles.Mat(GroundTiles.KindBridge)          // 4 Bridge
            };
            var pairSlot = new int[5, 5];
            for (int a = 0; a < 5; a++)
                for (int b = a + 1; b < 5; b++)
                {
                    pairSlot[a, b] = pairSlot[b, a] = mats.Count;
                    mats.Add(GroundTiles.Blend(a, b));
                }
            int slots = mats.Count;
            const int Sub = 4;   // 边界格子格细分数(0.25 米台阶)
            float sub = 1f / Sub, uvStep = (1f / GroundTiles.Module) / Sub;


            int chunk = Mathf.CeilToInt(TerrainMap.Cols / 12f);   // 分块:细格后单块顶点数要压住
            for (int c = 0; c * chunk < TerrainMap.Cols; c++)
            {
                int colMin = c * chunk;
                int colMax = Mathf.Min(colMin + chunk, TerrainMap.Cols) - 1;
                var vs = new List<Vector3>[slots]; var ts = new List<int>[slots]; var us = new List<Vector2>[slots];
                for (int i = 0; i < slots; i++) { vs[i] = new List<Vector3>(); ts[i] = new List<int>(); us[i] = new List<Vector2>(); }

                for (int row = 0; row < TerrainMap.Rows; row++)
                {
                    for (int col = colMin; col <= colMax; col++)
                    {
                        var v = new SimVec2(col + 0.5f, row + 0.5f);
                        if (!TerrainMap.IsInside(v)) continue;
                        var sk = TerrainMap.SurfaceOf(v);
                        int idx = IdxOf(sk);
                        float u0, v0, u1, v1;
                        GroundTiles.UvRect(row, col, out u0, out v0, out u1, out v1);

                        int diffDist = 0; int otherIdx = idx;
                        for (int dr = -2; dr <= 2; dr++)
                            for (int dc = -2; dc <= 2; dc++)
                            {
                                if (dr == 0 && dc == 0) continue;
                                int rr = row + dr, cc = col + dc;
                                if (rr < 0 || rr >= TerrainMap.Rows || cc < 0 || cc >= TerrainMap.Cols) continue;
                                var nv = new SimVec2(cc + 0.5f, rr + 0.5f);
                                if (!TerrainMap.IsInside(nv)) continue;
                                var nk = TerrainMap.SurfaceOf(nv);
                                if (nk == sk) continue;
                                int d = Mathf.Max(Mathf.Abs(dr), Mathf.Abs(dc));
                                if (diffDist == 0 || d < diffDist) { diffDist = d; otherIdx = IdxOf(nk); }
                            }

                        // 2026-09-11 v4(制作人:边界太清晰/有棱有角):
                        // 边界格用 N×N 子格细分,逐子格按 SurfaceOf 判定 → 1 米台阶降到 1/N 米,
                        // 圆角自然变圆;不引入透明/朝向问题(此前渐隐带方案两次翻车,已弃)。
                        if (diffDist == 0)
                        {
                            AddGroundQuad(vs, ts, us, idx, col + 0.5f, 3f - row - 0.5f, 1f, u0, v0, u1, v1, sk);
                            continue;
                        }
                        // 2026-09-11 制作人裁决:混色过渡带来"色块混乱",撤回,边界回到硬边(选①)。
                        // 圆角/渐隐改由"形状网格"解决(见 BuildPlazaDiscs)。
                        // 细分仅用于贴着圆盘/曲线边界的格子(广场外圈),让圆盘边缘咬合干净
                        if (diffDist <= 2)
                        {
                            for (int sy2 = 0; sy2 < Sub; sy2++)
                                for (int sx2 = 0; sx2 < Sub; sx2++)
                                {
                                    float px = col + (sx2 + 0.5f) * sub;
                                    float py = row + (sy2 + 0.5f) * sub;
                                    if (!TerrainMap.IsInside(new SimVec2(px, py))) continue;
                                    var subSk = TerrainMap.SurfaceOf(new SimVec2(px, py));
                                    AddGroundQuad(vs, ts, us, IdxOf(subSk),
                                        px, 3f - py, sub,
                                        u0 + sx2 * uvStep, v0 + sy2 * uvStep,
                                        u0 + sx2 * uvStep + uvStep, v0 + sy2 * uvStep + uvStep, subSk);
                                }
                        }
                        else
                            AddGroundQuad(vs, ts, us, idx, col + 0.5f, 3f - row - 0.5f, 1f, u0, v0, u1, v1, sk);
                    }
                }
                var mesh = ViewAssets.CreateMesh(vs, ts, us);
                ViewAssets.AttachMesh("Ground_" + c, t, mesh, mats.ToArray());
            }
        }

        /// <summary>铺一块朝上的四边形(自动判绕序:法线必须朝 +y,否则被背面剔除 —— 2026-09-11 踩过的坑)。</summary>
        static void AddQuadUp(List<Vector3> v, List<int> t, List<Vector2> uv,
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            var n = Vector3.Cross(p1 - p0, p2 - p0);
            int i0 = v.Count;
            if (n.y >= 0f)
            {
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                uv.Add(uv0); uv.Add(uv1); uv.Add(uv2); uv.Add(uv3);
            }
            else
            {
                v.Add(p0); v.Add(p3); v.Add(p2); v.Add(p1);
                uv.Add(uv0); uv.Add(uv3); uv.Add(uv2); uv.Add(uv1);
            }
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
        }

        /// <summary>单块地面;水面略低(y −0.12)、桥面略高(y 0.02),其余贴地。</summary>
        static int IdxOf(SurfaceKind sk)
        {
            switch (sk)
            {
                case SurfaceKind.Road: return 0;
                case SurfaceKind.Grass: return 1;
                case SurfaceKind.Sand: return 2;
                case SurfaceKind.Water: return 3;
                default: return 4;
            }
        }

        /// <summary>按指定材质槽落一块地面(过渡材质用)。</summary>
        static void AddGroundQuadAt(List<Vector3>[] vs, List<int>[] ts, List<Vector2>[] us, int slot,
            float cx, float cz, float size, float u0, float v0, float u1, float v1, SurfaceKind sk)
        {
            float y = sk == SurfaceKind.Water ? -0.12f : (sk == SurfaceKind.Bridge ? 0.02f : 0f);
            ViewAssets.AddTopQuad(vs[slot], ts[slot], us[slot], cx, cz, y, size, u0, v0, u1, v1);
        }

        static void AddGroundQuad(List<Vector3>[] vs, List<int>[] ts, List<Vector2>[] us, int idx,
            float cx, float cz, float size, float u0, float v0, float u1, float v1, SurfaceKind sk)
        {
            float y = sk == SurfaceKind.Water ? -0.12f : (sk == SurfaceKind.Bridge ? 0.02f : 0f);
            ViewAssets.AddTopQuad(vs[idx], ts[idx], us[idx], cx, cz, y, size, u0, v0, u1, v1);
        }

        /// <summary>
        /// 主城广场:以基地中心为圆心、半径 40 的真圆网格(2026-09-11 制作人:"边角做成圆形")。
        /// UV 与逐格地面同口径(世界坐标 ÷ 4 米模块),所以与周边格子纹理天然对齐、无接缝。
        /// 画在逐格地面之上 0.005 米,遮住方格子拼出的锯齿;广场格子本身仍在(不露底)。
        /// </summary>
        /// <summary>
        /// 世界坐标 → 与逐格地面一致的 4 米模块 UV(**不取模**,靠贴图自身 Repeat 平铺)。
        /// 取模会让相邻顶点 UV 跨整张贴图跳变 → 圆盘渲染成放射拉丝(2026-09-11 踩过)。
        /// </summary>
        static Vector2 UvOfWorld(float x, float z)
        {
            return new Vector2(x / GroundTiles.Module, (3f - z) / GroundTiles.Module);
        }

        private static void BuildPlazaDiscs(Transform t)
        {
            // 墙内整片石路 = 以基地中心为圆心、半径 = 城墙半径(20)的真圆盘(2026-09-11 制作人裁决)。
            // 真圆网格 + 世界 UV(未取模)平铺,与周边格子纹理对齐。
            const int Seg = 96;
            foreach (var side in new[] { Side.Blue, Side.Red })
            {
                var sc = TerrainMap.PlazaCenter(side);
                float R = TerrainMap.PlazaRadius;
                var c = new Vector2(sc.X, 3f - sc.Y);
                var v = new List<Vector3>(); var tr = new List<int>(); var uv = new List<Vector2>();
                v.Add(new Vector3(c.x, 0.005f, c.y)); uv.Add(UvOfWorld(c.x, c.y));
                for (int i = 0; i <= Seg; i++)
                {
                    float a = Mathf.PI * 2f * i / Seg;
                    float x = c.x + Mathf.Cos(a) * R, z = c.y + Mathf.Sin(a) * R;
                    // 基地在极限位时圆盘会伸出地图轮廓 → 按轮廓裁剪(贴合边缘的 D 形)
                    var q = new SimVec2(x, 3f - z);
                    if (!TerrainMap.IsInside(q))
                    {
                        var cl = TerrainMap.ClampToEllipse(q);
                        x = cl.X; z = 3f - cl.Y;
                    }
                    v.Add(new Vector3(x, 0.005f, z)); uv.Add(UvOfWorld(x, z));
                }
                for (int i = 1; i <= Seg; i++)
                {
                    var n = Vector3.Cross(v[i] - v[0], v[i + 1] - v[0]);
                    if (n.y >= 0f) { tr.Add(0); tr.Add(i); tr.Add(i + 1); }
                    else { tr.Add(0); tr.Add(i + 1); tr.Add(i); }
                }
                var mesh = ViewAssets.CreateMesh(new List<Vector3>[] { v }, new List<int>[] { tr },
                    new List<Vector2>[] { uv });
                ViewAssets.AttachMesh("Plaza_" + side, t, mesh,
                    new Material[] { GroundTiles.Mat(GroundTiles.KindStone) });
            }
        }

        private static void BuildEnvironment(Transform t)
        {
            var envGo = new GameObject("Environment");
            envGo.transform.SetParent(t, false);
            Transform env = envGo.transform;

            var items = new List<KeyValuePair<string, float>>(26);
            AddItems(items, "tree-large", 6, 4.4f);
            AddItems(items, "tree-small", 8, 2.2f);
            AddItems(items, "rocks-large", 4, 1.4f);
            AddItems(items, "rocks-small", 8, 0.7f);

            var rng = new System.Random(1234);
            bool warned = false;
            foreach (var it in items)
            {
                GameObject prefab = Resources.Load<GameObject>("Models/" + it.Key);
                if (prefab == null)
                {
                    if (!warned) { Debug.LogWarning("[TerrainView] Missing model: " + it.Key); warned = true; }
                    continue;
                }
                Vector3 pos;
                if (!FindFreeCell(rng, out pos)) continue;
                var go = Object.Instantiate(prefab, env);
                go.name = it.Key;
                var b = new Bounds(); bool found = false;
                foreach (var rr in go.GetComponentsInChildren<Renderer>())
                {
                    if (!found) { b = rr.bounds; found = true; }
                    else b.Encapsulate(rr.bounds);
                }
                if (found && b.size.y > 0.0001f) go.transform.localScale = Vector3.one * (it.Value / b.size.y);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            }
        }

        private static void AddItems(List<KeyValuePair<string, float>> list, string name, int count, float h)
        {
            for (int i = 0; i < count; i++) list.Add(new KeyValuePair<string, float>(name, h));
        }

        private static bool FindFreeCell(System.Random rng, out Vector3 pos)
        {
            pos = Vector3.zero;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                int row = rng.Next(TerrainMap.Rows);
                int col = rng.Next(TerrainMap.Cols);
                var v = new SimVec2(col + 0.5f, row + 0.5f);
                if (!TerrainMap.IsInside(v)) continue;
                // 只在草地上撒散件(不压石路/沙地/河/桥),并避开两侧主城广场
                if (TerrainMap.SurfaceOf(v) != SurfaceKind.Grass) continue;
                if (TerrainMap.InBasePlaza(v, Side.Blue) || TerrainMap.InBasePlaza(v, Side.Red)) continue;
                pos = new Vector3(col + 0.5f, 0f, 3f - row - 0.5f);
                return true;
            }
            return false;
        }
    }
}
