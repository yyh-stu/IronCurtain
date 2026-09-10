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
            var buildingCommands = gameObject.AddComponent<BuildingCommandPanel>();
            buildingCommands.Init(Sim, Side.Blue, selection.SelectedIds);
            selection.SetBuildingCommandPanel(buildingCommands);
            // 2026-09-10 两阶段 BGM(参考平安京):敌方首建筑被推/占领时切后期曲;音频缺席时自静默
            var bgm = gameObject.AddComponent<BgmController>();
            bgm.Init(Sim, Side.Blue);
            var audio = gameObject.AddComponent<AudioSystem>(); // M3-S01 SFX 框架(事件驱动+对象池)
            audio.Init(Sim);
#if UNITY_EDITOR
            var inputAudit = gameObject.AddComponent<InputAuditProbe>();
            inputAudit.Init(Sim);
#endif

            var topBar = gameObject.AddComponent<TopBarPanel>();
            topBar.Init(Sim, Side.Blue);
            var upgrades = gameObject.AddComponent<UpgradeCommandPanel>();
            upgrades.Init(Sim, Side.Blue);
            var hexes = gameObject.AddComponent<HexCommandPanel>();
            hexes.Init(Sim, Side.Blue, selection.SelectedIds);
            var tools = gameObject.AddComponent<ToolCommandPanel>();
            tools.Init(Sim, Side.Blue);
            selection.SetToolCommandPanel(tools);
            var commandPanels = new CommandPanelCoordinator();
            commandPanels.Attach(upgrades, hexes, tools, buildingCommands);
            var result = gameObject.AddComponent<ResultPanel>();
            result.Init(Sim, Side.Blue);
            var ftGo = new GameObject("FloatingText");
            ftGo.transform.SetParent(transform, false);
            var floats = ftGo.AddComponent<FloatingTextView>();
            floats.Init(Sim, Side.Blue, Board);

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
            // 2026-09-09 bug2 真因修复:相机必须摆在初始焦点正上方同构偏移,
            // 原摆在 col143 → CameraRig._offset 恒带 +103 米横向偏差,小地图/键盘怎么挪都错位,
            // 两边基地被推出视野。用户拍板:鼠标点哪里,视图中心就在哪里。焦点常量见 CameraRig。
            float fx = CameraRig.InitFocusCol, fz = 3f - CameraRig.InitFocusRow;
            go.transform.position = new Vector3(fx, 55f, fz - 38f);
            go.transform.LookAt(new Vector3(fx, 0f, fz));
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
            bloom.threshold.Override(0.55f);
            bloom.intensity.Override(1.7f);
            bloom.scatter.Override(0.7f);
            var colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.saturation.Override(32f);
            colorAdjustments.contrast.Override(22f);
            colorAdjustments.postExposure.Override(0.35f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var go = new GameObject("PostProcess");
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.profile = profile;
        }
    }

    /// <summary>M6.3 操作面板互斥：展开一个入口时折叠其余入口，避免遮挡和目标模式歧义。</summary>
    public sealed class CommandPanelCoordinator
    {
        private UpgradeCommandPanel _upgrades;
        private HexCommandPanel _hexes;
        private ToolCommandPanel _tools;
        private BuildingCommandPanel _building;

        public void Attach(UpgradeCommandPanel upgrades, HexCommandPanel hexes, ToolCommandPanel tools,
            BuildingCommandPanel building = null)
        {
            _upgrades = upgrades;
            _hexes = hexes;
            _tools = tools;
            _building = building;
            _upgrades?.SetOpenedCallback(() => Opened(CommandPanelKind.Upgrade));
            _hexes?.SetOpenedCallback(() => Opened(CommandPanelKind.Hex));
            _tools?.SetOpenedCallback(() => Opened(CommandPanelKind.Tool));
        }

        private void Opened(CommandPanelKind opened)
        {
            _building?.ClearTarget("panel_switch");
            if (opened != CommandPanelKind.Upgrade) _upgrades?.SetExpanded(false);
            if (opened != CommandPanelKind.Hex) _hexes?.SetExpanded(false);
            if (opened != CommandPanelKind.Tool) _tools?.CancelTargeting("panel_switch");
            if (opened != CommandPanelKind.Tool) _tools?.SetExpanded(false);
#if UNITY_EDITOR
            InputAudit.Log("command_panel opened=" + opened + " result=exclusive");
#endif
        }
    }

    public enum CommandPanelKind : byte { Upgrade, Hex, Tool }
}
