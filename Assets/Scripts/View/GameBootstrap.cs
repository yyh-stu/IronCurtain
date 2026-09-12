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
        private WallHealthView _wallHp;

        private void Awake()
        {
            if (Sim != null) return; // 域重载防护:已初始化则跳过
            // 2026-09-12 主界面流程:AI对战(简单/困难)或练习场(无AI,AppFlow.SwitchPracticeSide 重载换边);
            // 直接 Play Main(取证/旧流程)默认等价困难AI+蓝方,行为与既有通道完全一致。
            // 批处理取证注意:EnterPlaymode 域重载会清静态 AppFlow,模式经 SessionState "ic.mode" 传递。
#if UNITY_EDITOR
            switch (UnityEditor.SessionState.GetInt("ic.mode", -1))
            {
                case 0: AppFlow.Current = AppFlow.Mode.Menu; break;
                case 1: AppFlow.Current = AppFlow.Mode.AiSimple; break;
                case 2: AppFlow.Current = AppFlow.Mode.AiHard; break;
                case 3: AppFlow.Current = AppFlow.Mode.Practice; break;
            }
#endif
            var player = AppFlow.PlayerSide;
            AiTier? redAi = AppFlow.Current == AppFlow.Mode.Practice ? (AiTier?)null
                : (AppFlow.Current == AppFlow.Mode.AiSimple ? AiTier.Simple : AiTier.Hard);
            var cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            Sim = MatchSetup.Build(cfg, null, redAi);

            CreateCamera(player);
            _rig = Cam.gameObject.AddComponent<CameraRig>();
            _rig.Init(Cam, player == Side.Red ? CameraRig.MapWidthM - CameraRig.InitFocusCol : CameraRig.InitFocusCol, 80f); // 红方开局聚焦红主城前方
            CreateLighting();
            CreatePostProcess();

            AiSystem.LogHook = msg => Debug.Log(msg);   // AI 心跳日志(Sim 不引 UnityEngine,View 层转发)
            var boardGo = new GameObject("Board");
            boardGo.transform.SetParent(transform, false);
            Board = boardGo.AddComponent<BoardView>();
            Board.Init(Sim, transform);
            Board.PlayerSide = player;
            Sim.AddEventListener(Board.OnSimEvent);
            // 2026-09-11 屏障氛围粒子(每方一个系统,单 draw call;edit 模式由 CaptureBatch 手动推进)
            WallFx.SpawnAmbient(boardGo.transform, Side.Blue);
            WallFx.SpawnAmbient(boardGo.transform, Side.Red);
            // 2026-09-11 屏障血条:五段各自独立,受击才显示(满血隐藏)
            _wallHp = gameObject.AddComponent<WallHealthView>();
            _wallHp.Init(transform);
            // 2026-09-11 UI demo→成品迁移:UITK 根必须先于**所有**面板建立(含最早 Init 的兵营总览/建筑命令)
            gameObject.AddComponent<UiRoot>();
            var overview = gameObject.AddComponent<BarracksOverviewPanel>();
            overview.Init(Sim, player);
            gameObject.AddComponent<CaptureRunner>();
            var selection = gameObject.AddComponent<SelectionSystem>();
            selection.Init(Sim, Cam, player);
            var buildingCommands = gameObject.AddComponent<BuildingCommandPanel>();
            buildingCommands.Init(Sim, player, selection.SelectedIds);
            selection.SetBuildingCommandPanel(buildingCommands);
            // 2026-09-10 两阶段 BGM(参考平安京):敌方首建筑被推/占领时切后期曲;音频缺席时自静默
            var bgm = gameObject.AddComponent<BgmController>();
            bgm.Init(Sim, player);
            var audio = gameObject.AddComponent<AudioSystem>(); // M3-S01 SFX 框架(事件驱动+对象池)
            audio.Init(Sim);
#if UNITY_EDITOR
            var inputAudit = gameObject.AddComponent<InputAuditProbe>();
            inputAudit.Init(Sim);
#endif

            var topBar = gameObject.AddComponent<TopBarPanel>();
            topBar.Init(Sim, player);
            var upgrades = gameObject.AddComponent<UpgradeCommandPanel>();
            upgrades.Init(Sim, player);
            var hexes = gameObject.AddComponent<HexCommandPanel>();
            hexes.Init(Sim, player, selection.SelectedIds);
            var tools = gameObject.AddComponent<ToolCommandPanel>();
            tools.Init(Sim, player);
            selection.SetToolCommandPanel(tools);
            var commandPanels = new CommandPanelCoordinator();
            commandPanels.Attach(upgrades, hexes, tools, buildingCommands);
            var result = gameObject.AddComponent<ResultPanel>();
            result.Init(Sim, player);
            // 练习场:无AI,顶部提供重载式换边入口(制作人定稿:切换操控双方)
            if (AppFlow.Current == AppFlow.Mode.Practice)
            {
                var practiceHud = gameObject.AddComponent<PracticeHud>();
                practiceHud.Init(player);
#if UNITY_EDITOR
                if (UnityEditor.SessionState.GetBool("ic.pracap", false))
                    gameObject.AddComponent<PracticeCaptureRunner>();
#endif
            }
            var ftGo = new GameObject("FloatingText");
            ftGo.transform.SetParent(transform, false);
            var floats = ftGo.AddComponent<FloatingTextView>();
            floats.Init(Sim, player, Board);

            var fogGo = new GameObject("FogView");
            fogGo.transform.SetParent(transform, false);
            _fog = fogGo.AddComponent<FogView>();
            _fog.Init(Sim, player, transform);
            var minimap = gameObject.AddComponent<MinimapPanel>();
            minimap.Init(Sim, player, _rig, _fog, Cam);

            Debug.Log("[GameBootstrap] Match started mode=" + AppFlow.Current + " player=" + player + " redAi=" + redAi);
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
            if (_wallHp != null) _wallHp.Tick(Cam);   // 屏障血条:受击才显示
        }

        private void OnDestroy()
        {
            if (Sim != null) Sim.ClearEventListeners();
        }

        private void CreateCamera(Side player)
        {
            var go = new GameObject("MainCamera");
            go.transform.SetParent(transform, false);
            go.tag = "MainCamera";
            Cam = go.AddComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color32(0x79, 0xb5, 0xea, 0xff); // 明亮天空蓝(部落冲突式明快)
            Cam.nearClipPlane = 0.3f;
            Cam.farClipPlane = 300f;
            // ART-P0 2026-09-10:正交 → 透视(FOV 30°,落在规格 30-40° 下沿)。
            // 机位距离由 66.85(=√(55²+38²))收到 60,方向不变(按 55:38 比例缩放为 49.4:34.1),
            // 这样焦点平面上的横向视野仍≈57.2m(=1/5 地图宽),取景范围感与原正交一致,只多了透视纵深。
            Cam.orthographic = false;
            Cam.fieldOfView = 30f;
            // 2026-09-09 bug2 真因修复:相机必须摆在初始焦点正上方同构偏移,
            // 原摆在 col143 → CameraRig._offset 恒带 +103 米横向偏差,小地图/键盘怎么挪都错位,
            // 两边基地被推出视野。用户拍板:鼠标点哪里,视图中心就在哪里。焦点常量见 CameraRig。
            // 2026-09-12 换边:红方玩家初始焦点镜像到红主城前方。
            float fx = player == Side.Red ? CameraRig.MapWidthM - CameraRig.InitFocusCol : CameraRig.InitFocusCol;
            float fz = 3f - CameraRig.InitFocusRow;
            go.transform.position = new Vector3(fx, 49.4f, fz - 34.1f);
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
            // ART-P0 2026-09-10:此前全工程零阴影。除 URP 资产未开软阴影外,主光也从未显式设置
            // shadows;另 m_ShadowDistance 原为 50,而相机距地面焦点约 60-67m,焦点区整体落在
            // 阴影距离之外 → 即使开了也不会显示,故一并调到 120。
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.7f;
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
            // ART-P0 2026-09-10:后处理此前从未生效——根因是 UniversalRenderer.asset 的
            // postProcessData 为空引用,URP 后处理通道拿不到 shader(修好后 edit/Play 均生效,
            // 故"必须走 Play 才能看后处理"的旧结论作废)。以下数值是首次可见后才调的:
            // 原 postExposure +0.35 配 ACES 整屏泛白(实测亮部 21.6%→52.4%、饱和 0.26→0.16、
            // 边缘 5.1→1.9),按厚实高饱和卡通锚回调。
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);   // 原 0.55:压曝光后阈值过松会全屏起雾
            bloom.intensity.Override(0.5f);   // 原 1.7:泛光吃掉边缘能量(5.1→1.9 主因之一)
            bloom.scatter.Override(0.6f);
            var colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.saturation.Override(32f);
            colorAdjustments.contrast.Override(32f);
            colorAdjustments.postExposure.Override(0f); // 原 +0.35:ACES+泛光下过曝
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral); // 原 ACES:电影感压饱和,与卡通锚相冲
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
