using System.Collections.Generic;
using System.IO;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// W01(2026-09-18 GPT 审查):武器资源验证批处理。
    /// 检查六英雄×全部武器 FBX 是否存在、可加载、有渲染器;输出清单含 GUID。
    /// 缺件退出码=1(批处理可阻断);全齐=0。
    /// 用法:Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.WeaponValidate.Run
    /// </summary>
    public static class WeaponValidate
    {
        public static void Run()
        {
            var report = new List<string>();
            int missing = 0;

            // 从 HeroWeapons 配置提取全部武器模型名
            var heroNames = new Dictionary<HeroId, string>
            {
                { HeroId.PanShi, "磐石" }, { HeroId.ZhenYue, "镇岳" }, { HeroId.DuanFeng, "断峰" },
                { HeroId.ZhuFeng, "逐风" }, { HeroId.ZhuiRi, "坠日" }, { HeroId.XingLuo, "星落" },
            };
            var modelNames = new HashSet<string>();
            // 反射读取 HeroWeapons(私有静态字典),逐项提取 Model 字段(通用反射,不依赖具体类型)
            var field = typeof(EntityView).GetField("HeroWeapons",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (field != null)
            {
                var dict = field.GetValue(null) as System.Collections.IDictionary;
                if (dict != null)
                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        var arr = entry.Value as System.Array;
                        if (arr == null) continue;
                        foreach (var cfg in arr)
                        {
                            if (cfg == null) continue;
                            var modelField = cfg.GetType().GetField("Model");
                            var modelName = modelField != null ? modelField.GetValue(cfg) as string : null;
                            if (!string.IsNullOrEmpty(modelName)) modelNames.Add(modelName);
                        }
                    }
            }

            report.Add("=== 武器资源验证 (W01) ===");
            report.Add($"配置武器数: {modelNames.Count}");
            if (modelNames.Count == 0)
            {
                report.Add("✗ 配置为空:反射读取 HeroWeapons 失败(不是真的零武器,是工具坏了)");
                missing++;   // GPT⑤:空配置必须阻断,不能零项检查 PASS
            }

            foreach (var model in modelNames)
            {
                string path = "Models/Weapons/" + model;
                var prefab = Resources.Load<GameObject>(path);
                if (prefab == null)
                {
                    report.Add($"✗ {model}: Resources.Load 失败 (路径={path})");
                    missing++;
                    continue;
                }
                var renderers = prefab.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    report.Add($"✗ {model}: 无渲染器");
                    missing++;
                    continue;
                }
                // 资产路径与 GUID
                var assetPath = AssetDatabase.GetAssetPath(prefab.GetInstanceID());
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                report.Add($"✓ {model}: renderers={renderers.Length} guid={guid} bounds={bounds.size.ToString("F2")} rootRot={prefab.transform.rotation.eulerAngles.ToString("F0")}");
            }

            // 磁盘上存在但未被配置引用的武器(可能是遗漏或废弃)
            var diskFiles = Directory.GetFiles("Assets/Resources/Models/Weapons", "*.fbx");
            var diskNames = new HashSet<string>();
            foreach (var f in diskFiles)
                diskNames.Add(Path.GetFileNameWithoutExtension(f));
            foreach (var d in diskNames)
                if (!modelNames.Contains(d))
                    report.Add($"⚠ 磁盘有但未配置: {d}");

            report.Add($"=== 结果: {(missing == 0 ? "PASS" : $"FAIL({missing} 缺件)")} ===");
            var text = string.Join("\n", report);
            Debug.Log(text);
            File.WriteAllText("D:/DevTools/shots/weapon_validate.txt", text);

            if (missing > 0)
            {
                Debug.LogError($"[WeaponValidate] {missing} 个武器资源缺失,详见 D:/DevTools/shots/weapon_validate.txt");
                EditorApplication.Exit(1);
            }
            EditorApplication.Exit(0);
        }
    }
}
