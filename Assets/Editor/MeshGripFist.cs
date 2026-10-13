using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// VR03(2026-09-21 返工单):镇岳握拳 mesh 变体。
// 取证结论(Docs_VR/dump_handrig.txt):六英雄为 31 骨 Mixamo 简化骨架,Hand 下只有
// Hand_end、无手指骨,手部网格权重整体绑在 Hand 骨(镇岳 RightHand 1267 顶点,w=1.0)。
// → 走返工单"无手指骨"分支的**局部手部网格姿态修正**:对右手蒙皮顶点做"卷指"变换
// (腕部 0° → 指尖 75°,绕侧向轴、以掌心为 pivot),输出 _grip mesh 资产。
// mesh 级修改不经过骨骼,Animator 每帧蒙皮不会覆盖握拳形状。
public static class MeshGripFist
{
    const float WThreshold = 0.30f;    // 参与"手部几何"统计的权重阈值
    const float WApply = 0.10f;        // 开始施加卷曲的权重阈值(过渡带按权重混合)
    const float MaxCurlDeg = 115f;     // 指尖最大卷曲角(低模手视觉可读性:75°读不出"握",115°+线性才像握拳)
    const float CurlStartT = 0.10f;    // 沿指尖方向的起始卷曲位置(掌根不动)

    [MenuItem("IC/Make Grip Fist Mesh (VR03)")]
    public static void Run() => RunFor("ZhenYue", HumanBodyBones.RightHand, "Docs_VR/grip_fist_report.txt");

