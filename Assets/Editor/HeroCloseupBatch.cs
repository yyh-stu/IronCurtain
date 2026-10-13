using System.IO;
using System.Reflection;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 2026-09-13 新增:英雄武器握持**多机位特写**抓帧(编辑模式,不需要 Play,自退进程)。
    /// 存在的理由:武器握持(指向/握点/面朝向/弓弦朝向)一直是纯视觉问题,必须把模型渲成图去看。
    ///
    /// 取景按**模型实际包围盒**算 —— 各英雄模型尺度差异很大,写死高度会拍到脚(第一版就是这么错的)。
    /// 每个英雄:4 个正交方向全身 + 1 张手部近景;文件名带该英雄实际 yaw,便于换算基准朝向。
    ///
    /// 用法:
    ///   Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.HeroCloseupBatch.Run -logFile &lt;log&gt;
    /// 产物:D:/DevTools/shots/cu_&lt;英雄&gt;_&lt;0..3&gt;_yaw&lt;朝向&gt;.png 与 cu_&lt;英雄&gt;_hands.png
    /// </summary>
    public static class HeroCloseupBatch
    {
        private static readonly HeroId[] Lineup =
            { HeroId.ZhenYue, HeroId.PanShi, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };

        private const string OutDir = "D:/DevTools/shots";

        public static void Run()
        {
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");

            var bootGo = new GameObject("CloseupBoot");
            var boot = bootGo.AddComponent<GameBootstrap>();
            typeof(GameBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(boot, null);
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[closeup] no camera");
                EditorApplication.Exit(1);
                return;
            }
            Directory.CreateDirectory(OutDir);

            // 首帧预热(edit 模式第一次 Camera.Render 为废帧,同 CaptureBatch/HeroLineupBatch)
            var warm = new RenderTexture(64, 64, 24);
            cam.targetTexture = warm;
            cam.Render();
            Object.DestroyImmediate(warm);
            cam.targetTexture = null;

            foreach (var hero in Lineup)
            {
                var e = new Entity(90000, Side.Blue, EntityKind.Unit, new GridPos(2, 1), 100f)
                {
                    Hero = hero,
                    State = EntityState.Idle,
                    PosF = new SimVec2(20f, 20f),
                    MaxHp = 100f,
                };
                var go = new GameObject("CU_" + hero);
                go.transform.position = BoardView.WorldPos(e.PosF);
                var view = go.AddComponent<EntityView>();
                view.Bind(e, Side.Blue, hero, null);
                view.Tick(0.1f, cam);

                int yaw = Mathf.RoundToInt(go.transform.eulerAngles.y);

                // 全身包围盒(含武器):取景与近景锚点都按它算
                Bounds wb = new Bounds(); bool any = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r == null) continue;
                    if (!any) { wb = r.bounds; any = true; } else wb.Encapsulate(r.bounds);
                }
                if (!any) { wb = new Bounds(go.transform.position + Vector3.up * 1f, Vector3.one * 2f); }
                float h = Mathf.Max(0.6f, wb.size.y);
                Vector3 mid = wb.center;

                for (int a = 0; a < 4; a++)
                {
                    var dir = Quaternion.Euler(0f, a * 90f, 0f) * Vector3.forward;
                    cam.transform.position = mid + dir * (h * 1.35f) + Vector3.up * (h * 0.10f);
                    cam.transform.LookAt(mid);
                    Save(cam, OutDir + "/cu_" + hero + "_" + a + "_yaw" + yaw + ".png");
                }

                // 手部近景:上身 60% 高度处,斜前方平视(判握点落在兵器哪一段)
                var hands = new Vector3(mid.x, wb.min.y + h * 0.60f, mid.z);
                cam.transform.position = hands + new Vector3(0.72f, 0.10f, 0.72f) * h;
                cam.transform.LookAt(hands);
                Save(cam, OutDir + "/cu_" + hero + "_hands.png");

                Object.DestroyImmediate(go);
            }

            Object.DestroyImmediate(bootGo);
            Debug.Log("[closeup] done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Save(Camera cam, string path)
        {
            var rt = new RenderTexture(1600, 1200, 24, RenderTextureFormat.ARGB32);
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
            Debug.Log("[closeup] " + path);
        }

        /// <summary>
        /// 2026-09-13 加入:建筑取景,专门验证**旗帜落点**(制作人截图里旗子立在建筑旁边地面上)。
        /// 用法:-executeMethod IronCurtain.EditorTools.HeroCloseupBatch.RunBuildings
        /// </summary>
        public static void RunBuildings()
        {
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");

            var bootGo = new GameObject("BldBoot");
            var boot = bootGo.AddComponent<GameBootstrap>();
            typeof(GameBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(boot, null);
            var cam = Camera.main;
            if (cam == null) { Debug.LogError("[bld] no camera"); EditorApplication.Exit(1); return; }
            Directory.CreateDirectory(OutDir);

            var warm = new RenderTexture(64, 64, 24);
            cam.targetTexture = warm;
            cam.Render();
            Object.DestroyImmediate(warm);
            cam.targetTexture = null;

            var kinds = new[] { BuildingId.Base, BuildingId.Barracks, BuildingId.Tower, BuildingId.ResourcePoint };
            for (int i = 0; i < kinds.Length; i++)
            {
                var e = new Entity(91000 + i, Side.Blue, EntityKind.Building,
                    new GridPos(2 + i * 6, 10), 200f) { Building = kinds[i], MaxHp = 200f };
                var go = new GameObject("Bld_" + kinds[i]);
                go.transform.position = BoardView.WorldPos(new SimVec2(20f + i * 6f, 20f));
                var view = go.AddComponent<EntityView>();
                view.Bind(e, Side.Blue, null, kinds[i]);
                view.Tick(0.1f, cam);

                Bounds wb = new Bounds(); bool any = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r == null) continue;
                    // 旗子单独拍一张"只有旗"的特写,所以要能区分:整体包围盒要含旗
                    if (!any) { wb = r.bounds; any = true; } else wb.Encapsulate(r.bounds);
                }
                float h = any ? Mathf.Max(2f, wb.size.y) : 8f;
                Vector3 mid = any ? wb.center : go.transform.position + Vector3.up * 4f;
                // 取景拉远并把视线抬高一点:包围盒含旗杆,原来的 1.25h 会把旗布裁在画外
                var dir = Quaternion.Euler(0f, 40f, 0f) * Vector3.forward;
                cam.transform.position = mid + dir * (h * 1.95f) + Vector3.up * (h * 0.02f);
                cam.transform.LookAt(mid + Vector3.up * (h * 0.10f));
                Save(cam, OutDir + "/bld_" + kinds[i] + ".png");
                Debug.Log("[bld] " + kinds[i] + " worldTop=" + (wb.max.y - go.transform.position.y).ToString("F2")
                    + " size=" + wb.size.ToString("F2"));
                Object.DestroyImmediate(go);
            }

            Object.DestroyImmediate(bootGo);
            Debug.Log("[bld] done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
