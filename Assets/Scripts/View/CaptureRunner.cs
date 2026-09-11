using System.Collections;
using System.IO;
using IronCurtain.Sim;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IronCurtain.View
{
    // 验收截图器:由编辑器菜单(Play & Capture)设置 SessionState 触发,
    // 在 Play 模式内按 sim 时间点截图,完成后自动退出 Play。
    public sealed class CaptureRunner : MonoBehaviour
    {
        public static readonly double[] Marks = { 10, 30, 60, 120 };
        // 各截图时刻的镜头 x 中心(跟随战线推进:基地 → 中线 → 敌方区)
        public static readonly float[] CamX = { 40f, 100f, 143f, 200f };

        private void Start()
        {
#if UNITY_EDITOR
            if (SessionState.GetBool("ic.playcap", false))
            {
                StartCoroutine(RunArtCapture());
                return;
            }
            if (!SessionState.GetBool("ic.capture", false))
            {
                if (SessionState.GetBool("ic.fastresult", false))
                {
                    StartCoroutine(RunFastResult());
                    return;
                }
                // 2026-09-09 测试提速:非截图模式下若设了 ic.speed>1,进 Play 自动加速
                float sp = SessionState.GetFloat("ic.speed", 1f);
                if (sp > 1f)
                {
                    var boot = GetComponent<GameBootstrap>();
                    if (boot != null)
                    {
                        boot.Speed = sp;
                        Debug.Log("[capture] test speed " + sp + "x (ic.speed; SessionState 跨会话残留,手动 Play 前请用 Interactive Play 菜单重置)");
                    }
                }
                Destroy(this);
                return;
            }
            StartCoroutine(Run());
#endif
        }

        private IEnumerator Run()
        {
            var boot = GetComponent<GameBootstrap>();
            if (boot == null || boot.Sim == null)
            {
                Debug.LogError("[capture] GameBootstrap missing");
                yield break;
            }
            boot.Speed = 3f;
            // 验收模式:蓝方补一个简单 AI,保证截图里双方都有单位在对打
            AiSystem.Configure(boot.Sim, Side.Blue, AiTier.Hard); // 验收用普通 AI,兵种多样

            int i = 0;
            float guard = 0f;
            while (i < Marks.Length)
            {
                yield return null;
                double simSec = boot.Sim.Tick / (double)boot.Sim.Config.TickRate;
                if (simSec >= Marks[i])
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        var rig = cam.GetComponent<CameraRig>();
                        if (rig != null) rig.SetFocus(CamX[i], 15f);
                        else { cam.transform.position = new Vector3(CamX[i], 55f, -50f); cam.transform.LookAt(new Vector3(CamX[i], 0f, -12f)); }
                    }
                    string p = string.Format("D:/DevTools/shots/m6.6_{0}_t{1}s.png", i + 1, (int)Marks[i]);
                    ScreenCapture.CaptureScreenshot(p);
                    Debug.Log(string.Format("[capture] {0} at sim {1:F1}s", p, simSec));
                    i++;
                }
                guard += Time.unscaledDeltaTime;
                if (guard > 200f)
                {
                    Debug.Log("[capture] timeout");
                    break;
                }
            }
            yield return new WaitForSecondsRealtime(1.5f);
#if UNITY_EDITOR
            SessionState.SetBool("ic.capture", false);
            EditorApplication.ExitPlaymode();
#endif
        }

        // M6.4b 快速结算通道:压缩对局(sim 1min 常规+1min 加时)→ 加时/结算两张全屏截图
        private IEnumerator RunFastResult()
        {
            var boot = GetComponent<GameBootstrap>();
            if (boot == null || boot.Sim == null)
            {
                Debug.LogError("[fastresult] GameBootstrap missing");
                yield break;
            }
            boot.Sim.Game.Match.DurationMinutes = 1;   // 运行时压缩时限(MatchClockSystem 读实时值)
            boot.Sim.Game.Match.OvertimeMinutes = 1;
            boot.Speed = 6f;
            AiSystem.Configure(boot.Sim, Side.Blue, AiTier.Hard); // 保证对打有伤亡与 EP 差,面板数据非全零

            bool tookOvertime = false, tookResult = false;
            float guard = 0f;
            while (!(tookOvertime && tookResult))
            {
                yield return null;
                if (!tookOvertime && boot.Sim.Phase == MatchPhase.Overtime)
                {
                    tookOvertime = true;
                    yield return new WaitForSecondsRealtime(0.5f); // 等顶栏加时橙红倒计时稳定
                    ScreenCapture.CaptureScreenshot("D:/DevTools/shots/m6.4a_overtime.png");
                    Debug.Log("[fastresult] overtime shot");
                }
                if (!tookResult && boot.Sim.Phase == MatchPhase.Ended)
                {
                    tookResult = true;
                    yield return new WaitForSecondsRealtime(1.0f); // 等结算面板完整渲染
                    ScreenCapture.CaptureScreenshot("D:/DevTools/shots/m6.4a_result.png");
                    Debug.Log("[fastresult] result shot");
                }
                guard += Time.unscaledDeltaTime;
                if (guard > 180f) { Debug.Log("[fastresult] timeout"); break; }
            }
            yield return new WaitForSecondsRealtime(1.5f);
#if UNITY_EDITOR
            SessionState.SetBool("ic.fastresult", false);
            EditorApplication.ExitPlaymode();
#endif
        }