    public static void RunFor(string hero, HumanBodyBones handBone, string reportPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("== MeshGripFist " + hero + " " + handBone + " " + System.DateTime.Now.ToString("HH:mm:ss") + " ==");
        var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Models/Heroes/" + hero + ".fbx");
        if (src == null) { File.WriteAllText(reportPath, "fbx 未找到"); Debug.LogError("[MeshGripFist] fbx 未找到"); return; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        try
        {
            var anim = inst.GetComponent<Animator>();
            var hand = anim != null ? anim.GetBoneTransform(handBone) : null;
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            if (hand == null || smr == null || smr.sharedMesh == null) { sb.AppendLine("缺 Animator/手骨/SMR"); return; }
            int handIdx = System.Array.IndexOf(smr.bones, hand);
            if (handIdx < 0) { sb.AppendLine("手骨不在 SMR.bones 里"); return; }
            var mesh = Object.Instantiate(smr.sharedMesh);
            mesh.name = hero + "_grip";
            var bones = smr.bones;
            var bp = smr.sharedMesh.bindposes;
            var bw = smr.sharedMesh.boneWeights;
            var verts = mesh.vertices;
            var norms = mesh.normals;

            // 手部顶点收集(绑定空间):统计手骨局部的包围盒 + 找指尖方向(手骨→Hand_end)
            Matrix4x4 toBone = bp[handIdx];
            var handPts = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < verts.Length; i++)
            {
                float w = WeightOf(bw[i], handIdx);
                if (w >= WThreshold) handPts.Add(toBone.MultiplyPoint3x4(verts[i]));
            }
            if (handPts.Count < 10) { sb.AppendLine("手部顶点过少: " + handPts.Count); return; }
            var box = new Bounds(handPts[0], Vector3.zero);
            foreach (var p in handPts) box.Encapsulate(p);
            // 指尖方向:Hand_end 在手骨局部(绑定)的位置方向(比包围盒轴更语义化)
            var tip = hand.GetChild(0);   // 取证:Hand 下唯一子节点即 Hand_end
            Vector3 tipDir = tip.localPosition;   // 绑定层级即局部,无动画
            if (tipDir.sqrMagnitude < 1e-8f) tipDir = Vector3.up;
            tipDir.Normalize();
            sb.AppendLine("手部顶点=" + handPts.Count + " 骨局部包围盒 size=" + box.size.ToString("F3")
                + " 指尖方向(骨局部)=" + tipDir.ToString("F2"));

            // 掌厚轴 = 手顶点在垂直指尖平面上的最薄方向:用顶点对指尖方向的协方差最小特征向量近似。
            // 简化:在垂直 tipDir 的两个正交方向上投影,取投影方差小者为掌厚。
            Vector3 a1, a2;
            if (Mathf.Abs(tipDir.y) < 0.9f) { a1 = Vector3.Cross(tipDir, Vector3.up).normalized; a2 = Vector3.Cross(tipDir, a1).normalized; }
            else { a1 = Vector3.Cross(tipDir, Vector3.right).normalized; a2 = Vector3.Cross(tipDir, a1).normalized; }
            float v1 = 0f, v2 = 0f;
            foreach (var p in handPts) { v1 += Mathf.Pow(Vector3.Dot(p - box.center, a1), 2f); v2 += Mathf.Pow(Vector3.Dot(p - box.center, a2), 2f); }
            Vector3 thickDir = v1 < v2 ? a1 : a2;              // 掌厚(卷曲朝这个方向合拢)
            Vector3 sideDir = Vector3.Cross(tipDir, thickDir).normalized;   // 侧向 = 屈曲轴
            sb.AppendLine("掌厚方向=" + thickDir.ToString("F2") + " 屈曲轴=" + sideDir.ToString("F2"));

            // 掌心 pivot:包围盒中心沿指尖回退到 t≈0.35 处
            float alongMin = float.MaxValue, alongMax = float.MinValue;
            foreach (var p in handPts) { float d = Vector3.Dot(p - box.center, tipDir); alongMin = Mathf.Min(alongMin, d); alongMax = Mathf.Max(alongMax, d); }
            Vector3 pivot = box.center + tipDir * Mathf.Lerp(alongMin, alongMax, 0.35f);
            float span = Mathf.Max(0.01f, alongMax - alongMin);

            int touched = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                float w = WeightOf(bw[i], handIdx);
                if (w < WApply) continue;
                Vector3 pb = toBone.MultiplyPoint3x4(verts[i]);
                float t = (Vector3.Dot(pb - box.center, tipDir) - alongMin) / span;   // 0=腕 1=指尖
                float k = t <= CurlStartT ? 0f : (t - CurlStartT) / (1f - CurlStartT);
                float deg = MaxCurlDeg * k * w;   // 线性卷曲(平方缓入在低模手上读不出握感)+ 权重过渡带混合
                if (deg < 0.05f) continue;
                var rot = Quaternion.AngleAxis(deg, sideDir);
                Vector3 pb2 = pivot + rot * (pb - pivot);
                Vector3 world2 = bp[handIdx].inverse.MultiplyPoint3x4(pb2);
                verts[i] = Vector3.Lerp(verts[i], world2, 1f);   // deg 已含权重因子
                norms[i] = (rot * toBone.MultiplyVector(norms[i])).normalized;   // 近似:法线同旋转
                touched++;
            }
            sb.AppendLine("卷曲顶点数=" + touched + " / 总顶点=" + verts.Length + " (pivot=" + pivot.ToString("F2") + " span=" + span.ToString("F3") + ")");

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.RecalculateBounds();
            string assetPath = "Assets/Resources/Models/Heroes/" + hero + "_grip.asset";
            AssetDatabase.CreateAsset(mesh, assetPath);
            AssetDatabase.SaveAssets();
            sb.AppendLine("写出 " + assetPath);
            Debug.Log("[MeshGripFist] " + sb.ToString());
        }
        finally { Object.DestroyImmediate(inst); File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false)); }
    }

    static float WeightOf(BoneWeight b, int idx)
    {
        float w = 0f;
        if (b.boneIndex0 == idx) w += b.weight0;
        if (b.boneIndex1 == idx) w += b.weight1;
        if (b.boneIndex2 == idx) w += b.weight2;
        if (b.boneIndex3 == idx) w += b.weight3;
        return w;
    }
}
