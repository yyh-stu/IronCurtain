using System.Collections;
using System.IO;
using IronCurtain.Sim;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IronCurtain.View
{
    /// <summary>练习场自动验证通道(2026-09-12):练习模式进战斗拍蓝方帧(PracticeHud 可见+无AI),
    /// 然后走真实换边路径 AppFlow.SwitchPracticeSide() 重载场景,再拍红方帧——一次验证
    /// 无AI引导、换边HUD、重载式换边全链。由 GameBootstrap 按 SessionState "ic.pracap" 挂载,
    /// 编辑器入口 PlayCaptureBatch.PracticeCapture;阶段由 "ic.pracapPhase"(0=蓝/1=红)防死循环。</summary>
    public sealed class PracticeCaptureRunner : MonoBehaviour
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
            Directory.CreateDirectory(Dir);
            yield return new WaitForSecondsRealtime(3f);   // 等场景/UI/单位渲染稳定
            Side side = AppFlow.PlayerSide;
            Shot("practice_" + (side == Side.Blue ? "blue" : "red"));
            Debug.Log("[pracap] shot side=" + side + " ai=" + AiSystem.DebugStateOf(Side.Red));

            if (SessionState.GetInt("ic.pracapPhase", 0) == 0)
            {
                SessionState.SetInt("ic.pracapPhase", 1);
                Debug.Log("[pracap] switching side (reload path)");
                AppFlow.SwitchPracticeSide();              // 真实换边:重载场景,重进 Play 后本组件重挂
                yield break;
            }
            SessionState.SetInt("ic.pracapPhase", 0);
            SessionState.SetInt("ic.mode", -1);        // 模式传递复位,防污染后续直接 Play
            SessionState.SetBool("ic.pracap", false);
            yield return new WaitForSecondsRealtime(0.5f);
            EditorApplication.ExitPlaymode();
        }

        private static void Shot(string name)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Dir + name + ".png", tex.EncodeToPNG());
            Destroy(tex);
        }
#endif
    }
}
