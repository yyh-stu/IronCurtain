using System.IO;
using System.Reflection;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // 2026-09-12 结算英雄卡头像:用工程内既有英雄模型渲一张头肩像(前排三将+产将通用),
    // 背景取结算面板底色,卡片里直接贴图无需抠像。产物 Assets/Resources/UI/portraits/{HeroId}.png。
    // 用法:Unity.exe -batchmode -projectPath <proj> -executeMethod IronCurtain.EditorTools.HeroPortraitBatch.Run
    public static class HeroPortraitBatch
    {
        private static readonly HeroId[] Heroes =
            { HeroId.ZhenYue, HeroId.PanShi, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
        private const string OutDir = "Assets/Resources/UI/portraits";
        private static readonly Color CardBg = new Color(0.086f, 0.102f, 0.133f, 1f);   // 同结算卡底色

        public static void Run()
        {
            if (!File.Exists("Assets/Scenes/Main.unity")) IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory(OutDir);
            var bootGo = new GameObject("PortraitBoot");
            var boot = bootGo.AddComponent<GameBootstrap>();
            typeof(GameBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(boot, null);
            if (Camera.main == null) { Debug.LogError("[portrait] no camera"); EditorApplication.Exit(1); return; }
            var cam = Camera.main;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = CardBg;
            cam.fieldOfView = 30f;

            var warm = new RenderTexture(64, 64, 24);
            cam.targetTexture = warm; cam.Render(); Object.DestroyImmediate(warm); cam.targetTexture = null;

            for (int i = 0; i < Heroes.Length; i++)
            {
                var e = new Entity(92000 + i, Side.Blue, EntityKind.Unit, new GridPos(2, 1), 100f)
                {
                    Hero = Heroes[i], State = EntityState.Idle,
                    PosF = new SimVec2(20f, 8f), MaxHp = 100f,
                };
                var go = new GameObject("Portrait_" + Heroes[i]);
                var view = go.AddComponent<EntityView>();
                view.Bind(e, Side.Blue, Heroes[i], null);
                view.Tick(0.05f, cam);
                var p = BoardView.WorldPos(e.PosF);
                // 按模型实际包围盒定机位(与尺寸解耦,避免距离/缩放估算错):
                // 取"顶部往下 1/5 身高"为头肩中心,机位距离 = 1.6×身高,略偏侧前方
                var rr = go.GetComponentsInChildren<Renderer>();
                Bounds b = new Bounds(p + Vector3.up, Vector3.one);
                bool has = false;
                foreach (var r in rr)
                {
                    // 只用蒙皮网格(英雄本体)量取景:武器是 MeshRenderer、血条是额外物件,都会把目标框带偏
                    if (!(r is SkinnedMeshRenderer)) continue;
                    if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
                }
                float h = Mathf.Max(0.5f, b.size.y);
                var head = new Vector3(b.center.x, b.max.y - h * 0.14f, b.center.z);
                float dist = h * 1.15f;
                cam.transform.position = head + new Vector3(dist * 0.26f, dist * 0.06f, -dist);
                cam.transform.LookAt(head);
                cam.fieldOfView = 32f;
                Save(cam, string.Format("{0}/{1}.png", OutDir, Heroes[i]));
                Object.DestroyImmediate(go);
                Debug.Log("[portrait] " + Heroes[i]);
            }
            AssetDatabase.Refresh();
            Debug.Log("[portrait] done -> " + OutDir);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Save(Camera cam, string path)
        {
            var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
            Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
        }
    }
}
