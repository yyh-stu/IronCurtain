using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // 诊断:六英雄 FBX 的材质/贴图/底色导入情况(2026-09-09 制作人反馈模型无色)
    public static class DumpHeroMats
    {
        public static void Run()
        {
            string[] names = { "ZhenYue", "PanShi", "DuanFeng", "ZhuFeng", "ZhuiRi", "XingLuo" };
            foreach (var n in names)
            {
                var prefab = Resources.Load<GameObject>("Models/Heroes/" + n);
                if (prefab == null) { Debug.Log("[dump] " + n + " MISSING"); continue; }
                var go = Object.Instantiate(prefab);
                int rc = 0;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    rc++;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) { Debug.Log("[dump] " + n + " | " + r.name + " | NULLMAT"); continue; }
                        var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap")
                            : (m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null);
                        Color bc = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.clear;
                        Debug.Log("[dump] " + n + " | " + r.name + " | mat=" + m.name
                            + " | shader=" + m.shader.name
                            + " | tex=" + (tex != null ? tex.name + " " + tex.width + "x" + tex.height : "NULL")
                            + " | base=(" + bc.r.ToString("F2") + "," + bc.g.ToString("F2") + "," + bc.b.ToString("F2") + ")");
                    }
                }
                Debug.Log("[dump] " + n + " renderers=" + rc);
                Object.DestroyImmediate(go);
            }
            EditorApplication.Exit(0);
        }
    }
}
