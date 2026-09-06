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
    }
}
