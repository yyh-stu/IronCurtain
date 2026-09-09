using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // 2026-09-10 建筑材质生成:Blender 管线已把 diffuse 重渲染入 Tex/{名}_0.png(1024²),
    // 本脚本生成 URP Lit BuildingMat_{名}(同 HeroMat 流程,含废墟共 8 张)。
    // 用法:菜单 IronCurtain/Generate Building Mats,或批处理 -executeMethod ...GenerateBuildingMats.Run
    public static class GenerateBuildingMats
    {
        private static readonly string[] Buildings =
            { "主基地", "兵营", "箭塔", "资源点", "主基地_废墟", "兵营_废墟", "箭塔_废墟", "资源点_废墟" };

        [MenuItem("IronCurtain/Generate Building Mats")]
        public static void Run()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            int made = 0;
            foreach (var b in Buildings)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Models/Buildings/Tex/" + b + "_0.png");
                if (tex == null) { Debug.LogWarning("[bldmat2] missing tex " + b); continue; }
                var path = "Assets/Resources/Models/Buildings/BuildingMat_" + b + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetColor("_BaseColor", Color.white);
                mat.SetTexture("_BaseMap", tex);
                mat.SetFloat("_Smoothness", 0.3f);
                mat.SetFloat("_Metallic", 0f);
                EditorUtility.SetDirty(mat);
                made++;
                Debug.Log("[bldmat2] " + b + " -> " + path + " tex=" + tex.width + "x" + tex.height);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[bldmat2] done made=" + made);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
