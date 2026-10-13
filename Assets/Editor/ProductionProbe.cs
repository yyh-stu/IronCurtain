using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 2026-09-13 新增:**生产将的自检探针**(制作人:"不管什么将领生产出来都是磐石")。
    /// 逐英雄入队 → 跑 Sim 到出将 → 打印该单位的 Hero/IsSoldier/GeneralId,
    /// 再给这个主将建一个 EntityView,打印它实际加载的模型层级名 ——
    /// 一眼分清是"实体属性错了"还是"视图选错了模型"。
    ///
    /// 用法:Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.ProductionProbe.Run -logFile &lt;log&gt;
    /// </summary>
    public static class ProductionProbe
    {
        public static void Run()
        {
            var bootGo = new GameObject("ProbeBoot");
            var boot = bootGo.AddComponent<GameBootstrap>();
            typeof(GameBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(boot, null);
            var sim = boot.Sim;
            if (sim == null) { Debug.LogError("[probe] no sim"); EditorApplication.Exit(1); return; }

            // 己方兵营
            int barracks = 0;
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e != null && e.Kind == EntityKind.Building && e.Building == BuildingId.Base
                    && e.Side == AppFlow.PlayerSide) { barracks = e.Id; break; }
            }
            Debug.Log("[probe] base=" + barracks + " player=" + AppFlow.PlayerSide);

            var heroes = new[] { HeroId.PanShi, HeroId.ZhenYue, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
            var before = new HashSet<int>();
            foreach (var kv in sim.Entities) before.Add(kv.Key);

            foreach (var h in heroes)
            {
                string err;
                bool ok = ProductionSystem.Enqueue(sim, barracks, h, out err);
                Debug.Log("[probe] 入队 " + h + " ok=" + ok + " err=" + (err ?? "-"));
            }

            // 跑 Sim 直到出将(最多 60 秒仿真)
            for (int i = 0; i < 60 * sim.Config.TickRate; i++) sim.Step();

            Debug.Log("[probe] 新增单位:");
            int shown = 0;
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e == null || before.Contains(e.Id)) continue;
                Debug.Log("[probe]   id=" + e.Id + " soldier=" + e.IsSoldier + " hero=" + (e.Hero.HasValue ? e.Hero.Value.ToString() : "null")
                    + " general=" + e.GeneralId);
                shown++;
            }
            if (shown == 0) Debug.Log("[probe]   (无新增单位 —— 生产没出将)");

            // 给第一个新增的主将建视图,看它加载了什么模型
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e == null || before.Contains(e.Id) || e.IsSoldier) continue;
                var go = new GameObject("ProbeView");
                var view = go.AddComponent<EntityView>();
                view.Bind(e, e.Side, e.Hero, null);
                var names = new List<string>();
                foreach (var t in go.GetComponentsInChildren<Transform>()) names.Add(t.name);
                Debug.Log("[probe] 主将 hero=" + (e.Hero.HasValue ? e.Hero.Value.ToString() : "null")
                    + " 视图层级=" + string.Join("/", names.ToArray()));
                Object.DestroyImmediate(go);
                break;
            }

            // 2026-09-13 加:把**每个**新生产的主将连同其兵团拍下来 —— 光看属性不够,要能看图。
            Directory.CreateDirectory("D:/DevTools/shots");
            var cam = Camera.main;
            if (cam != null)
            {
                var warm = new RenderTexture(64, 64, 24);
                cam.targetTexture = warm; cam.Render(); Object.DestroyImmediate(warm); cam.targetTexture = null;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || before.Contains(e.Id) || e.IsSoldier) continue;
                    var center = BoardView.WorldPos(e.PosF);
                    cam.transform.position = center + new Vector3(-7f, 6f, -13f);
                    cam.transform.LookAt(center + Vector3.up * 1.6f);
                    var rt = new RenderTexture(1400, 1000, 24, RenderTextureFormat.ARGB32);
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    var path = "D:/DevTools/shots/prod_" + (e.Hero.HasValue ? e.Hero.Value.ToString() : "null") + "_" + e.Id + ".png";
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    RenderTexture.active = null; cam.targetTexture = null;
                    Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
                    Debug.Log("[probe] 拍照 " + path);
                }
            }

            Object.DestroyImmediate(bootGo);
            Debug.Log("[probe] done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
