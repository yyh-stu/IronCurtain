using System.IO;
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
    }
}
