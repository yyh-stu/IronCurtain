using System.IO;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // M6 场景工具:一键创建主场景(空场景 + GameBootstrap),并加入 Build Settings
    public static class IronCurtainSceneTools
    {
        [MenuItem("IronCurtain/Create Main Scene %#m")]
        public static void CreateMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Game");
            go.AddComponent<GameBootstrap>();

            Directory.CreateDirectory("Assets/Scenes");
            string path = "Assets/Scenes/Main.unity";
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            AssetDatabase.Refresh();
            Debug.Log("[IronCurtain] Main scene created at " + path);
        }

        [MenuItem("IronCurtain/Open Main Scene")]
        public static void OpenMainScene()
        {
            if (File.Exists("Assets/Scenes/Main.unity"))
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            else
                CreateMainScene();
        }

        // 2026-09-13 BP 选将场景(BpBootstrap),Build Settings 排在 Menu 之后、Main 之前。
        [MenuItem("IronCurtain/Create BP Scene")]
        public static void CreateBpScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("BP");
            go.AddComponent<BpBootstrap>();

            Directory.CreateDirectory("Assets/Scenes");
            string path = "Assets/Scenes/BP.unity";
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Menu.unity", true),
                new EditorBuildSettingsScene(path, true),
                new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true),
            };
            AssetDatabase.Refresh();
            Debug.Log("[IronCurtain] BP scene created at " + path);
        }

        [MenuItem("IronCurtain/Open BP Scene")]
        public static void OpenBpScene()
        {
            if (File.Exists("Assets/Scenes/BP.unity"))
                EditorSceneManager.OpenScene("Assets/Scenes/BP.unity");
            else
                CreateBpScene();
        }

        // 2026-09-12 主界面:菜单场景(MenuBootstrap),Build Settings 索引 0 作为游戏入口。
        [MenuItem("IronCurtain/Create Menu Scene")]
        public static void CreateMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Menu");
            go.AddComponent<MenuBootstrap>();

            Directory.CreateDirectory("Assets/Scenes");
            string path = "Assets/Scenes/Menu.unity";
            EditorSceneManager.SaveScene(scene, path);
            var main = new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true), main };
            AssetDatabase.Refresh();
            Debug.Log("[IronCurtain] Menu scene created at " + path);
        }

        [MenuItem("IronCurtain/Open Menu Scene")]
        public static void OpenMenuScene()
        {
            if (File.Exists("Assets/Scenes/Menu.unity"))
                EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");
            else
                CreateMenuScene();
        }
    }
}
