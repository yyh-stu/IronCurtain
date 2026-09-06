using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IronCurtain.View
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        public SimCore Sim;
        public BoardView Board;
        public Camera Cam;
        public bool Paused;
        public float Speed = 1f;

        private double _accumulator;
        private CameraRig _rig;
        private FogView _fog;

        private void Awake()
        {
            if (Sim != null) return; // 域重载防护:已初始化则跳过
            var cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            Sim = MatchSetup.Build(cfg, null, AiTier.Normal);

            CreateCamera();
            _rig = Cam.gameObject.AddComponent<CameraRig>();
            _rig.Init(Cam, 40f, 15f); // 开局聚焦蓝基地前方 col40/row15
            CreateLighting();
            CreatePostProcess();

            var boardGo = new GameObject("Board");
            boardGo.transform.SetParent(transform, false);
            Board = boardGo.AddComponent<BoardView>();
            Board.Init(Sim, transform);
            Board.PlayerSide = Side.Blue;
            Sim.AddEventListener(Board.OnSimEvent);
            var overview = gameObject.AddComponent<BarracksOverviewPanel>();
            overview.Init(Sim, Side.Blue);
            gameObject.AddComponent<CaptureRunner>();
            var selection = gameObject.AddComponent<SelectionSystem>();
            selection.Init(Sim, Cam);

            var fogGo = new GameObject("FogView");
            fogGo.transform.SetParent(transform, false);
            _fog = fogGo.AddComponent<FogView>();
            _fog.Init(Sim, Side.Blue, transform);
            var minimap = gameObject.AddComponent<MinimapPanel>();
            minimap.Init(Sim, Side.Blue, _rig, _fog, Cam);

            Debug.Log("[GameBootstrap] Match started");
        }

        private void Update()
        {
            if (Paused || Sim == null) return;
            double interval = 1.0 / Sim.Config.TickRate;
            _accumulator += Time.deltaTime * Speed;
            while (_accumulator >= interval)
            {
                _accumulator -= interval;
                Sim.Step();
            }
            Board.TickViews();
        }

        private void OnDestroy()
        {
            if (Sim != null) Sim.ClearEventListeners();
        }

        private void CreateCamera()
        {
            var go = new GameObject("MainCamera");
            go.transform.SetParent(transform, false);
            go.tag = "MainCamera";
            Cam = go.AddComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color32(0x79, 0xb5, 0xea, 0xff); // 明亮天空蓝(部落冲突式明快)
            Cam.nearClipPlane = 0.3f;
            Cam.farClipPlane = 300f;
            Cam.orthographic = true;
            Cam.orthographicSize = 27f;
            go.transform.position = new Vector3(143f, 55f, -50f);
            go.transform.LookAt(new Vector3(143f, 0f, -12f));
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            go.AddComponent<AudioListener>();
        }

        private void CreateLighting()
        {
            var go = new GameObject("Directional Light");
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.98f, 0.93f);
            light.intensity = 1.05f;
            go.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.58f, 1f);
        }

        private void CreatePostProcess()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.55f);
            bloom.intensity.Override(1.7f);
            bloom.scatter.Override(0.7f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var go = new GameObject("PostProcess");
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.profile = profile;
        }
    }
}
