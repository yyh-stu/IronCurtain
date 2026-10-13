using System.IO;
using System.Reflection;
using System.Text;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 2026-09-13 新增:UI 布局探针(诊断"面板超出屏幕/不可视不可点")。
    /// 不靠推理 —— 打印运行时真实尺寸链路:屏幕像素、PanelSettings 实际生效的缩放、
    /// 根元素与 Screen 容器的解析尺寸、以及**所有命名元素的面板坐标矩形**。
    ///
    /// 两种跑法:
    ///   · `Run`        —— 编辑模式。**注意**:编辑模式下 UITK 不做布局(resolvedStyle 全 NaN),
    ///                     只能用来核对 PanelSettings 的档位,量不到矩形。
    ///   · `RunInPlay`  —— Play 模式,等待若干帧后导出**真实**布局矩形,然后自己 EditorApplication.Exit(0)
    ///                     (所以不需要外部杀进程,避开 D 类事故那种风险)。
    ///
    /// 用法:Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.UiProbe.RunInPlay -logFile &lt;log&gt;
    /// 产物:Logs/ui_probe.txt
    /// </summary>
    public static class UiProbe
    {
        private const int MaxDepth = 8;
        private static int _frames;

        // ---------- 编辑模式入口(只核对 PanelSettings 档位) ----------
        public static void Run()
        {
            BootInEditMode();
            Dump();
            Debug.Log("[uiprobe] done(edit mode:布局为 NaN 属正常)");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---------- Play 模式入口(量真实矩形) ----------


        // ⚠ 2026-09-13 教训:曾有一个 RunInPlay() 入口(EnterPlaymode + EditorApplication.update 轮询,
        // 等 240 帧后导出布局)。实测在 -batchmode 下**不产出且不退出**,把 Unity 实例挂住并占住项目锁,
        // 进而导致后续批处理以 `HandleProjectAlreadyOpenInAnotherInstance` 崩溃 —— 正是文档里
        // "无头实例副作用"那一类。已删除,不再留这个陷阱。要量真实布局请在**编辑器 GUI 里**进 Play,
        // 直接看 Hierarchy 的 UITK Debugger,或把 Dump() 挂到某个已存在的 Play 取证通道上。

        private static void BootInEditMode()
        {
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var bootGo = new GameObject("UiProbeBoot");
            var boot = bootGo.AddComponent<GameBootstrap>();
            typeof(GameBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(boot, null);
            // 编辑模式下 AddComponent 不自动跑 Awake,须手动补(UiRoot 那套容器才建得起来)
            var uiRoot = bootGo.GetComponent<UiRoot>();
            if (uiRoot == null) { Debug.LogError("[uiprobe] no UiRoot component"); return; }
            var pflags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(UiRoot).GetMethod("Awake", pflags)?.Invoke(uiRoot, null);
            typeof(UiRoot).GetMethod("Start", pflags)?.Invoke(uiRoot, null);
            for (int i = 0; i < 60; i++) EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[uiprobe] screen=" + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height
                          + " dpi=" + UnityEngine.Screen.dpi + " safeArea=" + UnityEngine.Screen.safeArea);

            var ps = Resources.Load<PanelSettings>("UI/PanelSettings");
            if (ps != null)
                sb.AppendLine("[uiprobe] panelSettings scaleMode=" + ps.scaleMode
                              + " ref=" + ps.referenceResolution
                              + " match=" + ps.match
                              + " scale=" + ps.scale);

            var root = UiRoot.Current != null ? UiRoot.Current.Screen : null;
            if (root == null)
            {
                var doc = Object.FindFirstObjectByType<UIDocument>();
                root = doc != null ? doc.rootVisualElement : null;
            }
            if (root == null) { sb.AppendLine("[uiprobe] no UITK root"); }
            else
            {
                var rve = root.parent ?? root;
                sb.AppendLine("[uiprobe] root worldBound=" + Rect(rve.worldBound));
                sb.AppendLine("[uiprobe] screenContainer worldBound=" + Rect(root.worldBound));
                Walk(root, 0, sb);
            }

            Debug.Log(sb.ToString());
            File.WriteAllText("Logs/ui_probe.txt", sb.ToString());
        }

        private static string Rect(UnityEngine.Rect r) =>
            "(" + r.x.ToString("F0") + "," + r.y.ToString("F0") + " "
            + r.width.ToString("F0") + "x" + r.height.ToString("F0") + ")";

        private static void Walk(VisualElement e, int depth, StringBuilder sb)
        {
            if (depth > MaxDepth) return;
            var b = e.worldBound;
            if (!string.IsNullOrEmpty(e.name) && (b.width > 0.5f || b.height > 0.5f))
                sb.AppendLine("[uiprobe] " + new string(' ', depth * 2) + e.name + " " + Rect(b)
                              + " vis=" + (e.resolvedStyle.display != DisplayStyle.None)
                              + " cls=" + string.Join(",", e.GetClasses()));
            for (int i = 0; i < e.childCount; i++) Walk(e.ElementAt(i), depth + 1, sb);
        }
    }
}
