using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// VR02/VR03(2026-09-21 返工单 §3.4)骨骼取证:
// 对磐石/镇岳输出 ①含 hand/finger 语义的骨骼全名 ②Animator 人形映射命中情况
// ③SkinnedMeshRenderer.bones 规模 ④手部骨骼实际拿到蒙皮权重的顶点数。
// 结论决定 VR03 修法:有手指骨+权重 → 手姿态;没有 → 局部网格/最小补骨。
public static class DumpHandRig
{
    [MenuItem("IC/Dump Hand Rig (VR02/VR03)")]
    public static void Run() => Run("Assets/../Docs_VR/dump_handrig.txt");

    public static void Run(string outPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("== DumpHandRig " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ==");
        foreach (var hero in new[] { "PanShi", "ZhenYue" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ArtSourceRaw/Heroes/" + hero + ".fbx");
            if (go == null) { foreach (var p in AssetDatabase.FindAssets(hero, new[] { "Assets" }))
                { var path = AssetDatabase.GUIDToAssetPath(p); if (path.EndsWith(hero + ".fbx")) { go = AssetDatabase.LoadAssetAtPath<GameObject>(path); break; } } }
            if (go == null) { sb.AppendLine("[" + hero + "] fbx 未找到"); continue; }
            sb.AppendLine();
            sb.AppendLine("########## " + hero + " (" + AssetDatabase.GetAssetPath(go) + ") ##########");
            var bones = go.GetComponentsInChildren<Transform>(true);
            sb.AppendLine("骨骼节点总数=" + bones.Length);
            sb.AppendLine("-- 含 hand/finger/thumb/index/middle/ring/pinky 语义的骨骼 --");
            int fingerCount = 0;
            foreach (var b in bones)
            {
                var n = b.name.ToLowerInvariant();
                if (n.Contains("hand") || n.Contains("finger") || n.Contains("thumb")
                    || n.Contains("index") || n.Contains("middle") || n.Contains("ring") || n.Contains("pinky"))
                { sb.AppendLine("  " + AnimationUtility.CalculateTransformPath(b, go.transform)); fingerCount++; }
            }
            if (fingerCount == 0) sb.AppendLine("  (无 —— 该 fbx 没有手指骨语义)");
            sb.AppendLine("-- 手部直接子节点(判断手掌可分指程度) --");
            foreach (var b in bones)
            {
                var n = b.name.ToLowerInvariant();
                if ((n.EndsWith("hand") || n.EndsWith("hand1") || n.EndsWith("l_hand") || n.EndsWith("r_hand")
                     || n.Replace("mixamorig:", "").EndsWith("lefthand") || n.Replace("mixamorig:", "").EndsWith("righthand"))
                    && b.childCount > 0)
                {
                    sb.AppendLine("  " + b.name + " 子节点(" + b.childCount + "):");
                    for (int i = 0; i < b.childCount && i < 20; i++)
                        sb.AppendLine("    -> " + b.GetChild(i).name + " (childCount=" + b.GetChild(i).childCount + ")");
                }
            }
            // Avatar 人形映射:实例化 + Animator.GetBoneTransform(与游戏内 HandBone 同一路径,证据即真实链路)
            sb.AppendLine("-- Avatar 人形映射(实例化后 GetBoneTransform) --");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
            try
            {
                var anim = inst.GetComponent<Animator>();
                if (anim == null || anim.avatar == null || !anim.avatar.isHuman)
                    sb.AppendLine("  无 Animator/Avatar 或非人形(isHuman=false) → GetBoneTransform 不可用,游戏内走名字扫描");
                else
                {
                    foreach (var hb in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                        HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.Hips })
                    {
                        var t = anim.GetBoneTransform(hb);
                        sb.AppendLine("  " + hb + " → " + (t == null ? "(未映射)" : t.name));
                    }
                }
            }
            finally { Object.DestroyImmediate(inst); }
            // SMR 与权重
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var wc = mesh.boneWeights;   // BoneWeight[](每顶点 4 影响)
                var boneList = smr.bones;
                sb.AppendLine("-- SMR: " + smr.name + " bones=" + boneList.Length
                    + " verts=" + mesh.vertexCount + " weights=" + wc.Length);
                // 手骨在 bones[] 中的索引 → 这些索引上拿到权重的顶点数(且权重>0.1)
                for (int bi = 0; bi < boneList.Length; bi++)
                {
                    var bn = boneList[bi] != null ? boneList[bi].name.ToLowerInvariant() : "";
                    bool isHand = bn.Contains("hand") || bn.Contains("finger") || bn.Contains("thumb")
                        || bn.Contains("index") || bn.Contains("middle") || bn.Contains("ring") || bn.Contains("pinky");
                    if (!isHand) continue;
                    int verts = 0; float maxW = 0f;
                    for (int wi = 0; wi < wc.Length; wi++)
                    {
                        float wsum = 0f;
                        if (wc[wi].boneIndex0 == bi && wc[wi].weight0 > 0.1f) wsum = wc[wi].weight0;
                        else if (wc[wi].boneIndex1 == bi && wc[wi].weight1 > 0.1f) wsum = wc[wi].weight1;
                        else if (wc[wi].boneIndex2 == bi && wc[wi].weight2 > 0.1f) wsum = wc[wi].weight2;
                        else if (wc[wi].boneIndex3 == bi && wc[wi].weight3 > 0.1f) wsum = wc[wi].weight3;
                        if (wsum > 0f) { verts++; if (wsum > maxW) maxW = wsum; }
                    }
                    if (verts > 0) sb.AppendLine("    [权重] " + boneList[bi].name + " idx=" + bi
                        + " 影响顶点数(>0.1)=" + verts + " 最大权重=" + maxW.ToString("F2"));
                }
            }
        }
        // 盾/锤武器件结构(锚点标定用)
        foreach (var wpn in new[] { "磐石_盾", "镇岳_大锤" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Models/Weapons/" + wpn + ".fbx");
            if (go == null) { foreach (var p in AssetDatabase.FindAssets(wpn, new[] { "Assets" }))
                { var path = AssetDatabase.GUIDToAssetPath(p); if (path.EndsWith(wpn + ".fbx")) { go = AssetDatabase.LoadAssetAtPath<GameObject>(path); break; } } }
            sb.AppendLine();
            if (go == null) { sb.AppendLine("########## 武器 " + wpn + " 未找到 ##########"); continue; }
            sb.AppendLine("########## 武器 " + wpn + " (" + AssetDatabase.GetAssetPath(go) + ") ##########");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                var m = mf != null ? mf.sharedMesh : null;
                sb.AppendLine("  renderer=" + r.name + " verts=" + (m != null ? m.vertexCount.ToString() : "?")
                    + " 子节点数=" + r.transform.childCount);
                for (int i = 0; i < r.transform.childCount && i < 12; i++)
                    sb.AppendLine("    -> " + r.transform.GetChild(i).name);
            }
            sb.AppendLine("  根旋转(导入预设法内)=" + go.transform.rotation.eulerAngles.ToString("F1"));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[DumpHandRig] 写出 " + outPath + " (" + sb.Length + " chars)");
    }
}
