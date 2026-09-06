using System.IO;
using System.Reflection;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // M6 视觉验收·后台离屏截图(不需要窗口/焦点/热键):
    //   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IronCurtain.EditorTools.CaptureBatch.Run
    // 原理:edit 模式下反射调用 GameBootstrap.Awake 建全场景(Sim/相机/棋盘),
    // 手动步进 Sim 到 10/30/60/120s,相机按位 Camera.Render 到 RenderTexture 存 PNG。
    public static class CaptureBatch
    {
        private static readonly double[] Marks = { 10, 30, 60, 120 };
        private static readonly float[] CamX = { 40f, 100f, 143f, 200f };

        public static void Run()
        {
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");

            var go = new GameObject("CaptureBoot");
            var boot = go.AddComponent<GameBootstrap>();
            typeof(GameBootstrap)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(boot, null);
            if (boot.Sim == null || Camera.main == null)
            {
                Debug.LogError("[CaptureBatch] init failed: Sim=" + (boot.Sim == null) + " cam=" + (Camera.main == null));
                EditorApplication.Exit(1);
                return;
            }

            AiSystem.Configure(boot.Sim, Side.Blue, AiTier.Normal); // 双方对打,画面有内容
            Directory.CreateDirectory("D:/DevTools/shots");
            var cam = Camera.main;

            for (int i = 0; i < Marks.Length; i++)
            {
                long targetTick = (long)(Marks[i] * boot.Sim.Config.TickRate);
                int guard = 0;
                while (boot.Sim.Tick < targetTick && guard++ < 100000) boot.Sim.Step();
                boot.Board.TickViews();
                // 视图平滑收敛(edit 模式 Time.deltaTime≈0,手工喂 dt)
                for (int k = 0; k < 5; k++)
                    foreach (var v in Object.FindObjectsOfType<EntityView>())
                        v.Tick(0.1f, cam);

                cam.transform.position = new Vector3(CamX[i], 55f, -50f);
                cam.transform.LookAt(new Vector3(CamX[i], 0f, -12f));

                string path = string.Format("D:/DevTools/shots/m6.6_{0}_t{1}s.png", i + 1, (int)Marks[i]);
                SaveRender(cam, path);
                Debug.Log("[CaptureBatch] " + path + " simTick=" + boot.Sim.Tick);
            }

            Object.DestroyImmediate(go);
            Debug.Log("[CaptureBatch] done");
        }

        private static void SaveRender(Camera cam, string path)
        {
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
