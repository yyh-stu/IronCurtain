using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronCurtain.EditorTools
{
    /// <summary>启动守卫(2026-09-13):无头批处理/被强杀的编辑器实例会把"上次打开的场景"写成空路径,
    /// 下次正常启动就还原成一个空场景,表现为"打开工程啥也没有"(制作人实际踩到)。
    /// 这里在编辑器启动且当前是空场景时,自动打开主界面场景(Menu),避免空工程假象。
    /// 只在"确实没有场景"时动手,不干扰任何正常打开的场景。</summary>
    [InitializeOnLoad]
    public static class DefaultSceneOnLaunch
    {
        private const string MenuScene = "Assets/Scenes/Menu.unity";

        static DefaultSceneOnLaunch()
        {
            EditorApplication.delayCall += Check;
        }

        private static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var active = SceneManager.GetActiveScene();
            bool empty = string.IsNullOrEmpty(active.path)
                         && active.rootCount == 0
                         && !active.isDirty;
            if (!empty) return;
            if (!System.IO.File.Exists(MenuScene)) return;
            EditorSceneManager.OpenScene(MenuScene);
            Debug.Log("[DefaultSceneOnLaunch] 启动时是空场景 → 已打开 " + MenuScene);
        }
    }
}
