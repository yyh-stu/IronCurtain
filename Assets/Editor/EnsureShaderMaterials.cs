using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 2026-09-14 ★交付级修复:让"只靠 Shader.Find 引用"的 URP shader 进得了独立构建。
    ///
    /// 问题:全工程 13 处 `Shader.Find("Universal Render Pipeline/Unlit|Lit|Particles/Unlit")`。
    /// 编辑器里总能命中,但**独立构建里这些 shader 会被剥掉** → 播放器一进战斗场景
    /// `new Material(null)` 抛 ArgumentNullException,`GameBootstrap.Awake` 中断,
    /// 地图/墙体/单位/UI 全都不建(整局空白)。这也是此前"只能在编辑器 Play 里验收"的隐性原因。
    ///
    /// 走过的弯路(留作教训):第一版是把它们加进 GraphicsSettings 的 **Always Included Shaders** ——
    /// 那会强制编译**全部**变体,实测进度 11843/589824 变体(约 4 小时),构建立刻变成不可用。
    ///
    /// 正解:在 `Assets/Resources/Shaders/` 下各放一个引用该 shader 的**材质资产**。
    /// 材质会被打进包,其依赖的 shader 随之入包(只带用到的变体),运行时 `Shader.Find` 即可命中。
    /// 本工具幂等:已存在就只补引用,不覆盖参数。
    /// </summary>
    public static class EnsureShaderMaterials
    {
        private const string Dir = "Assets/Resources/Shaders";

        private static readonly Dictionary<string, string> Needed = new Dictionary<string, string>
        {
            { "urp_lit", "Universal Render Pipeline/Lit" },
            { "urp_unlit", "Universal Render Pipeline/Unlit" },
            { "urp_particles_unlit", "Universal Render Pipeline/Particles/Unlit" },
            { "sprites_default", "Sprites/Default" },
        };

        [MenuItem("IronCurtain/Ensure Shader Materials")]
        public static void Run()
        {
            Directory.CreateDirectory(Dir);
            foreach (var kv in Needed)
            {
                string path = Dir + "/" + kv.Key + ".mat";
                var shader = Shader.Find(kv.Value);
                if (shader == null) { Debug.LogWarning("[shaders] 编辑器里都找不到,跳过:" + kv.Value); continue; }
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                    Debug.Log("[shaders] 新建材质 " + path + " → " + kv.Value);
                }
                else if (mat.shader != shader)
                {
                    mat.shader = shader;
                    EditorUtility.SetDirty(mat);
                    Debug.Log("[shaders] 修正材质 shader " + path + " → " + kv.Value);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[shaders] 完成:Resources/Shaders 下的材质会把对应 shader 带进构建。");
        }

    }
}
