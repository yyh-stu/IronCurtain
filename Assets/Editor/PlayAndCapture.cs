using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace IronCurtain.EditorTools
{
    // M6 视觉验收工具:进 Play 由场景内 CaptureRunner 负责截图与退出。
    // 用法: 编辑器菜单 IronCurtain/Play & Capture(Ctrl+Shift+Alt+P)
    public static class PlayAndCapture
    {
        [MenuItem("IronCurtain/Play & Capture %#&p")]
        public static void Run()
        {
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory("D:/DevTools/shots");
            SessionState.SetBool("ic.capture", true);
            EditorApplication.EnterPlaymode();
        }
    }
}
