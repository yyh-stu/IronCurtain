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

            BuildVoid(t);
            BuildGroundChunks(t);
            BuildWater(t);
            BuildBridges(t);
            BuildEnvironment(t);
        }

        private static void BuildVoid(Transform t)
        {
            Material voidMat = ViewAssets.Flat(new Color32(0x0a, 0x0c, 0x12, 0xff), 0.05f);
            ViewAssets.DecoBox(voidMat, new Vector3(143f, -0.5f, -12f), new Vector3(420f, 0.4f, 160f), t, "Void");
        }

        private static void BuildGroundChunks(Transform t)
        {
            Material stoneDark = ViewAssets.Flat(new Color32(0x78, 0x7a, 0x7e, 0xff), 0.35f);
            Material stoneLight = ViewAssets.Flat(new Color32(0x86, 0x88, 0x8d, 0xff), 0.35f);
            Material grass = ViewAssets.Flat(new Color32(0x5f, 0x81, 0x49, 0xff), 0.12f);
            var mats = new[] { stoneDark, stoneLight, grass };

            for (int c = 0; c < 5; c++)
            {
                int colMin = c * 60;
                int colMax = Mathf.Min(colMin + 60, TerrainMap.Cols) - 1;
                var dv = new List<Vector3>(); var dt = new List<int>();
                var lv = new List<Vector3>(); var lt = new List<int>();
                var gv = new List<Vector3>(); var gt = new List<int>();
                for (int row = 0; row < TerrainMap.Rows; row++)
                {
                    for (int col = colMin; col <= colMax; col++)
                    {
                        var v = new SimVec2(col + 0.5f, row + 0.5f);
                        if (!TerrainMap.IsInside(v) || TerrainMap.IsWater(v) || TerrainMap.IsBridge(v)) continue;
                        var center = new Vector3(col + 0.5f, -0.05f, 3f - row - 0.5f);
                        var size = new Vector3(1f, 0.1f, 1f);
                        uint h = unchecked((uint)(row * 374761393 + col * 668265263));
                        h = (h ^ (h >> 13)) * 1274126177u;
                        h ^= h >> 16;
                        bool isGrass = TerrainMap.IsIsland(v) || h < 0x26666666u; // 约 15% 散点草地;中央岛强制草地
                        if (isGrass) ViewAssets.AddBox(gv, gt, center, size);
                        else if (((h >> 17) & 1) == 0) ViewAssets.AddBox(dv, dt, center, size);
                        else ViewAssets.AddBox(lv, lt, center, size);
                    }
                }
                var mesh = ViewAssets.CreateMesh(new List<Vector3>[] { dv, lv, gv }, new List<int>[] { dt, lt, gt });
                ViewAssets.AttachMesh("Ground_" + c, t, mesh, mats);
            }
        }

        private static void BuildWater(Transform t)
        {
            var wv = new List<Vector3>(); var wt = new List<int>();
            for (int row = 0; row < TerrainMap.Rows; row++)
                for (int col = 0; col < TerrainMap.Cols; col++)
                {
                    var v = new SimVec2(col + 0.5f, row + 0.5f);
                    if (!TerrainMap.IsWater(v)) continue;
                    ViewAssets.AddBox(wv, wt, new Vector3(col + 0.5f, -0.17f, 3f - row - 0.5f), new Vector3(1f, 0.1f, 1f));
                }
            var mesh = ViewAssets.CreateMesh(new List<Vector3>[] { wv }, new List<int>[] { wt });
            ViewAssets.AttachMesh("River", t, mesh,
                new Material[] { ViewAssets.Mat(new Color32(0x3f, 0x8f, 0xd6, 0xff), 0f, 0.6f, new Color(0.05f, 0.14f, 0.28f), 0.5f) });
        }

        private static void BuildBridges(Transform t)
        {
            var bv = new List<Vector3>(); var bt = new List<int>();
            for (int row = 0; row < TerrainMap.Rows; row++)
                for (int col = 0; col < TerrainMap.Cols; col++)
                {
                    var v = new SimVec2(col + 0.5f, row + 0.5f);
                    if (!TerrainMap.IsBridge(v)) continue;
                    ViewAssets.AddBox(bv, bt, new Vector3(col + 0.5f, 0.01f, 3f - row - 0.5f), new Vector3(1f, 0.1f, 1f));
                }
            var mesh = ViewAssets.CreateMesh(new List<Vector3>[] { bv }, new List<int>[] { bt });
            ViewAssets.AttachMesh("Bridges", t, mesh,
                new Material[] { ViewAssets.Flat(new Color32(0x9a, 0x7a, 0x52, 0xff), 0.3f) });

            Material railMat = ViewAssets.Flat(new Color32(0x6f, 0x56, 0x38, 0xff), 0.4f);
            // 两座桥:桥面 x ∈ [138,148),长 10;两侧栏杆截面 0.12×0.5,y 0.35
            AddBridgeRails(railMat, t, 0.5f);  // 下桥 Y∈[0,5),桥面中心 z=0.5
            AddBridgeRails(railMat, t, -24.5f); // 上桥 Y∈[25,30)
        }

        private static void AddBridgeRails(Material m, Transform t, float centerZ)
        {
            ViewAssets.DecoBox(m, new Vector3(143f, 0.35f, centerZ + 2.5f), new Vector3(10f, 0.5f, 0.12f), t, "BridgeRail_N");
            ViewAssets.DecoBox(m, new Vector3(143f, 0.35f, centerZ - 2.5f), new Vector3(10f, 0.5f, 0.12f), t, "BridgeRail_S");
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
                if (TerrainMap.IsWater(v) || TerrainMap.IsBridge(v) || TerrainMap.IsIsland(v)) continue;
                bool near = false;
                foreach (int bc in BuildingCols)
                    if (Mathf.Abs(col - bc) < 5 && Mathf.Abs(row - 15) < 5) { near = true; break; }
                if (near) continue;
                pos = new Vector3(col + 0.5f, 0f, 3f - row - 0.5f);
                return true;
            }
            return false;
        }
    }
}
