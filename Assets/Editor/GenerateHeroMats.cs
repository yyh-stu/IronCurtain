using System.IO;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // 2026-09-09 管线修复:绑骨 FBX 内嵌贴图 Unity 解不开(tex=NULL, 0.8 灰)。
    // 贴图已从 FBX 字节级抽取入库 Tex/{Hero}.png,本脚本生成 URP Lit 材质 HeroMat_{Hero}。
    // 用法:菜单 IronCurtain/Generate Hero Mats,或批处理 -executeMethod ...GenerateHeroMats.Run
    public static class GenerateHeroMats
    {
        private static readonly string[] Heroes = { "ZhenYue", "PanShi", "DuanFeng", "ZhuFeng", "ZhuiRi", "XingLuo" };

        [MenuItem("IronCurtain/Generate Hero Mats")]
        public static void Run()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            int made = 0;
            foreach (var h in Heroes)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Models/Heroes/Tex/" + h + ".png");
                if (tex == null) { Debug.LogWarning("[heromat] missing tex " + h); continue; }
                var path = "Assets/Resources/Models/Heroes/HeroMat_" + h + ".mat";
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
                Debug.Log("[heromat] " + h + " -> " + path + " tex=" + tex.width + "x" + tex.height);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[heromat] done made=" + made);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
