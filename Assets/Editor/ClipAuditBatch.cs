using System.IO;
using System.Linq;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>穿模录屏取证(2026-09-12,M6.0f 遗留尾巴):六英雄×三段动画逐帧采样成 PNG 序列,
    /// 外部 Python(imageio-ffmpeg)装配 MP4 入待审核图片。编辑器批处理:
    /// Unity.exe -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.ClipAuditBatch.Run -logFile &lt;log&gt;
    /// 逐风用骑乘三段(RiderIdle/Charge/Thrust,对局实际使用的动画),其余用 HeroIdle/Walk/Attack。
    /// 相机固定正面机位,重点看武器/披风/马具与身体的穿插。</summary>
    public static class ClipAuditBatch
    {
        private const string OutDir = "D:/DevTools/shots/clipaudit";
        private const int Frames = 12;
        private static readonly HeroId[] Heroes =
            { HeroId.PanShi, HeroId.ZhenYue, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
        private static readonly string[] HeroNames = { "磐石", "镇岳", "断峰", "逐风", "坠日", "星落" };

        [MenuItem("IronCurtain/Clip Audit Batch %#&c")]
        public static void Run()
        {
            if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true);
            Directory.CreateDirectory(OutDir);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.78f, 0.98f);
            RenderSettings.ambientEquatorColor = new Color(0.74f, 0.67f, 0.54f);
            RenderSettings.ambientGroundColor = new Color(0.4f, 0.31f, 0.23f);

            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x64, 0x70, 0x7c, 0xff);
            cam.orthographic = false;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 60f;

            var stageGo = new GameObject("Stage");
            var stage = stageGo.AddComponent<MenuStage>();
            stage.Init();

            int shot = 0;
            for (int h = 0; h < Heroes.Length; h++)
            {
                stage.Show(Heroes[h]);
                var body = FindBody(stageGo);
                if (body == null) { Debug.LogError("[clipaudit] no body for " + Heroes[h]); continue; }
                string[] clips = Heroes[h] == HeroId.ZhuFeng
                    ? new[] { "RiderIdle", "RiderCharge", "RiderThrust" }
                    : new[] { "HeroIdle", "HeroWalk", "HeroAttack" };
                foreach (var cn in clips)
                {
                    var clip = LoadClip(cn);
                    if (clip == null) { Debug.LogWarning("[clipaudit] clip missing " + cn); continue; }
                    var an = body.GetComponent<Animator>();
                    if (an != null) an.enabled = false;   // AnimationMode 直接采样骨骼,关掉控制器
                    AnimationMode.StartAnimationMode();   // SampleAnimationClip 前必须进动画模式
                    try
                    {
                        for (int f = 0; f < Frames; f++)
                        {
                            AnimationMode.SampleAnimationClip(body, clip, clip.length * f / Frames);
                            FrameCamera(cam, Heroes[h]);
                            SaveFrame(cam, string.Format("{0:000}_{1}_{2}_{3:00}", ++shot, HeroNames[h], cn, f));
                        }
                    }
                    finally { AnimationMode.StopAnimationMode(); }
                    Debug.Log("[clipaudit] " + HeroNames[h] + "/" + cn + " done");
                }
            }
            Debug.Log("[clipaudit] all done frames=" + shot + " -> " + OutDir);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static GameObject FindBody(GameObject stageGo)
        {
            var tr = stageGo.transform.Find("Showcase_");
            foreach (Transform t in stageGo.transform)
                if (t.name.StartsWith("Showcase_"))
                {
                    // 骑乘时身体挂在马下;否则身体是直接子物体
                    foreach (Transform c in t)
                        if (c.name == "Body") return c.gameObject;
                    foreach (Transform c in t)   // 马在 Body 之外的分支(逐风:马是子物体,Body 挂马内)
                        foreach (Transform cc in c)
                            if (cc.name == "Body") return cc.gameObject;
                    var an = t.GetComponentInChildren<Animator>();
                    return an != null ? an.gameObject : t.gameObject;
                }
            return null;
        }

        private static AnimationClip LoadClip(string name)
        {
            var path = "Assets/Resources/Models/Heroes/Anims/" + name + ".fbx";
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault();
        }

        // 机位按英雄身高微调:磐石1.94/镇岳2.30/其余2.14(骑马看整体)
        private static void FrameCamera(Camera cam, HeroId hero)
        {
            bool mounted = hero == HeroId.ZhuFeng;
            float top = mounted ? 3.1f : (hero == HeroId.ZhenYue ? 2.4f : 2.1f);
            cam.transform.position = new Vector3(0.9f, top * 0.62f, mounted ? 4.6f : 3.9f);
            cam.transform.LookAt(new Vector3(0f, top * 0.5f, 0f));
        }

        private static void SaveFrame(Camera cam, string name)
        {
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(OutDir + "/" + name + ".png", tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
