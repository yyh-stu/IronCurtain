using System.Collections;
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
            if (!SessionState.GetBool("ic.capture", false))
            {
                if (SessionState.GetBool("ic.fastresult", false))
                {
                    StartCoroutine(RunFastResult());
                    return;
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
            AiSystem.Configure(boot.Sim, Side.Blue, AiTier.Normal); // 验收用普通 AI,兵种多样

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
            AiSystem.Configure(boot.Sim, Side.Blue, AiTier.Normal); // 保证对打有伤亡与 EP 差,面板数据非全零

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
    }
}
