using System.IO;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 2026-09-10 建筑 fbx 导入+贴图抽取+材质生成(踩坑#12 流程)。
    /// 用法:菜单 IronCurtain/Setup Building Imports,或批处理 -executeMethod。
    /// 前提:Assets/Resources/Models/Buildings/*.fbx 已由 Blender 转换入库。
    /// </summary>
    public static class BuildingImportSetup
    {
        private static readonly string[] Buildings = { "主基地", "兵营", "箭塔", "资源点" };

        [MenuItem("IronCurtain/Setup Building Imports")]
        public static void Run()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            // 1) 提取贴图
            foreach (var name in Buildings)
            {
                var fbxPath = "Assets/Resources/Models/Buildings/" + name + ".fbx";
                var fbxRuins = "Assets/Resources/Models/Buildings/" + name + "_废墟.fbx";
                ProcessBuilding(name, fbxPath);
                ProcessBuilding(name + "_废墟", fbxRuins);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[bldmat] done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void ProcessBuilding(string name, string fbxPath)
        {
            if (!File.Exists(fbxPath)) { Debug.LogWarning("[bldmat] missing " + fbxPath); return; }
            var texDir = "Assets/Resources/Models/Buildings/Tex";
            Directory.CreateDirectory(texDir);
            // 提取贴图字节(签名扫描)
            var data = File.ReadAllBytes(fbxPath);
            var texFiles = ExtractPng(data, Path.Combine(texDir, name));
            if (texFiles == 0) Debug.LogWarning("[bldmat] " + name + ": no embedded texture found");
            else Debug.Log("[bldmat] " + name + ": extracted " + texFiles + " textures");
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        }

        private static int ExtractPng(byte[] data, string outPath)
        {
            int count = 0;
            int i = 0;
            while (true)
            {
                i = System.Array.IndexOf(data, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, i);
                if (i < 0) break;
                int end = System.Array.IndexOf(data, new byte[] { 0x49, 0x45, 0x4E, 0x44 }, i);
                if (end > 0)
                {
                    var len = end + 8 - i;
                    if (len > 10000 && len < 30000000) // 有效贴图尺寸范围
                    {
                        var p = outPath + "_" + count + ".png";
                        using (var fs = new FileStream(p, FileMode.Create, FileAccess.Write))
                            fs.Write(data, i, len);
                        count++;
                    }
                }
                i += 8;
            }
            return count;
        }
    }
}
