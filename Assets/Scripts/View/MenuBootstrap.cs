using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IronCurtain.View
{
    /// <summary>主界面场景组装(2026-09-12 制作人定稿):展示台相机/灯光/后处理 + UiRoot + MenuStage
    /// (六英雄 3D 自选展示) + MenuPanel(头像占位/右列商城活动占位+AI两档+练习场) + bgm_menu。
    /// 相机取景把英雄让到画面左半,右半留给右列入口。bgm_menu 已入库(山林小调循环版),
    /// 场景卸载即随 AudioSource 销毁,战斗场景由 BgmController 接管两阶段 BGM。</summary>
    public sealed class MenuBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            CreateCamera();
            CreateLighting();
            CreatePostProcess();
            CreateGround();

            gameObject.AddComponent<UiRoot>();
            var stageGo = new GameObject("MenuStage");
            stageGo.transform.SetParent(transform, false);
            var stage = stageGo.AddComponent<MenuStage>();
            stage.Init();
            stage.Show(HeroId.ZhenYue);
            var panel = gameObject.AddComponent<MenuPanel>();
            panel.Init(stage.Show, HeroId.ZhenYue);

            PlayMenuBgm();
#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool("ic.menucap", false))
                gameObject.AddComponent<MenuCaptureRunner>();
#endif
            Debug.Log("[MenuBootstrap] menu ready");
        }

        private void CreateCamera()
        {
            var go = new GameObject("MainCamera");
            go.transform.SetParent(transform, false);
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x6e, 0xa8, 0xd8, 0xff);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.orthographic = false;
            cam.fieldOfView = 35f;
            // 英雄立于原点展示台;视线目标右偏 → 英雄落在画面左半(右半让给右列入口)
            go.transform.position = new Vector3(1.35f, 1.95f, 4.9f);
            go.transform.LookAt(new Vector3(1.05f, 1.25f, 0f));
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            go.AddComponent<AudioListener>();
        }

        private void CreateGround()
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Ground";
            disc.transform.SetParent(transform, false);
            disc.transform.localScale = new Vector3(60f, 0.05f, 60f);
            disc.transform.localPosition = new Vector3(0f, -0.03f, 0f);
            disc.GetComponent<Renderer>().sharedMaterial = ViewAssets.Flat(new Color(0.42f, 0.62f, 0.36f), 0.2f);
        }

        private void CreateLighting()
        {
            var go = new GameObject("Directional Light");
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.98f, 0.93f);
            light.intensity = 1.05f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.7f;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.78f, 0.98f);
            RenderSettings.ambientEquatorColor = new Color(0.74f, 0.67f, 0.54f);
            RenderSettings.ambientGroundColor = new Color(0.40f, 0.31f, 0.23f);
            RenderSettings.ambientIntensity = 1.2f;
        }

        private void CreatePostProcess()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.5f);
            bloom.scatter.Override(0.6f);
            var colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.saturation.Override(32f);
            colorAdjustments.contrast.Override(32f);
            colorAdjustments.postExposure.Override(0f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var go = new GameObject("PostProcess");
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.profile = profile;
        }

        private void PlayMenuBgm()
        {
            var clip = Resources.Load<AudioClip>("Audio/bgm_menu");
            if (clip == null)
            {
                Debug.Log("[MenuBootstrap] bgm_menu missing, menu silent");
                return;
            }
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = 0.5f;
            src.Play();
        }
    }
}
