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
            SessionState.SetBool("ic.inputaudit", false);
            SessionState.SetBool("ic.fastresult", false);
            SessionState.SetBool("ic.capture", true);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("IronCurtain/Play & Fast Result")]
        public static void FastResult()
        {
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory("D:/DevTools/shots");
            SessionState.SetBool("ic.inputaudit", false);
            SessionState.SetBool("ic.capture", false);
            SessionState.SetBool("ic.fastresult", true);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("IronCurtain/Interactive Play %#&i")]
        public static void InteractivePlay()
        {
            // Keep this Play session open for real mouse/keyboard input auditing.
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetBool("ic.capture", false);
            SessionState.SetBool("ic.fastresult", false);
            SessionState.SetBool("ic.inputaudit", true);
            SessionState.SetFloat("ic.speed", 1f);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("IronCurtain/Interactive Play 6x")]
        public static void InteractivePlayFast()
        {
            // 2026-09-09 用户要求:测试不再实等(EP 90s/抽卡窗口 10s),进 Play 自动 6x。
            // 注意:6x 下抽卡 10s 窗口≈1.7s 真实时间,窗口敏感测试用轮询≤600ms 或改 3x。
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetBool("ic.capture", false);
            SessionState.SetBool("ic.fastresult", false);
            SessionState.SetBool("ic.inputaudit", true);
            SessionState.SetFloat("ic.speed", 6f);
            EditorApplication.EnterPlaymode();
        }
    }
}
