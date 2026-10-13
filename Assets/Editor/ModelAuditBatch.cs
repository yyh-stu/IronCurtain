using System.Collections.Generic;
using System.IO;
using System.Text;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 2026-09-14 新增:六英雄**模型几何**审计。制作人两次确认"不管什么将领生产出来都是磐石",
    /// 而属性链探针证明 Entity.Hero 与视图层级名都是对的 —— 那就必须回答最后一个问题:
    /// **六个 fbx 里的网格本身是不是同一个?**(名字/路径对、几何相同 = 一眼看全是磐石)
    /// 判据不看名字,看顶点数/包围盒/顶点位置哈希 —— 名字可以骗人,几何不会。
    ///
    /// 用法:Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.ModelAuditBatch.Run -logFile &lt;log&gt;
    /// </summary>
    public static class ModelAuditBatch
    {
        private static readonly HeroId[] All =
        {
            HeroId.PanShi, HeroId.ZhenYue, HeroId.DuanFeng,
            HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo
        };

        public static void Run()
        {
            var report = new StringBuilder();
            report.AppendLine("[modelaudit] 开始 六英雄模型几何审计");

            var sigs = new Dictionary<string, string>();
            foreach (var h in All)
            {
                string path = "Models/Heroes/" + h;
                var prefab = Resources.Load<GameObject>(path);
                if (prefab == null)
                {
                    report.AppendLine("[modelaudit] " + h + " 加载失败 path=" + path);
                    continue;
                }

                var go = Object.Instantiate(prefab);
                var meshes = new List<Mesh>();
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr.sharedMesh != null) meshes.Add(smr.sharedMesh);
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) meshes.Add(mf.sharedMesh);

                var mats = new List<string>();
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null) mats.Add(m.name);

                int totalVerts = 0;
                var hash = new Fnv();
                var bounds = new Bounds();
                bool first = true;
                foreach (var m in meshes)
                {
                    totalVerts += m.vertexCount;
                    var vs = m.vertices;
                    for (int i = 0; i < vs.Length; i++)
                    {
                        hash.Add(Mathf.RoundToInt(vs[i].x * 1000f));
                        hash.Add(Mathf.RoundToInt(vs[i].y * 1000f));
                        hash.Add(Mathf.RoundToInt(vs[i].z * 1000f));
                    }
                    var b = m.bounds;
                    if (first) { bounds = b; first = false; }
                    else bounds.Encapsulate(b);
                }

                string sig = "verts=" + totalVerts + " boundH=" + bounds.size.y.ToString("F2")
                    + " boundW=" + bounds.size.x.ToString("F2") + " hash=" + hash.Value.ToString("X8");
                sigs[h.ToString()] = sig;
                report.AppendLine("[modelaudit] " + h + " path=" + path
                    + " meshes=" + meshes.Count
                    + " " + sig
                    + " mats=[" + string.Join(",", mats.ToArray()) + "]");

                // 逐网格明细(哪个网格撞了,一眼看出)
                foreach (var m in meshes)
                    report.AppendLine("[modelaudit]    mesh " + m.name + " v=" + m.vertexCount
                        + " h=" + MeshHash(m).ToString("X8"));

                Object.DestroyImmediate(go);
            }

            // 交叉比较:任意两个英雄几何完全相同 = 实锤"同一个模型"
            report.AppendLine("[modelaudit] ---- 交叉比较 ----");
            bool dup = false;
            for (int i = 0; i < All.Length; i++)
            {
                for (int j = i + 1; j < All.Length; j++)
                {
                    string a = All[i].ToString(), b = All[j].ToString();
                    if (!sigs.ContainsKey(a) || !sigs.ContainsKey(b)) continue;
                    if (sigs[a] == sigs[b])
                    {
                        dup = true;
                        report.AppendLine("[modelaudit] !! 完全相同: " + a + " == " + b);
                    }
                }
            }
            if (!dup) report.AppendLine("[modelaudit] 六英雄几何两两不同(模型层无问题)");

            report.AppendLine("[modelaudit] done");
            File.WriteAllText("D:/DevTools/shots/modelaudit.txt", report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static uint MeshHash(Mesh m)
        {
            var h = new Fnv();
            var vs = m.vertices;
            for (int i = 0; i < vs.Length; i++)
            {
                h.Add(Mathf.RoundToInt(vs[i].x * 1000f));
                h.Add(Mathf.RoundToInt(vs[i].y * 1000f));
                h.Add(Mathf.RoundToInt(vs[i].z * 1000f));
            }
            return h.Value;
        }

        private struct Fnv
        {
            public uint Value;
            private bool _init;
            public void Add(int v)
            {
                if (!_init) { Value = 2166136261u; _init = true; }
                Value = (Value ^ (uint)v) * 16777619u;
            }
        }
    }
}
