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
            var fogView = Object.FindFirstObjectByType<FogView>();
            var floatText = Object.FindFirstObjectByType<FloatingTextView>();

            // 首帧预热:edit 模式下第一次 Camera.Render() 会产出纯色废帧(URP shader 未就绪,
            // 实测 t10 全屏 (92,195,255) 纯青),先渲染一帧弃用
            var warmup = new RenderTexture(64, 64, 24);
            cam.targetTexture = warmup;
            cam.Render();
            cam.targetTexture = null;            // 2026-09-10 重大修复:edit 模式下 URP Volume 后处理默认不参与 Camera.Render()
            // (实测 Bloom/ACES/ColorAdjustments 全部无效,saturation=100 无变化)。
            // 强制初始化 Volume 管理器并更新全局 Volume,让批处理截图真实反映后处理效果。
            // URP Volume 在 edit mode 的激活:Initialize 会重置 VolumeManager 状态栈,
            // 随后首帧 Camera.Render 即按全局 Volume 应用后处理
            UnityEngine.Rendering.VolumeManager.instance.Initialize();
            Object.DestroyImmediate(warmup);

            for (int i = 0; i < Marks.Length; i++)
            {
                long targetTick = (long)(Marks[i] * boot.Sim.Config.TickRate);
                int guard = 0;
                while (boot.Sim.Tick < targetTick && guard++ < 100000) boot.Sim.Step();
                boot.Board.TickViews();
                if (fogView != null) fogView.RefreshNow();
                // 视图平滑收敛 + 持续交战(edit 模式 Time.deltaTime≈0,手工喂 dt;
                // 继续步进 5 tick 让 DamageDealt 在镜头前新鲜发生——飘字寿命 1s,停机渲染会错过)
                for (int k = 0; k < 5; k++)
                {
                    boot.Sim.Step();
                    boot.Board.TickViews();
                    if (fogView != null) fogView.RefreshNow();
                    foreach (var v in Object.FindObjectsOfType<EntityView>())
                        v.Tick(0.1f, cam);
                    if (floatText != null) floatText.RenderFrame(0.1f); // 飘字:同一收敛口径喂 dt
                }

                // 战线跟随:优先对准交战单位(主将>士兵);无交战则固定机位
                var focus = FindBattleFocus(boot.Sim);
                float camX = focus?.X ?? CamX[i];
                float camRow = focus?.Y ?? 15f;
                cam.transform.position = new Vector3(camX, 55f, 3f - camRow - 38f);
                cam.transform.LookAt(new Vector3(camX, 0f, 3f - camRow));

                string path = string.Format("D:/DevTools/shots/m6.2_{0}_t{1}s.png", i + 1, (int)Marks[i]);
                SaveRender(cam, path);
                Debug.Log("[CaptureBatch] " + path + " simTick=" + boot.Sim.Tick + " focus=" + (focus?.ToString() ?? "fixed")
                    + " floatTexts=" + (floatText != null ? floatText.ActiveCount.ToString() : "n/a"));
            }

            Object.DestroyImmediate(go);
            Debug.Log("[CaptureBatch] done");
        }

        /// <summary>找当前交战点(Attacking 单位;主将优先,其次士兵);无交战返回 null。</summary>
        private static SimVec2? FindBattleFocus(SimCore sim)
        {
            SimVec2? soldier = null;
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Unit || e.State != EntityState.Attacking) continue;
                if (!e.IsSoldier) return e.PosF;
                if (soldier == null) soldier = e.PosF;
            }
            return soldier;
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
