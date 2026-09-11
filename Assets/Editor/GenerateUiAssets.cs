using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// UI Toolkit 资产生成(2026-09-11 demo→成品迁移):
    /// 运行时 CreateInstance<PanelSettings> 会因缺少序列化初始化抛
    /// ArgumentOutOfRangeException(实测,并连锁打断 GameBootstrap.Awake)——
    /// 故按本项目 GenerateBuildingMats 的模式,由编辑器一次性生成两份资产:
    ///   Assets/Resources/UI/UiTheme.tss(空主题,控件样式全在 theme.uss)
    ///   Assets/Resources/UI/PanelSettings.asset(1920×1080 等比缩放+宽高折中)
    /// Unity.exe -batchmode -quit -projectPath D:/DevTools/IronCurtain
    ///   -executeMethod IronCurtain.EditorTools.GenerateUiAssets.Run
    /// </summary>
    public static class GenerateUiAssets
    {
        [MenuItem("IronCurtain/Generate UI Assets")]
        public static void Run()
        {
            Directory.CreateDirectory("Assets/Resources/UI");
            const string tssPath = "Assets/Resources/UI/UiTheme.tss";
            const string psPath = "Assets/Resources/UI/PanelSettings.asset";

            var tss = File.Exists(tssPath)
                ? AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tssPath)
                : ScriptableObject.CreateInstance<ThemeStyleSheet>();
            if (tss == null) tss = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            if (File.Exists(tssPath)) AssetDatabase.DeleteAsset(tssPath);
            AssetDatabase.CreateAsset(tss, tssPath);

            if (File.Exists(psPath)) AssetDatabase.DeleteAsset(psPath);
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.themeStyleSheet = tss;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            AssetDatabase.CreateAsset(ps, psPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log("[GenerateUiAssets] wrote " + tssPath + " + " + psPath);
        }
    }
}
