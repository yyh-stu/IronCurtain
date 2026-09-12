using System.IO;
using System.Reflection;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // 2026-09-12 握持标定扫描:武器纵轴会被对齐到 socket 帧的 +Y,故只需扫 6 个手骨局部帧
    // (±X/±Y/±Z),每个英雄各拍一张小图,拼图后由人眼(原生多模态)挑出最像"握在手里"的那档。
    // 用法:Unity.exe -batchmode -projectPath <proj> -executeMethod IronCurtain.EditorTools.WeaponTuneBatch.Run
    public static class WeaponTuneBatch
    {
        private static readonly HeroId[] Heroes =
            { HeroId.ZhenYue, HeroId.PanShi, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
        private static readonly string[] Names = { "ZhenYue", "DuanFeng", "ZhuFeng", "XingLuo" };
        private static readonly Vector3[] Frames =
        {
            new Vector3(0f, 0f, 0f),      // 手骨 +Y
            new Vector3(180f, 0f, 0f),    // -Y
            new Vector3(0f, 0f, 90f),     // -X
            new Vector3(0f, 0f, -90f),    // +X
            new Vector3(90f, 0f, 0f),     // +Z
            new Vector3(-90f, 0f, 0f),    // -Z
        };
        private static readonly float[] Grips = { 0.25f, 0.50f, 0.75f };
        /// <summary>候选档(frame 索引 + 握点比例)。每档新建一次视图渲染(已验证稳定的形态)。</summary>
        private static readonly (int f, float grip)[] Cands =
        {
            (4, 0.25f), (4, 0.75f),          // +Z:近端/远端各一
            (5, 0.25f), (5, 0.75f),          // -Z
            (2, 0.75f), (3, 0.75f),          // ∓X
            (0, 0.50f), (1, 0.50f),          // ±Y
        };
        private static readonly HeroId[] TuneSet =
            { HeroId.ZhenYue, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.XingLuo };
        private const string OutDir = "D:/DevTools/shots/wpntune";

        public static void Run()
        {
            if (!File.Exists("Assets/Scenes/Main.unity")) IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var bootGo = new GameObject("TuneBoot");
            var boot = bootGo.AddComponent<GameBootstrap>();
            typeof(GameBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(boot, null);
            if (Camera.main == null) { Debug.LogError("[wpntune] no camera"); EditorApplication.Exit(1); return; }
            var cam = Camera.main;
            Directory.CreateDirectory(OutDir);

            var warm = new RenderTexture(64, 64, 24);
            cam.targetTexture = warm; cam.Render(); Object.DestroyImmediate(warm); cam.targetTexture = null;

            for (int h = 0; h < TuneSet.Length; h++)
            {
                // IC_WPN_HERO=<Name> 只跑一个英雄(一次性跑满 4 人 × 18 档会把编辑器压崩)
                string only = System.Environment.GetEnvironmentVariable("IC_WPN_HERO");
                if (!string.IsNullOrEmpty(only) && Names[h] != only) continue;
                // IC_WPN_VERIFY=1:不扫描,只按当前配置表各拍一张(用于标定后复验)
                bool verify = System.Environment.GetEnvironmentVariable("IC_WPN_VERIFY") == "1";
                int frames = verify ? 1 : Frames.Length;
                int grips = verify ? 1 : Grips.Length;
                for (int c = 0; c < (verify ? 1 : Cands.Length); c++)
                {
                    EntityView.WeaponAimOverride = verify ? (Vector3?)null : Frames[Cands[c].f];
                    EntityView.WeaponGripOverride = verify ? -1f : Cands[c].grip;
                    var e = new Entity(91000 + h, Side.Blue, EntityKind.Unit, new GridPos(2, 1), 100f)
                    {
                        Hero = TuneSet[h], State = EntityState.Idle,
                        PosF = new SimVec2(20f, 8f), MaxHp = 100f,
                    };
                    var go = new GameObject("Tune_" + Names[h]);
                    var view = go.AddComponent<EntityView>();
                    view.Bind(e, Side.Blue, TuneSet[h], null);
                    view.Tick(0.05f, cam);
                    var p = BoardView.WorldPos(e.PosF);
                    cam.transform.position = p + new Vector3(-3.4f, 2.9f, -4.6f);
                    cam.transform.LookAt(p + Vector3.up * 1.45f);
                    Save(cam, string.Format("{0}/{1}_{2}.png", OutDir, Names[h], c));
                    Object.DestroyImmediate(go);
                }
                Debug.Log("[wpntune] " + Names[h] + " done");
            }
            EntityView.WeaponAimOverride = null;
            EntityView.WeaponGripOverride = -1f;
            foreach (var v in Object.FindObjectsByType<EntityView>(FindObjectsSortMode.None))
                Object.DestroyImmediate(v.gameObject);
            Debug.Log("[wpntune] all done -> " + OutDir);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Save(Camera cam, string path)
        {
            var rt = new RenderTexture(720, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
            Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
        }
    }
}
