using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // 2026-09-10 武器材质生成:glb 内嵌贴图已抽取重渲染入 Tex/{名}_0.png,
    // 生成 URP Lit WeaponMat_{名}(同 BuildingMat 流程,含战马共 8 张)。
    // 用法:菜单 IronCurtain/Generate Weapon Mats,或批处理 -executeMethod ...GenerateWeaponMats.Run
    public static class GenerateWeaponMats
    {
        private static readonly string[] Items =
            { "磐石_盾", "磐石_大刀", "镇岳_大锤", "断峰_长矛", "逐风_长枪", "坠日_大弓", "星落_法杖", "逐风_战马" };

        [MenuItem("IronCurtain/Generate Weapon Mats")]
        public static void Run()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            int made = 0;
            foreach (var b in Items)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Models/Weapons/Tex/" + b + "_0.png");
                if (tex == null) { Debug.LogWarning("[wpnmat] missing tex " + b); continue; }
                var path = "Assets/Resources/Models/Weapons/WeaponMat_" + b + ".mat";
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
                mat.SetFloat("_Smoothness", 0.35f);
                mat.SetFloat("_Metallic", 0f);
                EditorUtility.SetDirty(mat);
                made++;
                Debug.Log("[wpnmat] " + b + " -> " + path + " tex=" + tex.width + "x" + tex.height);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[wpnmat] done made=" + made);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
