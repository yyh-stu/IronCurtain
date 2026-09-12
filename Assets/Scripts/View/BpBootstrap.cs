using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronCurtain.View
{
    /// <summary>BP 场景组装(2026-09-13):深色底 + 左蓝右红两片"屏障式"半透明斜面(加色发光,
    /// 呼唤战斗内魔法屏障),中间由 BpPanel 画选将 UI。选完 3 轮把结果写进 AppFlow 并进战斗。</summary>
    public sealed class BpBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            CreateCamera();
            CreateBarrierBackdrop();
            gameObject.AddComponent<UiRoot>();
            var session = new BpSession(Random.Range(int.MinValue, int.MaxValue));
            var panel = gameObject.AddComponent<BpPanel>();
            panel.Init(session, () =>
            {
                AppFlow.BpBluePicks = new List<HeroId>(session.Blue);
                AppFlow.BpRedPicks = new List<HeroId>(session.Red);
                AppFlow.EnterBattle();
            });
#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool("ic.bpcap", false)
                || System.Environment.GetEnvironmentVariable("IC_BPCAP") == "1")
                gameObject.AddComponent<BpCaptureRunner>();
#endif
            Debug.Log("[BpBootstrap] bp ready mode=" + AppFlow.Current);
        }

        private void CreateCamera()
        {
            var go = new GameObject("MainCamera");
            go.transform.SetParent(transform, false);
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x12, 0x18, 0x26, 0xff);   // DeepSeek:#121826
            cam.orthographic = false;
            cam.fieldOfView = 40f;
            go.transform.position = new Vector3(0f, 2.2f, 9f);
            go.transform.LookAt(new Vector3(0f, 2.2f, 0f));
        }

        /// <summary>左右两片斜置发光面(近大远小,像两道屏障墙对着镜头),蓝左红右。</summary>
        private void CreateBarrierBackdrop()
        {
            MakeBarrier(new Vector3(-5.6f, 2.6f, -2.5f), 160f, new Color(0.22f, 0.68f, 1.00f, 0.08f));   // DeepSeek:降到8%当氛围底
            MakeBarrier(new Vector3(5.6f, 2.6f, -2.5f), -160f, new Color(0.96f, 0.21f, 0.17f, 0.08f));
        }

        private void MakeBarrier(Vector3 pos, float yaw, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BarrierWash";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 12f);
            go.transform.localScale = new Vector3(9f, 7f, 1f);
            var m = new Material(Shader.Find("Sprites/Default")) { color = c };
            go.GetComponent<Renderer>().sharedMaterial = m;
        }
    }

#if UNITY_EDITOR
    /// <summary>BP 取证:进入 BP 后分段拍 Picking(带英雄卡/倒计时)与 Reveal(揭示浮层)与完成槽位。
    /// 环境变量 IC_BPCAP=1 触发(BpBootstrap 挂载)。</summary>
    public sealed class BpCaptureRunner : MonoBehaviour
    {
        private void Start() { StartCoroutine(Run()); }

        private System.Collections.IEnumerator Run()
        {
            const string dir = "D:/DevTools/shots/";
            System.IO.Directory.CreateDirectory(dir);
            yield return new WaitForSecondsRealtime(1.0f);
            Shot(dir + "bp_pick1.png");
            var panel = FindFirstObjectByType<BpPanel>();
            var s = SessionOf(panel);
            // 等第一轮揭示(最多 12s)
            float t = 0f;
            while (s != null && s.State == BpSession.Phase.Picking && t < 12f)
            { yield return null; t += Time.unscaledDeltaTime; }
            if (s != null && s.State == BpSession.Phase.Reveal)
            {
                Shot(dir + "bp_reveal1.png");
                yield return new WaitForSecondsRealtime(0.8f);
            }
            // 快进:直接喂时间跑完全部轮次
            while (s != null && s.State != BpSession.Phase.Done)
            {
                s.Tick(0.5f);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.4f);
            Shot(dir + "bp_slots.png");
            if (Application.isPlaying)
            {
                // 停在完成态:阻止 BpPanel 真正进战斗(取证不切场景)
                Time.timeScale = 1f;
                UnityEditor.EditorApplication.ExitPlaymode();
            }
        }

        private static BpSession SessionOf(BpPanel panel)
        {
            var f = typeof(BpPanel).GetField("_s", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return f != null ? f.GetValue(panel) as BpSession : null;
        }

        private static void Shot(string path)
        {
            var tex = UnityEngine.ScreenCapture.CaptureScreenshotAsTexture();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[bpcap] " + path);
        }
    }
}
#endif
