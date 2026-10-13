using System.Collections;
using System.Reflection;
using IronCurtain.Sim;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IronCurtain.View
{
    /// <summary>
    /// 2026-09-13 新增:**把 UITK 的 UI 离屏渲染成 PNG** —— 目的是让改 UI 的人(不管是我还是别人)
    /// 能"自己看一眼"再交付。工程既有两条抓帧通道都拿不到 UI:
    ///   ① `Camera.Render` 到贴图:UITK 的屏幕层不属于相机渲染 → 图里没有 UI(实测确认);
    ///   ② `ScreenCapture.CaptureScreenshot`:本机批处理/非批处理都产不出文件(实测 0 张)。
    /// 而 `PanelSettings.targetTexture` 是 UITK 官方支持的离屏渲染路径,不依赖真实屏幕,
    /// **批处理下也能出图,不弹窗口、不打扰使用机器的人**。
    ///
    /// 分辨率说明:贴图取 1920×1080。由于 PanelSettings 参考分辨率也是 1920×1080 且 match=0.5,
    /// 在 16:9 的屏幕上(如 2560×1440)缩放后面板单位恰好也等于 1920×1080 —— 所以这张图里的
    /// **布局与实机一致**(元素位置/重叠问题会同样出现),可用于验证叠层这类问题。
    ///
    /// 触发:`PlayCaptureBatch.UiShot()` 置 SessionState "ic.uishot"。
    /// 产物:`D:/DevTools/shots/uishot_&lt;序号&gt;_t&lt;秒&gt;.png`
    /// </summary>
    public sealed class UiShotRunner : MonoBehaviour
    {
        private static readonly float[] Marks = { 3f, 8f, 14f };
        private const string OutDir = "D:/DevTools/shots";

        private void Start()
        {
#if UNITY_EDITOR
            if (SessionState.GetBool("ic.uishot", false)) StartCoroutine(Run());
#endif
        }

        private IEnumerator Run()
        {
            var sim2 = Object.FindFirstObjectByType<GameBootstrap>()?.Sim;   // 抓帧场景用
            Directory.CreateDirectory(OutDir);
            var doc = Object.FindFirstObjectByType<UIDocument>();
            if (doc == null || doc.panelSettings == null)
            {
                Debug.LogError("[uishot] 找不到 UIDocument / PanelSettings,放弃");
                yield break;
            }

            var original = doc.panelSettings;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var clone = Object.Instantiate(original);   // 用副本,别动磁盘上的资产
            clone.targetTexture = rt;                    // ★ 离屏渲染的关键一行
            doc.panelSettings = clone;
            Debug.Log("[uishot] UI 切到离屏渲染 " + rt.width + "x" + rt.height
                + " scaleMode=" + original.scaleMode);

            // 造出"交互态"再拍:默认态看不出叠层问题。这里(仅抓帧用,反射改私有字段)
            // ①选中我方全部主将 → 撤退按钮会出现;②打开我方大本营的建筑面板 → 看信息+驻军标签。
            PrepareScenario();
            ClearSelectionForShot();   // 第 1 张:未框选 → [驻军] 应灰显
            yield return null;

            for (int i = 0; i < Marks.Length; i++)
            {
                while (Time.unscaledTime < Marks[i]) yield return null;
                // 注意:`WaitForEndOfFrame` 在 **-batchmode 下不会被唤醒**,协程会就此卡住(实测踩过)。
                // 这里改成再等两帧,足够让 UIElements 本帧的布局/绘制落进离屏贴图。
                yield return null;
                yield return null;
                // 每张图打开一个命令面板,检查"中央弹框"里的版式(抓帧专用)
                var ui = UiRoot.Current;
                if (ui != null)
                {
                    ui.CloseAnyEntry();
                    if (i == 1)
                    {
                        // 第 2 张:框选军队 → [驻军] 应亮起,并真的执行一次注入
                        SelectGeneralsForShot();
                    }
                    if (i == 2)
                    {
                        // 第 3 张:不弹面板,改为"悬停敌方箭塔",拍悬停态(占领/摧毁按钮 + 驻军提示)
                        ui.CloseAnyEntry();
                        var bcp2 = Object.FindFirstObjectByType<BuildingCommandPanel>();
                        int towerId = 0;
                        foreach (var kv in sim2.Entities)
                        {
                            var e = kv.Value;
                            if (e != null && e.Kind == EntityKind.Building && e.Building == BuildingId.Tower
                                && e.Side != AppFlow.PlayerSide) { towerId = e.Id; break; }
                        }
                        if (bcp2 != null && towerId != 0) { bcp2.ForceHover(towerId); Debug.Log("[uishot] 悬停敌塔 " + towerId); }
                    }
                    else
                    {
                        int idx = i == 0 ? 3 : 0;   // 道具 / 总览
                        ui.OpenEntryAt(idx);
                    }
                    yield return null;
                    yield return null;
                }
                Save(rt, string.Format("{0}/uishot_{1}_t{2}.png", OutDir, i + 1, (int)Marks[i]));
            }

            doc.panelSettings = original;           // 还原
            Debug.Log("[uishot] done");
        }

        private System.Collections.Generic.List<int> SelectedList()
        {
            var sel = Object.FindFirstObjectByType<SelectionSystem>();
            if (sel == null) return null;
            var f = typeof(SelectionSystem).GetField("_selected",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return f != null ? f.GetValue(sel) as System.Collections.Generic.List<int> : null;
        }

        private void ClearSelectionForShot() { SelectedList()?.Clear(); }

        private void SelectGeneralsForShot()
        {
            var boot = Object.FindFirstObjectByType<GameBootstrap>();
            var list = SelectedList();
            if (boot == null || boot.Sim == null || list == null) return;
            list.Clear();
            foreach (var kv in boot.Sim.Entities)
            {
                var e = kv.Value;
                if (e != null && e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == AppFlow.PlayerSide)
                    list.Add(e.Id);
            }
            Debug.Log("[uishot] 已框选主将 " + list.Count + " 个");
        }

        /// <summary>抓帧专用:把要检查的交互态"造出来"(反射改私有,不影响游戏逻辑)。</summary>
        private void PrepareScenario()
        {
            var boot = Object.FindFirstObjectByType<GameBootstrap>();
            if (boot == null || boot.Sim == null) { Debug.LogWarning("[uishot] 无 boot/Sim,跳过场景准备"); return; }
            var sim = boot.Sim;
            var player = AppFlow.PlayerSide;
            boot.Speed = 6f;   // 加速交战,让"行军至城下→入城"能在拍摄窗口内走完(端到端验证用)

            // ① 选中我方全部主将(撤退按钮据此出现)
            var sel = Object.FindFirstObjectByType<SelectionSystem>();
            if (sel != null)
            {
                var f = typeof(SelectionSystem).GetField("_selected",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var list = f != null ? f.GetValue(sel) as System.Collections.Generic.List<int> : null;
                if (list != null)
                {
                    list.Clear();
                    foreach (var kv in sim.Entities)
                    {
                        var e = kv.Value;
                        if (e != null && e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == player)
                            list.Add(e.Id);
                    }
                    Debug.Log("[uishot] 已选中主将 " + list.Count + " 个");
                }
                else Debug.LogWarning("[uishot] 反射取 _selected 失败");
            }

            // ② 打开我方大本营的建筑面板(看建筑信息 + 驻军标签 + 入驻)
            var bcp = Object.FindFirstObjectByType<BuildingCommandPanel>();
            int baseId = 0;
            // 优先己方兵营(可驻军 GarrisonFormal=30);大本营 GarrisonFormal=0 不可驻军,不能用来验入驻
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e != null && e.Kind == EntityKind.Building && e.Side == player
                    && e.Building == BuildingId.Barracks) { baseId = e.Id; break; }
            }
            if (baseId == 0)
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e != null && e.Kind == EntityKind.Building && e.Side == player
                        && e.Building == BuildingId.Base) { baseId = e.Id; break; }
                }
            if (bcp != null && baseId != 0)
            {
                bcp.SetTarget(baseId);
                Debug.Log("[uishot] 已开建筑面板 base=" + baseId);
                bcp.ForceGarrisonForTest();   // 真执行一次入驻,拍"驻军 N"验证功能真的通
                Debug.Log("[uishot] 已尝试入驻");
            }
        }

        private static void Save(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = prev;
            Object.Destroy(tex);
            Debug.Log("[uishot] " + path);
        }
    }
}
