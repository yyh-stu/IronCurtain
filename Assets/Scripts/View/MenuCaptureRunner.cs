using System.Collections;
using System.IO;
using IronCurtain.Sim;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IronCurtain.View
{
    /// <summary>主界面取证通道(2026-09-12):进 Menu 场景 Play 后拍两张——默认英雄展示 +
    /// 切换逐风(骑马态);同步抓帧含 UITK。由 MenuBootstrap 按 SessionState "ic.menucap" 挂载,
    /// 编辑器入口 PlayCaptureBatch.MenuCapture。</summary>
    public sealed class MenuCaptureRunner : MonoBehaviour
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
            yield return new WaitForSecondsRealtime(2.5f);   // 等英雄/贴图加载与 UITK 布局稳定
            Shot("menu_home");
            var stage = FindFirstObjectByType<MenuStage>();
            if (stage != null)
            {
                stage.Show(HeroId.ZhuFeng);                   // 骑乘态(战马+RiderAnimator)
                yield return new WaitForSecondsRealtime(1.2f);
                Shot("menu_zhufeng");
            }
            yield return new WaitForSecondsRealtime(1f);
            SessionState.SetBool("ic.menucap", false);
            EditorApplication.ExitPlaymode();
        }

        private static void Shot(string name)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Dir + name + ".png", tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[menucap] " + name);
        }
#endif
    }
}
