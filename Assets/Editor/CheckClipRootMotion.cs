using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // 诊断:动画"非原地"扫描——量化每个 clip 的骨盆(Hips/Pelvis)水平位移。
    // 背景:2026-09-10 制作人目击 AI 生成动画带全局位移,模型被拽离实体位置约一个身位,
    // 静态截图看不出,靠人工目击才发现。本工具把"非原地"变成入库时机器可判事实,
    // 供入库流程量化诊断(不是构建门禁,发现违规仍 Exit(0))。
    // 判定语义(第 3 轮修复):以净位移(首末帧水平位移的模)为主判据——
    // 真正造成"模型漂离光环/血条"的是循环播放下的净位移累积;
    // 前刺后收回这类瞬时摆幅大但净位移≈0 的动作无需补偿,单独归为"往复位移"。
    public static class CheckClipRootMotion
    {
        // 扫描根目录:英雄动画与武器模型所在目录,常量数组便于日后扩展。
        private static readonly string[] ScanDirs =
        {
            "Assets/Resources/Models/Heroes/Anims",
            "Assets/Resources/Models/Weapons"
        };

        // JSON 明细输出路径,固定写到截图/验收目录,便于脚本与人工复查。
        private const string ReportPath = "D:/DevTools/shots/play_check/rootmotion_report.json";

        // 水平位移判定阈值。来历:制作人目击的错位约一个身位(约 1m 级),
        // 正常原地动画的骨盆抖动远小于此;0.15m 是"脚底不再贴地"的经验下限,
        // 既放过正常步伐的水平晃动,又能抓住 AI 生成的整体拖拽位移。
        // 同一阈值同时用于净位移(累积判据)与极差(往复判据)。
        private const float HorizontalTolerance = 0.15f;

        [MenuItem("IronCurtain/Check Clip Root Motion")]
        public static void RunFromMenu()
        {
            RunInternal(isBatch: false);
        }

        // 批处理入口:
        // Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IronCurtain.EditorTools.CheckClipRootMotion.Run -logFile <log>
        public static void Run()
        {
            RunInternal(isBatch: true);
        }

        private static void RunInternal(bool isBatch)
        {
            var report = new List<Dictionary<string, object>>();
            int total = 0, accumulate = 0, reciprocate = 0, inplace = 0, noData = 0;
            var sb = new StringBuilder();

            foreach (var dir in ScanDirs)
            {
                // 枚举方式:不用 t:ModelImporter 类型过滤(实测在部分工程返回空集合),
                // 改用文件系统按扩展名枚举,再经 AssetDatabase 加载子资产。
                string[] files;
                try
                {
                    files = Directory.GetFiles(dir, "*.fbx", SearchOption.AllDirectories);
                }
                catch (DirectoryNotFoundException)
                {
                    Debug.LogWarning($"[rootmotion] 目录不存在,跳过: {dir}");
                    continue;
                }

                Debug.Log($"[rootmotion] 扫描目录 {dir} 文件数={files.Length}");

                foreach (var file in files)
                {
                    string assetPath = file.Replace('\\', '/'); // AssetDatabase 统一用 '/' 分隔
                    Debug.Log($"[rootmotion] 处理 {assetPath}");

                    // LoadAllAssetsAtPath 才能取出 fbx 内嵌的子资产(各 AnimationClip)。
                    var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                    foreach (var obj in clips)
                    {
                        var clip = obj as AnimationClip;
                        if (clip == null) continue;
                        // Unity 自动生成的预览动画不是入库资产,跳过。
                        if (clip.name.StartsWith("__preview__")) continue;

                        total++;
                        AnalyzeClip(assetPath, clip, report, sb, ref accumulate, ref reciprocate, ref inplace, ref noData);
                    }
                }
            }

            Debug.Log("[rootmotion] 明细\n" + sb);
            Debug.Log($"[rootmotion] SUMMARY total={total} accumulate={accumulate} reciprocate={reciprocate} inplace={inplace} noData={noData}");
            WriteJson(report);

            if (isBatch) EditorApplication.Exit(0); // 诊断工具:违规也不返回非 0,是否放行由流程决定
        }

        private static void AnalyzeClip(string file, AnimationClip clip,
            List<Dictionary<string, object>> report, StringBuilder sb,
            ref int accumulate, ref int reciprocate, ref int inplace, ref int noData)
        {
            // 日志标识:同 fbx 内 clip 常重名(如都叫 "Anim"),加上文件名才能区分来源。
            string fbxName = System.IO.Path.GetFileNameWithoutExtension(file);
            string label = $"{fbxName}/{clip.name}";

            float rangeX = 0, rangeZ = 0, netX = 0, netZ = 0, rangeY = 0;
            bool hasX = false, hasZ = false, hasY = false;

            // Humanoid 导入时骨骼动画被重定向到 muscle 空间,原始骨骼路径曲线
            // (mixamorig:Hips 的 m_LocalPosition.*)不再暴露;根位移表现为空路径(path=="")
            // 上的 RootT.x/y/z 曲线。因此优先读 RootT,骨骼路径仅作 Generic 兜底。
            var bindings = AnimationUtility.GetCurveBindings(clip);
            bool usedRootT = false;

            foreach (var binding in bindings)
            {
                if (binding.path != "") continue;
                string prop = binding.propertyName.ToLowerInvariant();
                if (!(prop == "roott.x" || prop == "roott.y" || prop == "roott.z")) continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0) continue;

                float min = curve.keys[0].value, max = curve.keys[0].value;
                foreach (var k in curve.keys)
                {
                    if (k.value < min) min = k.value;
                    if (k.value > max) max = k.value;
                }
                float range = max - min;
                float net = curve.keys[curve.length - 1].value - curve.keys[0].value;

                if (prop == "roott.x") { rangeX = range; netX = net; hasX = true; }
                else if (prop == "roott.z") { rangeZ = range; netZ = net; hasZ = true; }
                else if (prop == "roott.y") { rangeY = range; hasY = true; }
                usedRootT = true;
            }

            // 兜底:没有 RootT.* 曲线时,按骨骼路径找 Hips/Pelvis 的 m_LocalPosition.*
            // (应对非 Humanoid 或 Generic 导入的资产)。
            if (!usedRootT)
            {
                foreach (var binding in bindings)
                {
                    // Mixamo 等命名形如 "mixamorig:Hips",统一小写比对,且只认骨骼位置曲线
                    // (非骨骼根的 m_LocalPosition 属于骨架层级,不在此列;普通属性曲线 propertyName 不同)。
                    string pathLower = binding.path.ToLowerInvariant();
                    bool isHips = pathLower.EndsWith("hips") || pathLower.EndsWith("pelvis")
                        || pathLower.EndsWith("hips/") == false && pathLower.Contains("hips"); // 兼容嵌套路径
                    if (binding.type != typeof(Transform) && binding.type != typeof(Animator)) continue;
                    if (!isHips) continue;

                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.length == 0) continue;

                    float min = curve.keys[0].value, max = curve.keys[0].value;
                    foreach (var k in curve.keys)
                    {
                        if (k.value < min) min = k.value;
                        if (k.value > max) max = k.value;
                    }
                    float range = max - min;
                    float net = curve.keys[curve.length - 1].value - curve.keys[0].value;

                    if (binding.propertyName == "m_LocalPosition.x") { rangeX = range; netX = net; hasX = true; }
                    else if (binding.propertyName == "m_LocalPosition.z") { rangeZ = range; netZ = net; hasZ = true; }
                    else if (binding.propertyName == "m_LocalPosition.y") { rangeY = range; hasY = true; }
                }
            }

            // 两条路径都没有读到:可能是非 Humanoid/Generic 且无 Hips 曲线,或导入配置异常,
            // 需人工确认。附带 bindingsSample 便于日后快速定位曲线实际长什么样。
            if (!hasX && !hasZ && !hasY)
            {
                noData++;
                var sample = new List<string>();
                int n = 0;
                foreach (var b in bindings)
                {
                    if (n >= 8) break;
                    sample.Add(b.path + ":" + b.propertyName);
                    n++;
                }

                sb.Append($"[rootmotion] {label} | 时长 {clip.length:F1}s | 无数据(未找到 RootT/Hips 位置曲线) | 判定=无数据\n");
                var entry = new Dictionary<string, object>
                {
                    ["file"] = file, ["clip"] = clip.name, ["durationSec"] = clip.length,
                    ["rangeX"] = 0f, ["rangeZ"] = 0f, ["netX"] = 0f, ["netZ"] = 0f, ["netHorizontal"] = 0f, ["rangeY"] = 0f,
                    ["verdict"] = "无数据"
                };
                if (sample.Count > 0) entry["bindingsSample"] = string.Join("; ", sample);
                else entry["bindingsSample"] = "";
                report.Add(entry);
                return;
            }

            // 主判据:净位移(首末帧水平位移的模)。循环播放下净位移才会累积,
            // 造成"模型漂离光环/血条";瞬时摆幅只作往复位移的参考分类。
            float netHorizontal = Mathf.Sqrt(netX * netX + netZ * netZ);
            float swing = Mathf.Max(rangeX, rangeZ); // Y 极差是正常步伐起伏,不计入判定

            string verdict;
            if (netHorizontal > HorizontalTolerance)
            {
                verdict = "累积位移(需补偿)";
                accumulate++;
            }
            else if (swing > HorizontalTolerance)
            {
                verdict = "往复位移(净≈0,无需补偿)";
                reciprocate++;
            }
            else
            {
                verdict = "原地";
                inplace++;
            }

            sb.Append($"[rootmotion] {label} | 时长 {clip.length:F1}s | 净位移 {netHorizontal:F2}m" +
                      $"(X极差{rangeX:F2}/ Z极差{rangeZ:F2}/ Y极差{rangeY:F2})" +
                      $" | 判定={verdict}\n");

            report.Add(new Dictionary<string, object>
            {
                ["file"] = file, ["clip"] = clip.name, ["durationSec"] = clip.length,
                ["rangeX"] = rangeX, ["rangeZ"] = rangeZ, ["netX"] = netX, ["netZ"] = netZ,
                ["netHorizontal"] = netHorizontal, ["rangeY"] = rangeY, ["verdict"] = verdict
            });
        }

        private static void WriteJson(List<Dictionary<string, object>> report)
        {
            string dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var json = new StringBuilder("[\n");
            for (int i = 0; i < report.Count; i++)
            {
                json.Append("  {");
                bool first = true;
                foreach (var kv in report[i])
                {
                    if (!first) json.Append(", ");
                    first = false;
                    if (kv.Value is string s) json.Append($"\"{kv.Key}\": \"{s}\"");
                    else if (kv.Value is float f) json.Append($"\"{kv.Key}\": {f:0.###}");
                    else json.Append($"\"{kv.Key}\": {kv.Value}");
                }
                json.Append("}");
                if (i < report.Count - 1) json.Append(",");
                json.Append("\n");
            }
            json.Append("]");
            File.WriteAllText(ReportPath, json.ToString());
            Debug.Log($"[rootmotion] JSON 报告已写入: {ReportPath} ({report.Count} 条)");
        }
    }
}
