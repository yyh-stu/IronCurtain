using System.Collections;
using System.IO;
using IronCurtain.Sim;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IronCurtain.View
{
    /// <summary>结算+驻军 tooltip 专用取证通道(2026-09-12):此前 12_结算与驻军 证据是无 UI 战场帧。
    /// 同步抓帧(CaptureScreenshotAsTexture 直写 PNG,无异步落盘时序);tooltip 两张(远/近)在
    /// 强制结束前拍,结算两张(大字缓入/面板滑入后)在 Phase==Ended 后按真实时间拍;
    /// 每张同时落盘 ResultPanel.DebugState() 供像素/状态双向核对。
    /// 依赖环境变量 IC_UI_RESULT=1(ResultPanel 15s 强制蓝胜)与 IC_UI_TIP=1(常显 tooltip)。
    /// 由 CaptureRunner 按 SessionState "ic.resultcap" 动态挂载,编辑器入口 PlayCaptureBatch.ResultCapture。</summary>
    public sealed class ResultCaptureRunner : MonoBehaviour
    {
        private const string Dir = "D:/DevTools/shots/";

        private void Start()
        {
#if UNITY_EDITOR
            StartCoroutine(Run());
#endif
        }

#if UNITY_EDITOR
        private IEnumerator Run()
        {
            var boot = GetComponent<GameBootstrap>();
            if (boot == null || boot.Sim == null)
            {
                Debug.LogError("[resultcap] GameBootstrap missing");
                yield break;
            }
            boot.Speed = 3f;
            AiSystem.Configure(boot.Sim, Side.Blue, AiTier.Hard); // 对打保证战绩非零
            var cam = Camera.main;
            var rig = cam != null ? cam.GetComponent<CameraRig>() : null;
            if (cam != null)
            {
                cam.aspect = 16f / 9f;               // 定幅取证,与 art 通道同口径
                if (rig != null) rig.RefreshProjection();
            }
            Directory.CreateDirectory(Dir);
            SimVec2? tipAt = FindFirstOwnBuilding(boot.Sim, Side.Blue);
            if (tipAt == null) tipAt = new SimVec2(8f, 80f);

            bool tipFar = false, tipNear = false, titleShot = false, fullShot = false;
            float endedAt = -1f, guard = 0f;
            while (!(tipFar && tipNear && titleShot && fullShot))
            {
                yield return null;
                double simSec = boot.Sim.Tick / (double)boot.Sim.Config.TickRate;
                if (!tipFar && simSec >= 6)
                {
                    tipFar = true;
                    if (rig != null) rig.SetFocus(tipAt.Value.X, tipAt.Value.Y);
                    yield return new WaitForSecondsRealtime(0.4f);
                    Shot("result_tip_far");
                    Debug.Log("[resultcap] tip_far @" + tipAt.Value.X + "," + tipAt.Value.Y);
                }
                if (!tipNear && simSec >= 8)
                {
                    tipNear = true;
                    if (rig != null) { rig.SetViewWidth(30f); rig.SetFocus(tipAt.Value.X, tipAt.Value.Y); }
                    yield return new WaitForSecondsRealtime(0.4f);
                    Shot("result_tip_near");
                    Debug.Log("[resultcap] tip_near");
                }
                if (boot.Sim.Phase == MatchPhase.Ended)
                {
                    if (endedAt < 0f)
                    {
                        endedAt = Time.unscaledTime;
                        Debug.Log("[resultcap] ended! " + PanelState());
                    }
                    float t = Time.unscaledTime - endedAt;
                    if (!titleShot && t >= 0.8f)     // 遮罩+大字缓入完成(0.25+0.45),面板未滑入(0.85 起)
                    {
                        titleShot = true;
                        Shot("result_title");
                        Debug.Log("[resultcap] title " + t.ToString("F2") + "s " + PanelState());
                    }
                    if (!fullShot && t >= 2.0f)      // 面板滑入(0.85+0.4)后留裕量
                    {
                        fullShot = true;
                        Shot("result_full");
                        Debug.Log("[resultcap] full " + t.ToString("F2") + "s " + PanelState());
                    }
                }
                guard += Time.unscaledDeltaTime;
                if (guard > 120f) { Debug.Log("[resultcap] timeout " + PanelState()); break; }
            }
            yield return new WaitForSecondsRealtime(1.5f);
            SessionState.SetBool("ic.resultcap", false);
            EditorApplication.ExitPlaymode();   // Play 结束后协程即止,编辑器进程由外部按产物收口
        }

        /// <summary>同步抓当前帧(含 UITK UI)直写 PNG——排除 CaptureScreenshot 异步落盘时序。</summary>
        private static void Shot(string name)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Dir + name + ".png", tex.EncodeToPNG());
            Destroy(tex);
        }

        private static string PanelState()
        {
            var rp = FindFirstObjectByType<ResultPanel>();
            return rp != null ? rp.DebugState() : "result:component-missing";
        }

        /// <summary>与 BuildingCommandPanel.IC_UI_TIP 同源选点:第一个己方非废墟建筑。</summary>
        private static SimVec2? FindFirstOwnBuilding(SimCore sim, Side side)
        {
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Building || e.Rubbled) continue;
                if (e.Side != side) continue;
                return e.PosF;
            }
            return null;
        }
#endif
    }
}
