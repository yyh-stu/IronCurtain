using System.IO;
using System.Reflection;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // M6 视觉验收·后台离屏截图(不需要窗口/焦点/热键):
    //   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IronCurtain.EditorTools.CaptureBatch.Run
    // 原理:edit 模式下反射调用 GameBootstrap.Awake 建全场景(Sim/相机/棋盘),
    // 手动步进 Sim 到 10/30/60/120s,相机按位 Camera.Render 到 RenderTexture 存 PNG。
    // M3-S03: 环境变量 IC_SEQ=1 启用序列模式——同一机位连截 3 帧(间隔约 0.33s 游戏时间),
    // 用于捕捉静态截图不可见的动态错位/漂移缺陷。不设 IC_SEQ 时行为与旧版完全一致。
    public static class CaptureBatch
    {
        private static readonly double[] Marks = { 10, 30, 60, 120 };
        // 三路地图(2026-09-11):沿中路取点——蓝主城前 / 蓝前线 / 河心(中线) / 红前线
        private static readonly float[] CamX = { 40f, 100f, 135f, 200f };

        // ART-P0 美术验收通道(环境变量 IC_ART_ROUND=<tag> 启用,不设时行为与旧版完全一致):
        //   ① 强制 Camera.aspect=16:9 + 重算投影——批处理窗口 aspect 与实际不符会让画面上下留黑边,
        //      且正交尺寸/透视 FOV 都按错误 aspect 算,取证图不能代表玩家所见;
        //   ② 机位走 CameraRig.SetFocus(玩家真实规范机位),而非直摆 transform——两者在旧代码里等价
        //      (offset 同为 (0,55,-38)),但相机改透视后 offset 会变,只有走 rig 才与 Play 通道同口径;
        //   ③ 文件名 art_<tag>_edit_*,不覆盖 m6.2_* 归档证据。
        private static string _artTag;
        private static bool ArtMode { get { return ArtTag().Length > 0; } }

        private static string ArtTag()
        {
            if (_artTag == null) _artTag = System.Environment.GetEnvironmentVariable("IC_ART_ROUND") ?? "";
            return _artTag;
        }

        public static void Run()
        {
            if (!File.Exists("Assets/Scenes/Main.unity"))
                IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");

            // IC_WALL_FADE="alpha,pow,glow":屏障渐隐三档对照(必须在建场景之前设,材质在 Build 时成型)
            string fadeEnv = System.Environment.GetEnvironmentVariable("IC_WALL_FADE");
            if (!string.IsNullOrEmpty(fadeEnv))
            {
                var parts = fadeEnv.Split(',');
                if (parts.Length == 3)
                {
                    WallView.SetFade(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                    Debug.Log("[CaptureBatch] wall fade " + fadeEnv);
                }
            }

            var go = new GameObject("CaptureBoot");
            var boot = go.AddComponent<GameBootstrap>();
            typeof(GameBootstrap)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(boot, null);
            if (boot.Sim == null || Camera.main == null)
            {
                Debug.LogError("[CaptureBatch] init failed: Sim=" + (boot.Sim == null) + " cam=" + (Camera.main == null));
                EditorApplication.Exit(1);
                return;
            }

            AiSystem.Configure(boot.Sim, Side.Blue, AiTier.Normal); // 双方对打,画面有内容
            Directory.CreateDirectory("D:/DevTools/shots");
            var cam = Camera.main;
            var fogView = Object.FindFirstObjectByType<FogView>();
            var floatText = Object.FindFirstObjectByType<FloatingTextView>();
            var wallHp = Object.FindFirstObjectByType<WallHealthView>();

            // M3-S03: 序列模式开关(环境变量 IC_SEQ=1);默认关闭,零侵入
            bool seqMode = System.Environment.GetEnvironmentVariable("IC_SEQ") == "1";
            // 2026-09-11 屏障特效演示开关(IC_WALL_FX=1);IC_WALL_AMBIENT=<每秒每方> 调氛围密度档
            bool wallFxDemo = System.Environment.GetEnvironmentVariable("IC_WALL_FX") == "1";
            bool playerView = System.Environment.GetEnvironmentVariable("IC_WALL_VIEW") == "player";
            // IC_WALL_BODY=0:关掉屏障"实体层",回到纯加色发光版(审美 A/B 用)
            bool bodyOff = System.Environment.GetEnvironmentVariable("IC_WALL_BODY") == "0";
            if (bodyOff) WallView.SetBodyVisible(false);
            string ambEnv = System.Environment.GetEnvironmentVariable("IC_WALL_AMBIENT");
            float ambRate;
            if (!string.IsNullOrEmpty(ambEnv) && float.TryParse(ambEnv, out ambRate))
            {
                WallFx.SetAmbientRate(ambRate);
                Debug.Log("[CaptureBatch] ambient rate=" + ambRate);
            }

            // ART-P0: 定死 16:9 并重算投影,后续所有帧都在同一规范取景下拍摄
            var rig = cam.GetComponent<CameraRig>();
            if (ArtMode)
            {
                cam.aspect = 16f / 9f;
                if (rig != null) rig.RefreshProjection();
                Debug.Log("[CaptureBatch] art mode tag=" + ArtTag() + " aspect=" + cam.aspect
                    + " ortho=" + cam.orthographic + " fov=" + cam.fieldOfView);
            }
            // 序列帧间隔约 0.33s 游戏时间,按 TickRate 换算 tick 数(至少 1 tick)
            int seqTicks = System.Math.Max(1, (int)(0.33 * boot.Sim.Config.TickRate));

            // 首帧预热:edit 模式下第一次 Camera.Render() 会产出纯色废帧(URP shader 未就绪,
            // 实测 t10 全屏 (92,195,255) 纯青),先渲染一帧弃用
            var warmup = new RenderTexture(64, 64, 24);
            cam.targetTexture = warmup;
            cam.Render();
            cam.targetTexture = null;            // 2026-09-10 重大修复:edit 模式下 URP Volume 后处理默认不参与 Camera.Render()
            // (实测 Bloom/ACES/ColorAdjustments 全部无效,saturation=100 无变化)。
            // 强制初始化 Volume 管理器并更新全局 Volume,让批处理截图真实反映后处理效果。
            // URP Volume 在 edit mode 的激活:Initialize 会重置 VolumeManager 状态栈,
            // 随后首帧 Camera.Render 即按全局 Volume 应用后处理
            UnityEngine.Rendering.VolumeManager.instance.Initialize();
            Object.DestroyImmediate(warmup);

            // 2026-09-11:全景俯瞰开关 IC_ART_OVERVIEW=1 —— 一帧覆盖 270×160 全图,
            // 便于整体判断铺装(局部截图看不出全貌,调试与验收都需要)。
            if (System.Environment.GetEnvironmentVariable("IC_ART_OVERVIEW") == "1")
            {
                cam.aspect = 16f / 9f;
                cam.orthographic = true;
                // 正俯视(制作人:不要让相机偏移;屏障已改为向内倾斜,俯视即可见)
                cam.orthographicSize = LaneGrid.Rows * 0.62f;
                cam.transform.position = new Vector3(LaneGrid.Cols * 0.5f, 200f, 3f - LaneGrid.Rows * 0.5f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                boot.Board.TickViews();
                if (fogView != null) fogView.RefreshNow();
                var fv = Object.FindFirstObjectByType<FogView>(); if (fv != null) fv.RefreshNow();
                SaveRender(cam, "D:/DevTools/shots/overview.png");
                Debug.Log("[CaptureBatch] overview saved");
                Object.DestroyImmediate(go);
                return;
            }

            for (int i = 0; i < Marks.Length; i++)
            {
                long targetTick = (long)(Marks[i] * boot.Sim.Config.TickRate);
                int guard = 0;
                while (boot.Sim.Tick < targetTick && guard++ < 100000) boot.Sim.Step();
                boot.Board.TickViews();
                if (fogView != null) fogView.RefreshNow();
                // 视图平滑收敛 + 持续交战(edit 模式 Time.deltaTime≈0,手工喂 dt;
                // 继续步进 5 tick 让 DamageDealt 在镜头前新鲜发生——飘字寿命 1s,停机渲染会错过)
                for (int k = 0; k < 5; k++)
                {
                    boot.Sim.Step();
                    boot.Board.TickViews();
                    if (fogView != null) fogView.RefreshNow();
                    foreach (var v in Object.FindObjectsOfType<EntityView>())
                        v.Tick(0.1f, cam);
                    if (floatText != null) floatText.RenderFrame(0.1f); // 飘字:同一收敛口径喂 dt
                    WallFx.RenderFrameAll(0.1f); // 屏障特效:edit 模式手动推进(2026-09-11)
                    if (wallHp != null) wallHp.Tick(cam); // 屏障血条:edit 模式手动刷新
                }

                // 战线跟随:优先对准交战单位(主将>士兵);无交战则固定机位
                var focus = FindBattleFocus(boot.Sim);
                float camX = focus?.X ?? CamX[i];
                float camRow = focus?.Y ?? 80f;   // 三路地图:默认对准中路
                if (ArtMode && rig != null)
                    rig.SetFocus(camX, camRow); // 玩家规范机位(旧代码里与直摆等价)
                else
                {
                    cam.transform.position = new Vector3(camX, 55f, 3f - camRow - 38f);
                    cam.transform.LookAt(new Vector3(camX, 0f, 3f - camRow));
                }

                // 2026-09-11 屏障特效演示(IC_WALL_FX=1):真实 AI 局到不了墙前(120s 内战线在河),
                // 用与 SimCore 同构的 SimEvent 注入确定性证据帧(涟漪中段/击碎碎片飞行/连击压力)。
                // 相机直接对准被演示的屏障曲面——原先沿用战线跟随机位,特效落在画外(本窗复核发现)。
                if (wallFxDemo)
                {
                    var fxSide = DemoWallFx(boot, i);
                    // IC_WALL_VIEW=player:走玩家规范机位(rig 拖到墙前一屏),用于判断"游戏里实际看到的样子";
                    // 默认(face):沿幕面法线正对取证,专看特效本身。
                    if (playerView)
                    {
                        var wc = WallSystem.PanelCenterPos(fxSide, 2);
                        if (rig != null) rig.SetFocus(wc.X + (fxSide == Side.Blue ? 14f : -14f), wc.Y);
                    }
                    else AimWallCamera(cam, fxSide);
                    WallFx.RenderFrameAll(0.05f);
                    if (wallHp != null) wallHp.Tick(cam);
                }

                string path = ArtMode
                    ? string.Format("D:/DevTools/shots/art_{0}_edit_{1}_t{2}s.png", ArtTag(), i + 1, (int)Marks[i])
                    : string.Format("D:/DevTools/shots/m6.2_{0}_t{1}s.png", i + 1, (int)Marks[i]);
                SaveRender(cam, path);
                Debug.Log("[CaptureBatch] " + path + " simTick=" + boot.Sim.Tick + " focus=" + (focus?.ToString() ?? "fixed")
                    + " floatTexts=" + (floatText != null ? floatText.ActiveCount.ToString() : "n/a")
                    + (seqMode ? " seqMode=1" : ""));

                // M3-S03: 序列模式——主截图后同机位再截 _s2/_s3 两帧。
                // 相机位置/朝向不动(不复算 focus),仅步进 Sim 让画面内容演进,保证像素级可比。
                if (seqMode)
                {
                    for (int s = 2; s <= 3; s++)
                    {
                        for (int t = 0; t < seqTicks; t++) boot.Sim.Step();
                        boot.Board.TickViews();
                        if (fogView != null) fogView.RefreshNow();
                        foreach (var v in Object.FindObjectsOfType<EntityView>())
                            v.Tick(0.1f, cam);
                        if (floatText != null) floatText.RenderFrame(0.1f);
                        WallFx.RenderFrameAll(0.1f); // 特效随序列帧推进(捕捉涟漪/碎片动态)

                        string seqPath = ArtMode
                            ? string.Format("D:/DevTools/shots/art_{0}_edit_{1}_t{2}s_s{3}.png", ArtTag(), i + 1, (int)Marks[i], s)
                            : string.Format("D:/DevTools/shots/m6.2_{0}_t{1}s_s{2}.png", i + 1, (int)Marks[i], s);
                        SaveRender(cam, seqPath);
                        Debug.Log("[CaptureBatch] " + seqPath + " simTick=" + boot.Sim.Tick);
                    }
                }
            }

            Object.DestroyImmediate(go);
            Debug.Log("[CaptureBatch] done" + (seqMode ? " (seq)" : ""));
        }

        /// <summary>
        /// 把相机架到屏障幕面**正前方**——沿该段曲面法线后退,不走路面跟随机位。
        /// 关键几何:碗沿幕面的法线是"水平外向 + 向上"倾斜(本项约 37° 仰角),所以机位必须
        /// 沿法线退开才能正对幕面;此前把相机放在基地正上方 26 米、水平只 8 米处,
        /// 视线与幕面近乎平行,画面里屏障被压成边缘一道薄片(本窗复核实测)。
        /// </summary>
        private static void AimWallCamera(Camera cam, Side side)
        {
            const int Panel = 2;                       // 中路:正对地图内侧,最能看清
            const float ViewHeight = 3.0f;             // 幕面中下部高度
            const float Dist = 38f;                    // 沿法线后退距离:要容下"幕面 0~7m + 血条"整段
            const float AimLift = 2.0f;                // 视点上抬,把幕顶与血条一起框进画面
            var p = WallView.SurfacePoint(side, Panel, 0.5f, ViewHeight);
            var n = WallView.SurfaceNormal(side, Panel, 0.5f, ViewHeight);
            cam.orthographic = false;
            cam.transform.position = p + n * Dist;
            cam.transform.LookAt(p + new Vector3(0f, AimLift, 0f));
            Debug.Log("[CaptureBatch] wall fx camera side=" + side + " pos=" + cam.transform.position + " look=" + p);
        }

        /// <summary>
        /// 屏障特效演示(IC_WALL_FX=1):每个 mark 注入确定性事件后推进一小段再截图,
        /// 让涟漪处于扩散中段、碎片处于飞行中段。事件结构与 SimCore 攻墙发射完全同构。
        /// 返回本帧要对准的墙方(相机随之对准该方屏障曲面)。**演示中段(2=中路)而非边段**:
        /// 中路正对镜头,击碎缺口/碎片一眼可见(此前演示 0 段,缺口在画面侧后方)。
        /// </summary>
        private static Side DemoWallFx(GameBootstrap boot, int mark)
        {
            var sim = boot.Sim;
            switch (mark)
            {
                case 0: // t10:蓝中段受击涟漪(单发,扩散中段)
                    sim.Emit(new SimEvent(sim.Tick, SimEventType.WallDamaged, 2, 30f, 0,
                        (int)Side.Blue, RippleHitPos(Side.Blue, 2)));
                    WallFx.RenderFrameAll(0.06f);   // 只推进 0.06s:核心闪光(0.12s)仍在画面里
                    return Side.Blue;

                case 1: // t30:蓝中段击碎(缺口+碎片沿弧飞散+光爆),同屏红中段涟漪作对照
                    sim.Emit(new SimEvent(sim.Tick, SimEventType.WallDamaged, 2, 30f, 0,
                        (int)Side.Red, RippleHitPos(Side.Red, 2)));
                    WallSystem.Damage(Side.Blue, 2, 9999f);      // Sim 与画面一致(该段真破)
                    sim.Emit(new SimEvent(sim.Tick, SimEventType.WallBroken, 2, 0f, 0, (int)Side.Blue));
                    WallFx.RenderFrameAll(0.15f);
                    return Side.Blue;

                case 2: // t60:蓝方连打两段(中段+上草段)→ 血条读数 + 缺口相邻
                    sim.Emit(new SimEvent(sim.Tick, SimEventType.WallDamaged, 2, 30f, 0,
                        (int)Side.Blue, RippleHitPos(Side.Blue, 2)));
                    WallSystem.Damage(Side.Blue, 1, 110f);        // 打到约 45% 血:血条读条可见
                    sim.Emit(new SimEvent(sim.Tick, SimEventType.WallDamaged, 1, 110f, 0,
                        (int)Side.Blue, RippleHitPos(Side.Blue, 1)));
                    WallFx.RenderFrameAll(0.13f);   // 环扩散中段 + 飘散光点已喷出
                    return Side.Blue;

                default: // t120:红中段击碎(镜像对照,顺带看红方配色是否还发白)
                    WallSystem.Damage(Side.Red, 2, 9999f);
                    sim.Emit(new SimEvent(sim.Tick, SimEventType.WallBroken, 2, 0f, 0, (int)Side.Red));
                    WallSystem.Damage(Side.Red, 3, 140f);
                    sim.Emit(new SimEvent(sim.Tick, SimEventType.WallDamaged, 3, 140f, 0,
                        (int)Side.Red, RippleHitPos(Side.Red, 3)));
                    WallFx.RenderFrameAll(0.2f);
                    return Side.Red;
            }
        }

        /// <summary>演示用命中点:取该段弧上略偏一点(模拟从侧面顶上来的一刀)。</summary>
        private static SimVec2 RippleHitPos(Side side, int panel)
        {
            var c = WallSystem.BaseCenter(side);
            float a = WallSystem.PanelAngle(side, panel) + WallSystem.PanelHalf(side, panel) * 0.35f;
            return new SimVec2(c.X + (float)System.Math.Cos(a) * WallSystem.Radius,
                c.Y + (float)System.Math.Sin(a) * WallSystem.Radius);
        }

        /// <summary>找当前交战点(Attacking 单位;主将优先,其次士兵);无交战返回 null。</summary>
        private static SimVec2? FindBattleFocus(SimCore sim)        {
            SimVec2? soldier = null;
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Unit || e.State != EntityState.Attacking) continue;
                if (!e.IsSoldier) return e.PosF;
                if (soldier == null) soldier = e.PosF;
            }
            return soldier;
        }

        private static void SaveRender(Camera cam, string path)
        {
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
