using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 铁幕:前线 · WebGL 构建入口（2026-10 新增，用于作品集「可直接试玩的 Web 版本」）。
/// 用法：Unity.exe -quit -batchmode -projectPath D:\DevTools\IronCurtain
///        -executeMethod BuildWebGL.Build -logFile build_webgl.log
/// 说明：只读取 EditorBuildSettings 里已启用的场景，不改动业务源码与现有 Windows 构建。
/// </summary>
public static class BuildWebGL
{
    private const string OutDir = "D:/DevTools/build_webgl";

    [MenuItem("IronCurtain/Build WebGL")]
    public static void Build()
    {
        if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true);
        Directory.CreateDirectory(OutDir);

        // WebGL 关键设置：默认 32MB 堆对三路大地图 + 六英雄 + 粒子明显不够
        PlayerSettings.WebGL.memorySize = 512;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; // 本地/静态托管可直接跑
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.template = "APPLICATION:Default";

        var scenes = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) scenes.Add(s.path);

        if (scenes.Count == 0)
        {
            Debug.LogError("[BuildWebGL] EditorBuildSettings 里没有已启用的场景，终止。");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("[BuildWebGL] 场景: " + string.Join(", ", scenes.ToArray()));

        var opts = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = OutDir + "/index.html",
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;
        Debug.Log(string.Format("[BuildWebGL] 结果={0} 大小={1:F1}MB 耗时={2} 错误={3}",
            summary.result, summary.totalSize / 1048576f, summary.totalTime, summary.totalErrors));

        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