#if UNITY_EDITOR
        // ART-P0 美术验收通道(Play 模式,SessionState ic.playcap):
        // 为什么必须走 Play——URP Volume 后处理在 edit 模式 Camera.Render() 下架构性不生效
        // (实测 saturation=100/postExposure+3 零变化),批处理截图只能验光照/材质层。
        // 口径与 CaptureBatch 的 art 模式逐项一致(16:9 定幅 + CameraRig 规范机位 + 同 mark 同焦点算法),
        // 所以「edit 无后处理」与「Play 有后处理」两张同机位图只差后处理一项,可直接 A/B 与量化比对。
        private IEnumerator RunArtCapture()
        {
            var boot = GetComponent<GameBootstrap>();
            if (boot == null || boot.Sim == null)
            {
                Debug.LogError("[artcap] GameBootstrap missing");
                yield break;
            }
            boot.Speed = 3f;
            AiSystem.Configure(boot.Sim, Side.Blue, AiTier.Hard);
            var cam = Camera.main;
            var rig = cam != null ? cam.GetComponent<CameraRig>() : null;
            if (cam != null)
            {
                cam.aspect = 16f / 9f; // 与 edit 通道同幅,避免窗口 aspect 影响取景与指标
                if (rig != null) rig.RefreshProjection();
                Debug.Log("[artcap] aspect=" + cam.aspect + " ortho=" + cam.orthographic + " fov=" + cam.fieldOfView);
            }
            string tag = System.Environment.GetEnvironmentVariable("IC_ART_ROUND") ?? "pre";
            Directory.CreateDirectory("D:/DevTools/shots");

            int i = 0;
            float guard = 0f;
            while (i < Marks.Length)
            {
                yield return null;
                double simSec = boot.Sim.Tick / (double)boot.Sim.Config.TickRate;
                if (simSec >= Marks[i])
                {
                    FocusCamera(cam, rig, i, boot.Sim);
                    string p = string.Format("D:/DevTools/shots/art_{0}_play_{1}_t{2}s.png", tag, i + 1, (int)Marks[i]);
                    SaveRender(cam, p);
                    // 同帧再存一张含 UI 的玩家实见图(顶栏/小地图/面板入图),供审美判断
                    ScreenCapture.CaptureScreenshot(string.Format("D:/DevTools/shots/art_{0}_playui_{1}_t{2}s.png", tag, i + 1, (int)Marks[i]));
                    Debug.Log("[artcap] " + p + " simSec=" + simSec.ToString("F1"));
                    i++;
                }
                guard += Time.unscaledDeltaTime;
                if (guard > 200f) { Debug.Log("[artcap] timeout"); break; }
            }
            yield return new WaitForSecondsRealtime(1.5f);
            SessionState.SetBool("ic.playcap", false);
            EditorApplication.ExitPlaymode();
        }

        // 与 CaptureBatch.FindBattleFocus/机位算法同源:优先对准交战单位,无交战走固定机位
        private static void FocusCamera(Camera cam, CameraRig rig, int i, SimCore sim)
        {
            SimVec2? focus = FindBattleFocus(sim);
            float camX = focus.HasValue ? focus.Value.X : CamX[i];
            float camRow = focus.HasValue ? focus.Value.Y : 15f;
            if (rig != null)
                rig.SetFocus(camX, camRow);
            else if (cam != null)
            {
                cam.transform.position = new Vector3(camX, 55f, 3f - camRow - 38f);
                cam.transform.LookAt(new Vector3(camX, 0f, 3f - camRow));
            }
        }

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
            DestroyImmediate(tex);
            DestroyImmediate(rt);
        }
#endif
    }
}
