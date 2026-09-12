using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IronCurtain.View
{
    /// <summary>主界面场景组装(2026-09-12 制作人简化版):大本营场景(主基地+两翼兵营箭塔)+ 相机/灯光/后处理
    /// + UiRoot + MenuPanel(头像占位/右列商城活动占位+AI两档+练习场) + bgm_menu。
    /// 英雄展示台与图鉴已按制作人指令移除 —— 大厅现在就是一个大本营。
    /// bgm_menu 已入库(山林小调循环版),场景卸载即随 AudioSource 销毁,战斗场景由 BgmController 接管两阶段 BGM。</summary>
    public sealed class MenuBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            CreateCamera();
            CreateLighting();
            CreatePostProcess();
            CreateGround();
            CreateBackdrop();

            gameObject.AddComponent<UiRoot>();
            // 2026-09-12 制作人简化:大厅不再摆英雄(展示台/英雄图鉴一并移除),只留大本营场景
            var panel = gameObject.AddComponent<MenuPanel>();
            panel.Init();

            PlayMenuBgm();
#if UNITY_EDITOR
            // 2026-09-12:抓帧触发同时认 SessionState 与环境变量——环境变量跨域重载、跨编译器重载都稳,
            // 实测 SessionState 那一路在"启动时刚好在编译"的批次里会漏触发(取证不出图)。
            if (UnityEditor.SessionState.GetBool("ic.menucap", false)
                || System.Environment.GetEnvironmentVariable("IC_MENUCAP") == "1")
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
            cam.fieldOfView = 33f;
            // 2026-09-12 无展示台后重定取景:镜头正对大本营,基座居中,右列 UI 落在画面右侧
            go.transform.position = new Vector3(0.9f, 4.6f, 7.4f);
            go.transform.LookAt(new Vector3(0.9f, 3.4f, -8.6f));
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            go.AddComponent<AudioListener>();
        }

        /// <summary>大厅背景(2026-09-12 制作人指令:大本营背景+人物 3D 建模,用美术资源仓库/工程内既有 fbx)。
        /// 主基地居中后景、兵营与箭塔做两翼,全部按高度归一贴地(口径同 EntityView.AddPiece:保留 fbx root 旋转)。</summary>
        private void CreateBackdrop()
        {
            var backdrop = new GameObject("Backdrop");
            backdrop.transform.SetParent(transform, false);
            MenuStage.PlacePiece(backdrop.transform, "Models/Buildings/主基地", 8.0f, new Vector3(0.9f, 0f, -8.6f), "Buildings");
            MenuStage.PlacePiece(backdrop.transform, "Models/Buildings/兵营", 4.6f, new Vector3(-3.9f, 0f, -6.3f), "Buildings");
            MenuStage.PlacePiece(backdrop.transform, "Models/Buildings/兵营", 4.6f, new Vector3(3.6f, 0f, -6.8f), "Buildings");
            MenuStage.PlacePiece(backdrop.transform, "Models/Buildings/箭塔", 5.6f, new Vector3(-6.4f, 0f, -4.4f), "Buildings");
            MenuStage.PlacePiece(backdrop.transform, "Models/Buildings/箭塔", 5.6f, new Vector3(6.2f, 0f, -5.0f), "Buildings");
        }

        private void CreateGround()
        {
            // 2026-09-12:纯绿地面造成"空场感",改为石板色 + 外圈承台(大厅背景的地面基调)
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Ground";
            disc.transform.SetParent(transform, false);
            disc.transform.localScale = new Vector3(60f, 0.05f, 60f);
            disc.transform.localPosition = new Vector3(0f, -0.03f, 0f);
            disc.GetComponent<Renderer>().sharedMaterial = ViewAssets.Flat(new Color(0.50f, 0.48f, 0.44f), 0.12f);

            var apron = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            apron.name = "GroundApron";
            apron.transform.SetParent(transform, false);
            apron.transform.localScale = new Vector3(26f, 0.06f, 26f);
            apron.transform.localPosition = new Vector3(0.9f, 0.005f, -3.2f);
            apron.GetComponent<Renderer>().sharedMaterial = ViewAssets.Flat(new Color(0.42f, 0.40f, 0.37f), 0.1f);
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
