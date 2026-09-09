using System.IO;
using System.Reflection;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // 2026-09-10 英雄阵容特写:六英雄带武器分离件+逐风骑乘,绑定姿势双机位渲染。
    // 用途:武器挂接/骑乘/身高校准验收(不依赖 AI 产将,Sim 走真实 boot 保灯光地形)。
    // 用法:菜单 IronCurtain/Hero Lineup Capture,或批处理 -executeMethod ...HeroLineupBatch.Run
    public static class HeroLineupBatch
    {
        private static readonly HeroId[] Lineup =
            { HeroId.ZhenYue, HeroId.PanShi, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };

        public static void Run()
        {
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");

            var bootGo = new GameObject("LineupBoot");
            var boot = bootGo.AddComponent<GameBootstrap>();
            typeof(GameBootstrap)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(boot, null);
            if (Camera.main == null)
            {
                Debug.LogError("[lineup] no camera");
                EditorApplication.Exit(1);
                return;
            }
            var cam = Camera.main;
            Directory.CreateDirectory("D:/DevTools/shots");

            // 手工摆六英雄(不动 Sim 状态;Pos 只喂视图)
            var views = new System.Collections.Generic.List<EntityView>();
            for (int i = 0; i < Lineup.Length; i++)
            {
                var e = new Entity(90000 + i, Side.Blue, EntityKind.Unit, new GridPos(2, 1), 100f)
                {
                    Hero = Lineup[i],
                    State = EntityState.Idle,
                    PosF = new SimVec2(14f + i * 5f, 1.5f),
                    MaxHp = 100f,
                };
                var go = new GameObject("Lineup_" + Lineup[i]);
                go.transform.position = BoardView.WorldPos(e.PosF);
                go.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
                var view = go.AddComponent<EntityView>();
                view.Bind(e, Side.Blue, Lineup[i], null);
                view.Tick(0.1f, cam);
                views.Add(view);
            }

            // 首帧预热(edit 模式第一次 Camera.Render 为废帧,同 CaptureBatch)
            var warm = new RenderTexture(64, 64, 24);
            cam.targetTexture = warm;
            cam.Render();
            Object.DestroyImmediate(warm);
            cam.targetTexture = null;

            // 机位1:全阵容斜侧
            var c0 = BoardView.WorldPos(new SimVec2(14f + 2.5f * 5f, 1.5f));
            cam.transform.position = c0 + new Vector3(-6f, 5.5f, -13f);
            cam.transform.LookAt(c0 + Vector3.up * 1.4f);
            Save(cam, "D:/DevTools/shots/lineup_all.png");

            // 机位2:逐风骑乘特写
            var cz = BoardView.WorldPos(new SimVec2(14f + 3 * 5f, 1.5f));
            cam.transform.position = cz + new Vector3(-5f, 3.4f, -8f);
            cam.transform.LookAt(cz + Vector3.up * 1.2f);
            Save(cam, "D:/DevTools/shots/lineup_zhufeng.png");

            foreach (var v in views) Object.DestroyImmediate(v.gameObject);
            Object.DestroyImmediate(bootGo);
            Debug.Log("[lineup] done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Save(Camera cam, string path)
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
            Debug.Log("[lineup] " + path);
        }
    }
}
