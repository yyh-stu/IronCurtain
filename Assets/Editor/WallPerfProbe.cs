using System;
using System.IO;
using System.Reflection;
using System.Text;
using Stopwatch = System.Diagnostics.Stopwatch;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 屏障特效性能探针(2026-09-11,ART-HANDOFF-v2:"概念图级氛围做之前先在三路大地图量性能——
    /// 单 draw call 的粒子系统可以,逐六边形实例化会炸")。
    /// Unity.exe -batchmode -quit -projectPath D:/DevTools/IronCurtain
    ///   -executeMethod IronCurtain.EditorTools.WallPerfProbe.Run -logFile D:/DevTools/wallperf.log
    /// 在真实三路大地图 t=120s 交战局、蓝主城机位(屏障入镜)分档测 Camera.Render+粒子模拟平均毫秒:
    /// 基线(无氛围) → 氛围 8/16/32 每秒/方 → 涟漪×10(持续重发) → 击碎×2 → 全开。
    /// 口径注意:edit 模式绝对帧时 ≠ Play FPS,结论只看**档间增量**定氛围预算。
    /// </summary>
    public static class WallPerfProbe
    {
        const int Frames = 30;
        const float Dt = 1f / 30f;

        public static void Run()
        {
            if (!File.Exists("Assets/Scenes/Main.unity")) IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");

            var go = new GameObject("PerfBoot");
            var boot = go.AddComponent<GameBootstrap>();
            typeof(GameBootstrap)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(boot, null);
            if (boot.Sim == null || Camera.main == null)
            {
                UnityEngine.Debug.LogError("[WallPerfProbe] init failed");
                EditorApplication.Exit(1);
                return;
            }
            AiSystem.Configure(boot.Sim, Side.Blue, AiTier.Normal); // 双方对打,画面有内容

            var cam = Camera.main;
            cam.aspect = 16f / 9f;
            var rig = cam.GetComponent<CameraRig>();
            if (rig != null) rig.RefreshProjection();

            long target = (long)(120 * boot.Sim.Config.TickRate);
            int guard = 0;
            while (boot.Sim.Tick < target && guard++ < 200000) boot.Sim.Step();
            boot.Board.TickViews();
            var fog = UnityEngine.Object.FindFirstObjectByType<FogView>();
            if (fog != null) fog.RefreshNow();
            rig.SetFocus(40f, 80f); // 蓝主城前方,蓝屏障入镜(特效发生地)

            // 与 CaptureBatch 同口径:预热渲染 + 后处理 Volume 激活
            UnityEngine.Rendering.VolumeManager.instance.Initialize();
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            var sb = new StringBuilder();
            sb.AppendLine("WallPerfProbe " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                + " | sim t=" + (boot.Sim.Tick / (double)boot.Sim.Config.TickRate).ToString("F0") + "s"
                + " | 机位=蓝主城(40,80) | 帧数/档=" + Frames);

            Measure(sb, cam, "基线(无氛围)", () => WallFx.SetAmbientRate(0f), null);
            Measure(sb, cam, "氛围 8/s/方", () => WallFx.SetAmbientRate(8f), null);
            Measure(sb, cam, "氛围 16/s/方(当前默认)", () => WallFx.SetAmbientRate(16f), null);
            Measure(sb, cam, "氛围 32/s/方", () => WallFx.SetAmbientRate(32f), null);
            Action rippleBurst = () =>
            {   // 每帧补 1 发,保持 ~10 发存活(涟漪命 0.4s)
                for (int k = 0; k < 3; k++)
                    boot.Sim.Emit(new SimEvent(boot.Sim.Tick, SimEventType.WallDamaged, 2, 30f, 0,
                        (int)Side.Blue, WallSystem.PanelCenterPos(Side.Blue, 2)));
            };
            Measure(sb, cam, "涟漪连发(持续,含氛围16)", () => WallFx.SetAmbientRate(16f), rippleBurst);
            Action shatterOnce = () =>
            {   // 两段一次性击碎(44 碎片/段,寿命 ~1.4s)
                WallSystem.Damage(Side.Blue, 1, 9999f);
                boot.Sim.Emit(new SimEvent(boot.Sim.Tick, SimEventType.WallBroken, 1, 0f, 0, (int)Side.Blue));
                WallSystem.Damage(Side.Blue, 3, 9999f);
                boot.Sim.Emit(new SimEvent(boot.Sim.Tick, SimEventType.WallBroken, 3, 0f, 0, (int)Side.Blue));
            };
            bool shattered = false;
            Measure(sb, cam, "击碎×2(一次性,含氛围16)", () => WallFx.SetAmbientRate(16f),
                () => { if (!shattered) { shatterOnce(); shattered = true; } });
            Measure(sb, cam, "全开(氛围32+涟漪连发)", () => WallFx.SetAmbientRate(32f), rippleBurst);

            File.WriteAllText("D:/DevTools/shots/wallperf_report.txt", sb.ToString());
            UnityEngine.Debug.Log("[WallPerfProbe] report:\n" + sb);
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Debug.Log("[WallPerfProbe] done");
        }

        /// <summary>一档:setup 一次,可选 perFrame 每帧调用(维持特效存活),预热 3 帧后计均值。</summary>
        private static void Measure(StringBuilder sb, Camera cam, string label, Action setup, Action perFrame)
        {
            setup();
            for (int i = 0; i < 3; i++)
            {
                if (perFrame != null) perFrame();
                WallFx.RenderFrameAll(Dt);
                cam.Render();
            }
            var sw = Stopwatch.StartNew();
            int particles = 0;
            for (int i = 0; i < Frames; i++)
            {
                if (perFrame != null) perFrame();
                WallFx.RenderFrameAll(Dt);
                cam.Render();
                particles = Math.Max(particles, AliveParticles());
            }
            sw.Stop();
            double avg = sw.Elapsed.TotalMilliseconds / Frames;
            sb.AppendLine(string.Format("{0,-24} avg={1,7:F2} ms/帧  峰值存活粒子={2}", label, avg, particles));
            UnityEngine.Debug.Log("[WallPerfProbe] " + label + " avg=" + avg.ToString("F2") + "ms particles=" + particles);
        }

        private static int AliveParticles()
        {
            int n = 0;
            foreach (var ps in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
                n += ps.particleCount;
            return n;
        }
    }
}
