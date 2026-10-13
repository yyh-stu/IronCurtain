using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 2026-09-14 新增:出独立 Windows 播放器,用于**实机(真鼠标)验收**。
    /// 起因:编辑器内抓帧有已知伪影(帧累积重影、Camera.Render 不含 UITK、ScreenCapture 本机产不出文件),
    /// 而"点开面板没反应""换边还是坏的"这类问题只有真窗口真点击才算数。
    /// 输出目录可用环境变量 IC_BUILD_DIR 覆盖,默认 D:/DevTools/build_ic。
    ///
    /// 用法:Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.BuildPlayer.Perform -logFile &lt;log&gt; -quit
    /// </summary>
    public static class BuildPlayer
    {
        public static void Perform()
        {
            // 2026-09-14:构建前先保证 URP shader 会被打进包(Resources 材质方案) ——
            // 否则播放器里 Shader.Find 返回 null,战斗场景 Awake 直接抛异常(实测)。
            EnsureShaderMaterials.Run();
            string dir = System.Environment.GetEnvironmentVariable("IC_BUILD_DIR");
            if (string.IsNullOrEmpty(dir)) dir = "D:/DevTools/build_ic";
            Directory.CreateDirectory(dir);

            var scenes = new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/BP.unity", "Assets/Scenes/Main.unity" };
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = dir + "/IronCurtain.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            // 开发版构建:保留日志文件与调试符号,实机出问题时能直接看 Player.log
            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.allowDebugging = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);
            // 窗口化 1600x900,方便桌面控制与截图(构建后仍可用 -screen-* 覆盖)
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log("[build] result=" + s.result + " size=" + s.totalSize
                + " errors=" + s.totalErrors + " time=" + s.totalTime.TotalSeconds.ToString("F1") + "s"
                + " out=" + opts.locationPathName);
            // 2026-09-14:无头构建会把"上次打开的场景"记录写空 → 制作人下次双击桌面快捷方式打开工程是
            // 一个**无标题空场景**(他的原话"连主菜单都不是")。这里在退出前把主界面场景开回来,
            // 让**构建自己把记录修回去**,从源头去掉这个反复踩的坑。
            DefaultSceneOnLaunch.RepairStartupScene();
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
