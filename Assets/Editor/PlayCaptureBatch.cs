using System.IO;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace IronCurtain.EditorTools
{
    // ART-P0 美术验收·Play 模式截图通道(URP 后处理生效的唯一通道):
    //   Unity.exe -batchmode -projectPath <proj> -executeMethod IronCurtain.EditorTools.PlayCaptureBatch.Run -logFile <log>
    // 环境变量 IC_ART_ROUND=<tag> 决定产物前缀(art_<tag>_play_*/art_<tag>_playui_*);
    // 拍摄与退 Play 由场景内 CaptureRunner.RunArtCapture 负责,文件名与 CaptureBatch 的 art 模式一一对应。
    // batchmode 下 Play 结束后编辑器不会自行退出(退出机制与 PlayAndCapture 同,由外部按产物判定后收进程)。
    public static class PlayCaptureBatch
    {
        [MenuItem("IronCurtain/Play Capture Batch %#&a")]
        public static void Run()
        {
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory("D:/DevTools/shots");
            SessionState.SetBool("ic.capture", false);
            SessionState.SetBool("ic.fastresult", false);
            SessionState.SetBool("ic.inputaudit", false);
            SessionState.SetBool("ic.playcap", true);
            EditorApplication.EnterPlaymode();
        }

        // 结算+驻军 tooltip 取证(2026-09-12):需在进程环境里带 IC_UI_RESULT=1 IC_UI_TIP=1。
        // Unity.exe -projectPath <proj> -executeMethod IronCurtain.EditorTools.PlayCaptureBatch.ResultCapture -logFile <log>
        [MenuItem("IronCurtain/Result Capture Batch %#&r")]
        public static void ResultCapture()
        {
            EnsureMenuScene();
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory("D:/DevTools/shots");
            SessionState.SetBool("ic.capture", false);
            SessionState.SetBool("ic.fastresult", false);
            SessionState.SetBool("ic.playcap", false);
            SessionState.SetInt("ic.mode", -1);
            SessionState.SetBool("ic.resultcap", true);
            EditorApplication.EnterPlaymode();
        }

        // 主界面取证(2026-09-12):开菜单场景拍默认英雄+逐风骑乘两张。
        // Unity.exe -projectPath <proj> -executeMethod IronCurtain.EditorTools.PlayCaptureBatch.MenuCapture -logFile <log>
        [MenuItem("IronCurtain/Menu Capture Batch %#&k")]
        public static void MenuCapture()
        {
            EnsureMenuScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");
            Directory.CreateDirectory("D:/DevTools/shots");
            SessionState.SetBool("ic.menucap", true);
            EditorApplication.EnterPlaymode();
        }

        // 练习场自动验证(2026-09-12):练习模式蓝方帧→真实换边重载→红方帧,验证无AI+HUD+换边全链。
        // Unity.exe -projectPath <proj> -executeMethod IronCurtain.EditorTools.PlayCaptureBatch.PracticeCapture -logFile <log>
        [MenuItem("IronCurtain/Practice Capture Batch %#&v")]
        public static void PracticeCapture()
        {
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory("D:/DevTools/shots");
            AppFlow.Current = AppFlow.Mode.Practice;   // 练习场:双方无 AI
            AppFlow.PlayerSide = IronCurtain.Sim.Side.Blue;
            SessionState.SetInt("ic.mode", 3);         // 域重载会清静态,经 SessionState 传递
            SessionState.SetInt("ic.pracapPhase", 0);
            SessionState.SetBool("ic.pracap", true);
            EditorApplication.EnterPlaymode();
        }


        // BP 选将取证(2026-09-13):进 BP 场景拍 选将中/揭示/完成槽位 三张(带 IC_BPCAP)。
        // Unity.exe -projectPath <proj> -executeMethod IronCurtain.EditorTools.PlayCaptureBatch.BpCapture -logFile <log>
        [MenuItem("IronCurtain/BP Capture Batch %#&b")]
        public static void BpCapture()
        {
            if (!File.Exists("Assets/Scenes/BP.unity")) IronCurtainSceneTools.CreateBpScene();
            EditorSceneManager.OpenScene("Assets/Scenes/BP.unity");
            Directory.CreateDirectory("D:/DevTools/shots");
            SessionState.SetBool("ic.bpcap", true);
            EditorApplication.EnterPlaymode();
        }

        private static void EnsureMenuScene()
        {
            if (!File.Exists("Assets/Scenes/Menu.unity")) IronCurtainSceneTools.CreateMenuScene();
        }
    }
}
