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
            // 2026-09-17:失焦也继续跑(此前 runInBackground=0,切走窗口/系统对话框一弹,
            // 整局暂停 —— 实机自检曾因此冻在半途,玩家切屏也会停)。制作人可随时收回。
            Application.runInBackground = true;
            // 2026-09-12 主界面流程:AI对战(简单/困难)或练习场(无AI;换边走 AppFlow.SwitchPracticeSide **原地换边**);
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
            if (AppFlow.Current == AppFlow.Mode.Practice)
            {
                Sim.PopulationCapOverride = 999;   // 训练场:将名额上限无限制(制作人 2026-09-13)
                Sim.EpCapOverride = 99;            // 训练场:EP 无限(制作人 2026-09-14;顶栏显示 X/99)
                Sim.HexFreeDraw = true;            // 训练场:海克斯不限时不限次(制作人 2026-09-17)
            }

            CreateCamera(player);
            CreateLighting();
            CreatePostProcess();

            // 视图层抽成可重复调用的方法:换边(不重载)时只重建视图,Sim(兵力/比分/时间)继续
            BuildView(player);

            AiSystem.LogHook = msg => Debug.Log(msg);   // AI 心跳日志(Sim 不引 UnityEngine,View 层转发)

            Debug.Log("[GameBootstrap] Match started mode=" + AppFlow.Current + " player=" + player + " redAi=" + redAi);
        }

        // ===== 2026-09-13 训练场换边改"原地换边继续"(制作人:不是重载,是换个边继续) =====
        // 保留 Sim(对局状态继续),只把**视图/输入层**按新阵营重建。两段式:先拆、下一帧再建,
        // 避免同帧拆建导致 UiRoot.Current(静态)与 Sim 监听器打架。
        private bool _rebuildRequested;
        private int _rebuildPhase;

        /// <summary>请求换边重建(按钮调用)。实际拆除在下一帧 Update 里做,避开 UITK 回调栈。</summary>
        public void RequestSideRebuild() => _rebuildRequested = true;

        private void BuildView(Side player)
        {
            // 2026-09-13 临时诊断(换边行为):确认视图重建真的跑了、切到哪一边
            Debug.Log("[GameBootstrap] BuildView side=" + player + " simAlive=" + (Sim != null)
                + " tick=" + (Sim != null ? Sim.Tick : -1));
            // 2026-09-13 制作人:"换边不是移动镜头,是切换为对面的视野状态" ——
            // 相机本体与 CameraRig 在换边时**保持不动**(rig 在 TeardownView 里不再被拆),
            // 只有首次构建(Awake)才创建并定位 rig。
            if (_rig == null)
            {
                PlaceCameraHome(player);
                _rig = Cam.gameObject.AddComponent<CameraRig>();
                _rig.Init(Cam, player == Side.Red ? CameraRig.MapWidthM - CameraRig.InitFocusCol : CameraRig.InitFocusCol, 80f);
            }
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
            gameObject.AddComponent<UiShotRunner>();   // UI 离屏抓帧(ic.uishot 触发)
            var selection = gameObject.AddComponent<SelectionSystem>();
            selection.Init(Sim, Cam, player);
            selection.SetBoardView(Board); // 选中态要落到单位脚下的光环上(id → EntityView)
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
            // 2026-09-17 制作人拍板:玩家可主动增减的倍速 ×1/×2/×4/×8(顶栏右侧 [−] ×N [+])
            var speedCtl = gameObject.AddComponent<SpeedControlPanel>();
            speedCtl.Init(this);
            // 2026-09-17 特效计划 M1.1:对局根/双时钟/池(内部验证标记暂挂其下;正式效果自 M1.2 接入)
            var vfx = gameObject.AddComponent<VfxDirector>();
            vfx.Init(this);
            // 2026-09-17 M3/M4:状态图标(C06)+ 海克斯横幅(X00)
            var statusIcons = gameObject.AddComponent<StatusIconSystem>();
            statusIcons.Init(Sim, player);
            var upgrades = gameObject.AddComponent<UpgradeCommandPanel>();
            upgrades.Init(Sim, player);
            var hexes = gameObject.AddComponent<HexCommandPanel>();
            hexes.Init(Sim, player, selection.SelectedIds);
            var tools = gameObject.AddComponent<ToolCommandPanel>();
            tools.Init(Sim, player);
            selection.SetToolCommandPanel(tools);
            // 2026-09-16 三十七轮:目标模式战场可发现性(高亮合法目标/常驻提示/准星光标)
            var targetingFx = gameObject.AddComponent<TargetingFeedback>();
            targetingFx.Init(Sim, player, tools, Board);
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
            // 2026-09-14 制作人:ESC 暂停浮层(仅对局场景有它 → "仅 ai/训练场生效"天然成立)
            var pause = gameObject.AddComponent<PauseOverlayPanel>();
            pause.Init(this);
            var minimap = gameObject.AddComponent<MinimapPanel>();
            minimap.Tool = tools;   // 小地图选敌方建筑当道具目标(2026-09-14)
            minimap.Init(Sim, player, _rig, _fog, Cam);
        }

        /// <summary>拆掉上一次建的视图(Sim 与相机/灯光/后处理本体不动)。</summary>
        private void TeardownView()
        {
            if (Sim != null) Sim.ClearEventListeners();   // 旧 Board/Audio/FloatingText 的订阅一并丢弃
            foreach (var n in new[] { "Board", "FloatingText", "FogView" })
            {
                var t = transform.Find(n);
                if (t != null) Destroy(t.gameObject);
            }
            // 2026-09-14 换边"问题还在"的根因修复。旧实现只 Destroy 组件,漏了四类残留:
            // ① UIDocument(整棵旧 UI 树仍渲染且仍吃点击 → 点到的都是死按钮);
            // ② MinimapPanel / UiShotRunner(不在拆除清单里 → 每次换边多一份,旧的那份还在按旧阵营画);
            // ③ WallHealthView 自建的 WallHpBars 物体(组件拆了、物体还在 → 换边后叠两套屏障血条);
            // ④ WallFx/WallFlash 的静态池(池里的物件挂在被拆掉的 Board 下 → 换边后第一次命中摸到已销毁对象);
            // 外加 SelectionSystem 的行军路径线(挂在 bootstrap 下,OnDestroy 没管)。
            var ui = GetComponent<UiRoot>();
            if (ui != null) { ui.Teardown(); Destroy(ui); }
            var wallBars = transform.Find("WallHpBars");
            if (wallBars != null) Destroy(wallBars.gameObject);
            var paths = transform.Find("SelPaths");
            if (paths != null) Destroy(paths.gameObject);
            WallFx.ResetWorldPools();
            DestroyComp<WallHealthView>();
            DestroyComp<BarracksOverviewPanel>();
            DestroyComp<CaptureRunner>();
            DestroyComp<MinimapPanel>();
            DestroyComp<PauseOverlayPanel>();
            DestroyComp<UiShotRunner>();
            DestroyComp<SelectionSystem>();
            DestroyComp<BuildingCommandPanel>();
            DestroyComp<BgmController>();
            DestroyComp<AudioSystem>();
            DestroyComp<TopBarPanel>();
            DestroyComp<UpgradeCommandPanel>();
            DestroyComp<HexCommandPanel>();
            DestroyComp<ToolCommandPanel>();
            DestroyComp<TargetingFeedback>();
            DestroyComp<SpeedControlPanel>();
            DestroyComp<VfxDirector>();
            DestroyComp<StatusIconSystem>();
            DestroyComp<ResultPanel>();
            DestroyComp<PracticeHud>();
#if UNITY_EDITOR
            DestroyComp<InputAuditProbe>();
            DestroyComp<PracticeCaptureRunner>();
#endif
            // 2026-09-14 制作人(第三次明确):"训练场切换阵营**只切换阵营和视野状态,不要改相机位置**"。
            // —— 相机本体与它的控制(CameraRig)**一律不动**:不拆、不重建、不归位。
            // (上一版我把它改成"随新阵营归位、把相机拉到新基地",那是我推翻了制作人口径,已还原。
            //  注意 BuildView 里 PlaceCameraHome 只在 `_rig == null`(首次构建)时才走,
            //  所以这里**保留 rig** 就等于"换边后镜头停在原处"。)
            Board = null; _fog = null; _wallHp = null;
        }

        private void DestroyComp<T>() where T : Component
        {
            var c = GetComponent<T>();
            if (c != null) Destroy(c);
        }

        private void Update()
        {
            if (Paused || Sim == null) return;
            if (_rebuildRequested) { _rebuildRequested = false; TeardownView(); _rebuildPhase = 1; return; }
            if (_rebuildPhase == 1) { _rebuildPhase = 0; BuildView(AppFlow.PlayerSide); return; }
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

        /// <summary>相机初始机位(焦点正上方同构偏移)。CreateCamera 与换边重建都要用。</summary>
        private void PlaceCameraHome(Side player)
        {
            float fx = player == Side.Red ? CameraRig.MapWidthM - CameraRig.InitFocusCol : CameraRig.InitFocusCol;
            float fz = 3f - CameraRig.InitFocusRow;
            Cam.transform.position = new Vector3(fx, 49.4f, fz - 34.1f);
            Cam.transform.LookAt(new Vector3(fx, 0f, fz));
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
            Cam.farClipPlane = 900f;   // 2026-09-12:拉远到整图视野(~400m 宽)时远处不能被裁
            // ART-P0 2026-09-10:正交 → 透视(FOV 30°,落在规格 30-40° 下沿)。
            // 机位距离由 66.85(=√(55²+38²))收到 60,方向不变(按 55:38 比例缩放为 49.4:34.1),
            // 这样焦点平面上的横向视野仍≈57.2m(=1/5 地图宽),取景范围感与原正交一致,只多了透视纵深。
            Cam.orthographic = false;
            Cam.fieldOfView = 30f;
            // 2026-09-09 bug2 真因修复:相机必须摆在初始焦点正上方同构偏移,
            // 原摆在 col143 → CameraRig._offset 恒带 +103 米横向偏差,小地图/键盘怎么挪都错位,
            // 两边基地被推出视野。用户拍板:鼠标点哪里,视图中心就在哪里。焦点常量见 CameraRig。
            // 2026-09-12 换边:红方玩家初始焦点镜像到红主城前方。
            PlaceCameraHome(player);
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
