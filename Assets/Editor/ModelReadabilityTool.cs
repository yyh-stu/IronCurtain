using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 模型可读性工具(2026-09-14 制作人确认修):
    /// 视图层"按**顶点最低点**贴地"(`EntityView.Building.LowestPointLocalY` 等)需要读 `mesh.vertices`;
    /// 但部分模型导入时 `Read/Write` 没开 → Unity 只报一句警告
    /// (`Not allowed to access vertices on mesh 'node_0.001' (isReadable is false)`)并返回空数组,
    /// 代码只能退化到"包围盒最低点"兜底 → 个别单位/建筑会站不准。
    ///
    /// 这里**只对运行时真正加载的那棵目录**(`Assets/Resources/Models/`)开启 Read/Write ——
    /// 其余 Kenney 素材(500+ 个 fbx)不加载、也不占构建体积,不动它们。
    ///
    /// 用法:`Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.ModelReadabilityTool.EnableReadable -quit`
    /// </summary>
    public static class ModelReadabilityTool
    {
        private const string Root = "Assets/Resources/Models";

        public static void EnableReadable()
        {
            var files = new List<string>();
            foreach (var f in Directory.GetFiles(Root, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".fbx" || ext == ".obj" || ext == ".blend" || ext == ".dae") files.Add(f.Replace('\\', '/'));
            }
            int changed = 0, already = 0, failed = 0;
            foreach (var path in files)
            {
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) { failed++; continue; }
                if (imp.isReadable) { already++; continue; }
                imp.isReadable = true;
                imp.SaveAndReimport();
                changed++;
                Debug.Log("[model] 开启 Read/Write: " + path);
            }
            AssetDatabase.Refresh();
            Debug.Log("[model] 扫描 " + files.Count + " 个模型:新开启 " + changed + "、本就可读 " + already
                + "、非模型导入器 " + failed);
        }

        /// <summary>
        /// 回滚:把运行时目录下模型的 Read/Write 关掉。
        /// 为什么默认关着(2026-09-14 实测):开着会把每个网格的**可读副本**打进包 —— 实测构建 399MB → **477MB(+78MB)**,
        /// 而 `Verify` 量出这些模型的"顶点最低点 vs 包围盒最低点"**差值为 0**(方块风格资产包围盒本来就贴紧顶点)
        /// → 观感收益为零。所以改为:代码侧"读不到就不去读"(见 `EntityView.Building.LowestPointLocalY`),需要时再一键开启。
        /// </summary>
        public static void DisableReadable()
        {
            var files = new List<string>();
            foreach (var f in Directory.GetFiles(Root, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".fbx" || ext == ".obj" || ext == ".blend" || ext == ".dae") files.Add(f.Replace('\\', '/'));
            }
            int changed = 0;
            foreach (var path in files)
            {
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null || !imp.isReadable) continue;
                imp.isReadable = false;
                imp.SaveAndReimport();
                changed++;
            }
            AssetDatabase.Refresh();
            Debug.Log("[model] 已回滚 Read/Write:" + changed + " 个(构建体积回到原状;需要顶点级贴地时再跑 EnableReadable)");
        }

        /// <summary>复核:运行时目录下的模型是否都已可读(并顺带打印最低顶点所在高度,便于对照贴地)。</summary>
        public static void Verify()
        {
            var files = new List<string>();
            foreach (var f in Directory.GetFiles(Root, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".fbx" || ext == ".obj" || ext == ".blend" || ext == ".dae") files.Add(f.Replace('\\', '/'));
            }
            int bad = 0;
            foreach (var path in files)
            {
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null || imp.isReadable) continue;
                bad++;
                Debug.LogWarning("[model] 仍不可读: " + path);
            }
            // 抽两个样本读顶点,证明"读得到"
            foreach (var sample in new[] { "Assets/Resources/Models/Heroes/DuanFeng.fbx", "Assets/Resources/Models/Buildings/箭塔.fbx" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(sample);
                if (go == null) { Debug.Log("[model] 样本缺失(名字可能不同): " + sample); continue; }
                float lowest = float.MaxValue; string meshName = "无网格";
                var meshes = new List<Mesh>();
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) meshes.Add(mf.sharedMesh);
                // 英雄是蒙皮网格(SkinnedMeshRenderer),不是 MeshFilter —— 两路都要查
                foreach (var sm in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (sm.sharedMesh != null) meshes.Add(sm.sharedMesh);
                foreach (var m in meshes)
                {
                    meshName = m.name;
                    if (!m.isReadable) { Debug.LogWarning("[model] " + sample + " 的网格 " + m.name + " 仍不可读"); continue; }
                    foreach (var v in m.vertices) if (v.y < lowest) lowest = v.y;
                }
                Debug.Log("[model] " + sample + " 网格=" + meshName + " 最低顶点 y=" + (lowest == float.MaxValue ? "n/a" : lowest.ToString("F4"))
                    + " 可读=" + (lowest != float.MaxValue ? "是" : "否"));
            }
            Debug.Log("[model] 复核完成:不可读模型数 = " + bad + "(期望 0)");

            // 量化这次修的影响:逐模型比较"顶点最低点贴地"与"包围盒最低点贴地"的差值 ——
            // 读不到顶点时旧代码走的就是包围盒,差值大 = 之前站得明显不准。
            Debug.Log("[model] ===== 贴地口径差值(顶点 vs 包围盒,>0.02m 才算明显) =====");
            int notable = 0;
            foreach (var path in files)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var meshes = new List<Mesh>();
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) meshes.Add(mf.sharedMesh);
                foreach (var sm in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (sm.sharedMesh != null) meshes.Add(sm.sharedMesh);
                if (meshes.Count == 0) continue;
                float vb = float.MaxValue, bb = float.MaxValue;
                foreach (var m in meshes)
                {
                    if (!m.isReadable) continue;
                    foreach (var v in m.vertices) if (v.y < vb) vb = v.y;
                    if (m.bounds.min.y < bb) bb = m.bounds.min.y;
                }
                if (vb == float.MaxValue || bb == float.MaxValue) continue;
                float d = System.Math.Abs(vb - bb);
                if (d > 0.02f) { notable++; Debug.Log(string.Format("[model] {0,-52} 顶点={1,7:F4} 包围盒={2,7:F4} 差={3:F4}m",
                    Path.GetFileName(path), vb, bb, d)); }
            }
            Debug.Log("[model] 贴地口径差异明显的模型数 = " + notable + "(这些模型在本次修复前是站不准的)");
        }
    }
}
