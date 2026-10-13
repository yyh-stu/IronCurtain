using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>
    /// 2026-09-14 新增:**真实播放器内的交互验收驱动器**。
    ///
    /// 为什么必须有它:这台机器的桌面背景一直在动(整屏截图一到手就"过期"),桌面控制的
    /// 坐标点击被工具面永久拒绝 → 真鼠标点不进去。而制作人点名的问题(道具点了没用、海克斯能看不能抽、
    /// 换边还是坏的)**全都是交互问题**,只跑仿真探针证明不了。
    /// 所以这里在**独立的 exe** 里驱动游戏自己:按钮走它们真实的回调(UiKit 挂在 userData 上那一份),
    /// 时间线走真实 Sim.Step,截图走 ScreenCapture(播放器里可用、且**含 UITK**),
    /// 判定走可比较的数字(面板矩形/组件计数/相机机位/Sim 状态)。
    ///
    /// 触发:环境变量 IC_ACCEPT=swap|items|prod|capture|all,场景用 -executeMethod 无关;
    /// 入口是 RuntimeInitializeOnLoadMethod,只认环境变量,正常玩家跑不到。
    /// 输出:D:/DevTools/shots/acc_*.png 与 acc_*.txt
    /// </summary>
    public static class IcAcceptance
    {
        private const string Dir = "D:/DevTools/shots";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var mode = System.Environment.GetEnvironmentVariable("IC_ACCEPT");
            if (string.IsNullOrEmpty(mode)) return;
            var go = new GameObject("IcAcceptance");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().Mode = mode;
        }

        private sealed class Runner : MonoBehaviour
        {
            public string Mode = "all";
            private readonly List<string> _log = new List<string>();

            private void L(string s) { _log.Add(s); Debug.Log("[accept] " + s); }

            private void Flush(string name)
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Dir + "/acc_" + name + ".txt", string.Join("\n", _log.ToArray()));
            }

            private IEnumerator Start()
            {
                yield return null;
                // 播放器默认从 Menu 起,自检需要直接进对局:由环境变量 IC_MODE 指定模式 →
                // 自己设置 AppFlow(静态,跨场景保持)后直接载入战斗场景,等价于玩家点了练习场。
                if (Mode == "audio") DumpAudio("① 主界面(进对局前)");
                StartCoroutine(Watchdog());   // 兜底:自检若因异常中途断掉,15 分钟后也强制收尾
                var want = System.Environment.GetEnvironmentVariable("IC_MODE");
                if (!string.IsNullOrEmpty(want) && Boot0() == null)
                {
                    AppFlow.Current = want == "practice" ? AppFlow.Mode.Practice
                        : want == "simple" ? AppFlow.Mode.AiSimple
                        : want == "hard" ? AppFlow.Mode.AiHard : AppFlow.Mode.AiSimple;
                    AppFlow.PlayerSide = Side.Blue;
                    AppFlow.BpBluePicks = AppFlow.BpRedPicks = null;
                    var bpEnv = System.Environment.GetEnvironmentVariable("IC_BP_PICKS");
                    if (!string.IsNullOrEmpty(bpEnv))
                    {
                        AppFlow.BpBluePicks = ParseHeroes(bpEnv);
                        AppFlow.BpRedPicks = new List<HeroId> { HeroId.PanShi, HeroId.ZhenYue, HeroId.XingLuo };
                        L("自检注入 BP 选将:蓝方=" + Join(AppFlow.BpBluePicks));
                    }
                    // 注:走"练习场"入口会清空 BP 结果(AppFlow.StartPractice),所以**注入 BP 选将时跳过菜单**,
                    // 直接载入战斗场景 —— 那正是"BP 选完进对局"的真实状态。
                    if (!string.IsNullOrEmpty(bpEnv))
                    {
                        UnityEngine.SceneManagement.SceneManager.LoadScene(AppFlow.BattleScenePath);
                        for (int i = 0; i < 240 && Boot0() == null; i++) yield return null;
                        yield return new WaitForSecondsRealtime(1.5f);
                        goto afterEnter;
                    }
                    // 2026-09-14:优先走**真实路径** —— 点主界面上的入口按钮(验证 MenuBootstrap→AppFlow 路由)。
                    // 点不到才退回直接载场景,免得把"路由坏掉"和"自检进不去"混在一起。
                    var menuLabel = AppFlow.Current == AppFlow.Mode.Practice ? "练习场"
                        : AppFlow.Current == AppFlow.Mode.AiSimple ? "简单" : "困难";
                    Button menuBtn = null;
                    for (int i = 0; i < 180 && menuBtn == null; i++)   // 主界面 UI 要几帧才建起来
                    {
                        menuBtn = FindButton(UiRoot.Current != null ? UiRoot.Current.Screen : null, menuLabel);
                        if (menuBtn == null) yield return null;
                    }
                    if (menuBtn == null)
                    {
                        var root = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                        int nb = 0; string first = "";
                        CollectButtons(root, ref nb, ref first);
                        L("  主界面查找失败:UiRoot.Current=" + (UiRoot.Current != null ? "有" : "空")
                            + " 树根=" + (root != null ? "有" : "空")
                            + " 树里按钮数=" + nb + " 前几个=" + first);
                    }
                    if (Click(menuBtn, "主界面入口(" + menuLabel + ")", _log))
                    {
                        for (int i = 0; i < 240 && Boot0() == null; i++) yield return null;
                        yield return new WaitForSecondsRealtime(1.5f);
                        if (Boot0() != null) { L("主界面按钮 → 已进入对局(真实路由)"); goto afterEnter; }
                        L("主界面按钮点了但没进对局 —— 退回直接载场景");
                    }
                    L("自检直入对局 mode=" + AppFlow.Current + " 场景=" + AppFlow.BattleScenePath);
                    UnityEngine.SceneManagement.SceneManager.LoadScene(AppFlow.BattleScenePath);
                    yield return null;
                    yield return null;
                    yield return new WaitForSecondsRealtime(1.5f);
                    afterEnter: ;
                }
                if (Mode == "swap" || Mode == "all") yield return Guard(RunSwap(), "RunSwap");
                // 信息面板要**先跑**:后面的场景会把仿真推进 200+ 秒,侦察精灵那时已把地图探开,
                // 就没有"未侦察过"的敌方建筑了(那样拍不到"？"那张)。
                if (Mode == "pause" || Mode == "all") yield return Guard(RunPause(), "RunPause");
                // 2026-09-20 GPT②:朝向专项(八方向移动/停止保持/攻击朝目标/静止出生)
                if (Mode == "facing" || Mode == "all") yield return Guard(RunFacing(), "RunFacing");
                if (Mode == "prod3" || Mode == "all") yield return Guard(RunProdThree(), "RunProdThree");
                if (Mode == "ui" || Mode == "all") yield return Guard(RunPanelStability(), "RunPanelStability");
                if (Mode == "ui" || Mode == "all") yield return Guard(RunPracticeEp(), "RunPracticeEp");
                if (Mode == "ui" || Mode == "all") yield return Guard(RunMeleeSfx(), "RunMeleeSfx");
                if (Mode == "ui" || Mode == "all") yield return Guard(RunBottomUiSize(), "RunBottomUiSize");
                if (Mode == "ui" || Mode == "all") yield return Guard(RunOrderTowerAttack(), "RunOrderTowerAttack");
                if (Mode == "ui" || Mode == "all") yield return Guard(RunPanelRebuild(), "RunPanelRebuild");
                // 2026-09-15 制作人「点敌方建筑无法展开面板 / 框选自己军队失灵」的定性与回归
                if (Mode == "ui" || Mode == "all") yield return Guard(RunUiHitAudit(), "RunUiHitAudit");
                if (Mode == "info" || Mode == "all") yield return Guard(RunInfoPanel(), "RunInfoPanel");
                if (Mode == "items" || Mode == "all") yield return Guard(RunItems(), "RunItems");
                // 2026-09-16 三十七轮:交接任务"为每张海克斯写一条实机判据(释放→断言 sim 状态变化)"
                if (Mode == "hexfx" || Mode == "all") yield return Guard(RunHexEffects(), "RunHexEffects");
                // 2026-09-21 视觉返修单:①血盾一体 ②磐石盾贴臂 ③镇岳握锤(runtime 求值取证)
                if (Mode == "vr") yield return Guard(RunVrVisual(), "RunVrVisual");
                if (Mode == "prod" || Mode == "all") yield return Guard(RunProd(), "RunProd");
                if (Mode == "capture" || Mode == "all") yield return Guard(RunCapture(), "RunCapture");
                // 「点开没画面」用的首屏自检:必须**不带 IC_MODE** 单独跑(等于双击 exe 看到的第一个画面)
                if (Mode == "menu") yield return Guard(RunFirstScreen(), "RunFirstScreen");
                // BGM 叠放排查:主界面 → 对局 → 返回大厅,每步枚举场上所有 AudioSource
                if (Mode == "audio") yield return Guard(RunAudio(), "RunAudio");
                Flush(Mode);
                Debug.Log("[accept] done " + Mode);
                Application.Quit(0);
            }

            /// <summary>
            /// 2026-09-15:给每个场景套一层"抛异常也不会把整条自检链打断"的壳。
            /// 事故现场:`IC_ACCEPT=all` 跑到 `RunItems` 里 `SpawnEntity` 抛 `出生点过近`,
            /// 协程里的异常直接终止 `Start()` → **既不 Flush 也不 Quit** → 播放器永久挂着、
            /// 也不产出日志(只能靠 15 分钟看门狗兜底),看起来像"自检卡死",实际是被一个用例摆位问题弄崩的。
            /// 现在:异常记一行 `!!场景[...]抛异常` 并**跳过该场景继续跑**,其余判据照常产出。
            /// </summary>
            private IEnumerator Guard(IEnumerator inner, string name)
            {
                while (true)
                {
                    bool moved;
                    object cur = null;
                    try
                    {
                        moved = inner.MoveNext();
                        if (moved) cur = inner.Current;
                    }
                    catch (System.Exception ex)
                    {
                        L("  !! 场景[" + name + "]抛异常 → 跳过并继续:" + ex.GetType().Name + " " + ex.Message);
                        yield break;
                    }
                    if (!moved) yield break;
                    yield return cur;
                }
            }

            /// <summary>
            /// 首屏自检(制作人报"点开没画面"):独立播放器启动后**不进对局**,只看第一画面是否真有东西。
            /// 判据:当前场景名、场景根部物体数、UI 按钮数与前几个按钮文字、相机数量与是否在渲染、
            /// 渲染帧统计(累计帧数/三角面/可见渲染器),外加一张 `acc_menu.png` 供目视。
            /// 用法:`IC_ACCEPT=menu`(不要带 IC_MODE),双击 exe 等价于此。
            /// </summary>
            private IEnumerator RunFirstScreen()
            {
                L("---- 首屏(双击 exe 的第一画面) ----");
                yield return new WaitForSecondsRealtime(3f);   // 等 UI 与场景物体建起来
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                L("  分辨率=" + Screen.width + "x" + Screen.height + " 当前场景=" + scene.name
                    + " 已加载=" + scene.isLoaded + " 根部物体数=" + scene.rootCount);

                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                int nb = 0; string first = "";
                CollectButtons(ui, ref nb, ref first);
                L("  UiRoot=" + (UiRoot.Current != null ? "有" : "空") + " UI 树=" + (ui != null ? "有" : "空")
                    + " 按钮数=" + nb + " 前几个=" + first);

                int cams = CountOf<Camera>();
                var cam = Camera.main;
                L("  相机数=" + cams + " 主相机=" + (cam != null ? (cam.name + " @" + cam.transform.position.ToString("F1")) : "无")
                    + (cam != null ? (" 视野内渲染器=" + (cam.enabled ? "相机启用" : "相机禁用")) : ""));

                var rig = cam != null ? cam.GetComponent<CameraRig>() : null;
                L("  CameraRig=" + (rig != null ? "有" : "无"));
                L("  ★判据:场景已加载=" + (scene.isLoaded && scene.rootCount > 0 ? "是" : "否")
                    + " ★判据:有相机=" + (cams > 0 && cam != null ? "是" : "否")
                    + " ★判据:首屏有 UI 按钮=" + (nb > 0 ? "是" : "否"));
                Shot("acc_menu");

                // 2026-09-14 制作人报"点开没画面":点[AI 对战]会先去 **BP 选将场景**(AppFlow.StartGame
                // → BpScenePath),而我以往所有实机自检都走 `IC_MODE=practice`(直达战斗场景)或直接用
                // `IC_BP_PICKS` 载入战斗场景 —— **BP 场景从没在独立播放器里跑过**。这里补上:
                // 点真按钮 → 等它切场景 → 把 BP 场景里的画面、UI、相机、渲染帧全部记下来 + 截图。
                var aiBtn = FindButton(ui, "AI 对战 · 困难");
                if (aiBtn != null)
                {
                    Click(aiBtn, "AI 对战 · 困难", _log);
                    for (int i = 0; i < 300; i++) yield return null;      // 等场景切换与 UI 构建
                    yield return new WaitForSecondsRealtime(2f);
                    var s2 = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    int nb2 = 0; string f2 = "";
                    CollectButtons(UiRoot.Current != null ? UiRoot.Current.Screen : null, ref nb2, ref f2);
                    int cams2 = CountOf<Camera>();
                    L("  点[AI 对战 · 困难]后 场景=" + s2.name + " 根部物体=" + s2.rootCount
                        + " 相机数=" + cams2 + " UI 按钮数=" + nb2 + " 前几个=" + f2);
                    L("  ★判据:BP/战斗入口有画面(场景已加载且有相机)="
                        + (s2.isLoaded && s2.rootCount > 0 && cams2 > 0 ? "是" : "否"));
                    Shot("acc_entry_ai");
                }
                else L("  !! 主界面上找不到『AI 对战 · 困难』按钮");
                yield return null;
            }

            /// <summary>
            /// 把场上**所有** AudioSource 列出来:曲子名、是否在播、音量、是否循环、所在场景、物体名。
            /// 排查制作人报的"BGM 重复播放/进游戏后主菜单音乐没停":只要看对局里还有没有 `bgm_menu` 在播。
            /// </summary>
            private void DumpAudio(string stage)
            {
                var all = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
                int playing = 0;
                var peaks = new List<string>();
                foreach (var s in all)
                    if (s != null && s.isPlaying) { playing++; peaks.Add(s.clip != null ? s.clip.name : "(无clip)"); }
                L("  [" + stage + "] AudioSource 总数=" + all.Length + " 正在播=" + playing
                    + (peaks.Count > 0 ? " 曲子=" + string.Join(",", peaks) : ""));
                foreach (var s in all)
                {
                    if (s == null) continue;
                    L("    源 " + s.gameObject.name + " clip=" + (s.clip != null ? s.clip.name : "无")
                        + " 在播=" + s.isPlaying + " 音量=" + s.volume.ToString("F2") + " 循环=" + s.loop
                        + " 场景=" + s.gameObject.scene.name);
                }
            }

            private IEnumerator RunAudio()
            {
                L("---- BGM 叠放排查 ----");
                var boot = Boot0();
                if (boot == null) { L("  !! 没有战斗场景(IC_MODE 未设置?)"); yield break; }
                yield return new WaitForSecondsRealtime(1.5f);
                DumpAudio("② 对局中");
                bool menuBgmInBattle = false, earlyOn = false;
                foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                {
                    if (s == null || !s.isPlaying || s.clip == null) continue;
                    if (s.clip.name == "bgm_menu") menuBgmInBattle = true;
                    if (s.clip.name == "bgm_early") earlyOn = true;
                }
                L("  ★判据:对局中菜单曲已停(bgm_menu 不在播)=" + (menuBgmInBattle ? "否" : "是")
                    + " ★判据:对局曲在播(bgm_early)=" + (earlyOn ? "是" : "否"));
                Shot("acc_audio_battle");

                // ④ 后期曲切换(两阶段 BGM 交叉淡化):直接让一座敌建筑易主触发,等 4 秒(淡化 2.5s)后看音量
                var bsim = Boot0().Sim;
                int captured = 0;
                foreach (var kv in bsim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Building) continue;
                    if (e.Side == AppFlow.PlayerSide || e.Side == Side.Neutral) continue;
                    e.Side = AppFlow.PlayerSide;    // 等价于"占领敌方建筑"(控制器条件二)
                    captured = e.Id;
                    break;
                }
                L("  已触发后期曲条件(让敌建筑 id=" + captured + " 易主),等 4 秒看交叉淡化结果");
                yield return new WaitForSecondsRealtime(4f);
                DumpAudio("④ 后期曲切换后");
                float vEarly = -1f, vLate = -1f;
                foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                {
                    if (s == null || s.clip == null) continue;
                    if (s.clip.name == "bgm_early") vEarly = s.volume;
                    if (s.clip.name == "bgm_late") vLate = s.volume;
                }
                L("  ★判据:切换后只有后期曲在响(early=0,late>0)="
                    + (vEarly <= 0.01f && vLate > 0.01f ? "是" : "否") + "(early=" + vEarly.ToString("F2")
                    + " late=" + vLate.ToString("F2") + ")");
                Shot("acc_audio_late");

                // ⑤ 练习场换边(重建视图):最容易再造出第二条对局 BGM 的路径
                Click(FindButton(UiRoot.Current != null ? UiRoot.Current.Screen : null, "切换操控"), "切换操控", _log);
                for (int i = 0; i < 300; i++) yield return null;
                yield return new WaitForSecondsRealtime(2.5f);
                DumpAudio("⑤ 换边之后");
                int battleSrc = 0;
                foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                    if (s != null && s.isPlaying && s.clip != null && s.clip.name.StartsWith("bgm_")) battleSrc++;
                L("  ★判据:换边后只有 1 条 BGM 在播=" + (battleSrc == 1 ? "是" : "否") + "(实际 " + battleSrc + " 条)");
                Shot("acc_audio_swap");

                // 返回大厅:反向也要干净(大厅曲起、对局曲停)
                AppFlow.EnterMenu();
                for (int i = 0; i < 240 && (Boot0() != null || MenuPanelAlive() == false); i++) yield return null;
                yield return new WaitForSecondsRealtime(2f);
                DumpAudio("③ 返回大厅后");
                bool menuOn = false, battleOn = false;
                foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                {
                    if (s == null || !s.isPlaying || s.clip == null) continue;
                    if (s.clip.name == "bgm_menu") menuOn = true;
                    if (s.clip.name.StartsWith("bgm_")) battleOn |= s.clip.name != "bgm_menu";
                }
                L("  ★判据:大厅曲在播=" + (menuOn ? "是" : "否")
                    + " ★判据:对局曲已停=" + (battleOn ? "否" : "是"));
                Shot("acc_audio_menu");
            }

            private static bool MenuPanelAlive()
            {
                return Object.FindFirstObjectByType<MenuPanel>() != null;
            }

            /// <summary>
            /// 看门狗(2026-09-14 实测教训):自检协程里只要有一处抛异常,迭代器就**停在那里**,
            /// 后面的 Flush/Quit 永远走不到 → 播放器挂死(本次就是这样卡住的)。这里给个硬兜底:
            /// 超过 15 分钟仍未退出 → 落盘日志 + 退出。
            /// </summary>
            private IEnumerator Watchdog()
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 900f) yield return new WaitForSecondsRealtime(5f);
                L("  !! 看门狗触发:自检超 15 分钟未收尾(多半是某处异常打断了协程),强制落盘退出");
                Flush(Mode + "_watchdog");
                Application.Quit(3);
            }

            private static GameBootstrap Boot0()
            {
                var b = Object.FindFirstObjectByType<GameBootstrap>();
                return b;
            }

            private static void Shot(string name)
            {
                Directory.CreateDirectory(Dir);
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { Debug.LogWarning("[accept] 截图失败 " + name); return; }
                File.WriteAllBytes(Dir + "/" + name + ".png", tex.EncodeToPNG());
                Object.Destroy(tex);
                Debug.Log("[accept] 截图 " + name);
            }

            private static int CountOf<T>() where T : Component
            {
                var all = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
                return all.Length;
            }

            private static int CountTransforms(string name)
            {
                int n = 0;
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t != null && t.name == name) n++;
                return n;
            }

            /// <summary>在 UITK 树里按文字找按钮。</summary>
            private static Button FindButton(VisualElement root, string contains)
            {
                if (root == null) return null;
                var b = root as Button;
                if (b != null && !string.IsNullOrEmpty(b.text) && Squash(b.text).Contains(Squash(contains))) return b;
                foreach (var c in root.Children())
                {
                    var r = FindButton(c, contains);
                    if (r != null) return r;
                }
                return null;
            }

            /// <summary>去掉空白后再比 —— 主界面入口的文案是「练 习 场(无 AI · 可换边)」这种字间带空格的排版。</summary>
            private static string Squash(string s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                var sb = new System.Text.StringBuilder(s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == ' ' || c == ' ' || c == '　' || c == '	') continue;
                    sb.Append(c);
                }
                return sb.ToString();
            }

            private static void CollectButtons(VisualElement root, ref int n, ref string first)
            {
                if (root == null) return;
                var b = root as Button;
                if (b != null)
                {
                    n++;
                    if (n <= 8) first += "[" + b.text + "]";
                }
                foreach (var c in root.Children()) CollectButtons(c, ref n, ref first);
            }

            /// <summary>找到"含该文字的标签"所在行,再取该行里的按钮 ——
            /// 道具背包的行是「标签=物品名+消耗」+「按钮=选中」,按行找才不会错。</summary>
            private static Button FindButtonInRow(VisualElement root, string labelContains)
            {
                var label = FindLabel(root, labelContains);
                if (label == null) return null;
                var p = label.parent;
                for (int i = 0; i < 4 && p != null; i++)
                {
                    var b = FindButton(p, "");
                    if (b != null) return b;
                    p = p.parent;
                }
                return null;
            }

            private static Label FindLabel(VisualElement root, string contains)
            {
                if (root == null) return null;
                var l = root as Label;
                if (l != null && !(l is Button) && !string.IsNullOrEmpty(l.text) && Squash(l.text).Contains(Squash(contains))) return l;
                foreach (var c in root.Children())
                {
                    var r = FindLabel(c, contains);
                    if (r != null) return r;
                }
                return null;
            }

            /// <summary>按钮走它自己的回调(UiKit.MakeButton 把原回调挂在 userData 上)。</summary>
            private static bool Click(Button b, string tag, List<string> log)
            {
                if (b == null) { log.Add("  点击失败:找不到按钮 " + tag); return false; }
                var act = b.userData as System.Action;
                if (act != null) { act(); log.Add("  点击 " + tag + "(\"" + b.text + "\") 已派发"); return true; }
                var cl = b.clickable;
                var m = cl.GetType().GetMethod("SimulateSingleClick",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m != null)
                {
                    m.Invoke(cl, m.GetParameters().Length == 1 ? new object[] { null } : new object[] { null, 100 });
                    log.Add("  点击 " + tag + "(SimulateSingleClick) 已派发");
                    return true;
                }
                log.Add("  点击失败:" + tag + " 既无 userData 也无 SimulateSingleClick");
                return false;
            }

            // ================= VR 视觉返修单(2026-09-21)=================
            // ①血盾一体(五状态+蓝红+双镜头+填充顶点在外框内断言)
            // ②磐石盾贴臂(runtime 求值:待机/走/攻击连续帧+盾竖直/离臂判据)
            // ③镇岳握锤(握拳 mesh 应用+掌心接触+连续帧)
            private IEnumerator RunVrVisual()
            {
                L("---- VR 视觉返修:①血盾一体 ②磐石盾贴臂 ③镇岳握锤 ----");
                var boot = Boot0();
                if (boot == null || boot.Sim == null) { L("  !! 没有战斗场景(需要 IC_MODE=practice)"); yield break; }
                var sim = boot.Sim;
                var player = AppFlow.PlayerSide;
                var board = boot.Board;
                if (board == null) { L("  !! 没有 BoardView"); yield break; }

                // ---------- ① VR01:血盾一体 ----------
                var u = sim.SpawnEntity(player, EntityKind.Unit, new GridPos(80, 100), false);
                u.Hero = HeroId.PanShi; u.IsSoldier = false; u.MoveSpeed = 0f; u.Hp = u.MaxHp = 100f; u.Shield = 0f; u.ShieldMax = 60f;
                u.AutoKind = 0;
                sim.AnnounceSpawn(u);
                sim.Step(); yield return null; yield return null;
                var uv = board.GetView(u.Id);
                Transform BarOf(string child) => uv != null ? uv.transform.Find("HpBar/" + child) : null;
                var fill = BarOf("Fill"); var sh = BarOf("Shield"); var back = BarOf("Back");
                if (fill == null || sh == null || back == null)
                {
                    L("  !! 血条件缺失 Fill=" + (fill != null) + " Shield=" + (sh != null) + " Back=" + (back != null));
                }
                else
                {
                    System.Func<bool> inside = () =>
                    {
                        var bb = back.GetComponent<Renderer>().bounds;
                        bb.Expand(0.02f * Mathf.Max(bb.extents.x, bb.extents.y));
                        var fr = fill.GetComponent<Renderer>();
                        if (fr != null && fr.gameObject.activeInHierarchy
                            && (!(bb.Contains(fr.bounds.min) && bb.Contains(fr.bounds.max)))) return false;
                        if (sh.gameObject.activeInHierarchy)
                        {
                            var sr = sh.GetComponent<Renderer>();
                            if (sr != null && !(bb.Contains(sr.bounds.min) && bb.Contains(sr.bounds.max))) return false;
                        }
                        return true;
                    };
                    var cases = new (float hp, float s, string tag)[] {
                        (100f, 50f, "full_shield"), (60f, 20f, "low_shield"),
                        (75f, 0f, "zero_shield"), (100f, 200f, "over_shield") };
                    foreach (var c in cases)
                    {
                        u.Hp = c.hp; u.Shield = c.s;
                        sim.Step(); yield return null; yield return null;
                        bool ok = inside();
                        Shot("vr01_" + c.tag);
                        L("  [" + c.tag + "] Hp=" + u.Hp + " Shield=" + u.Shield + " 盾段宽=" + sh.localScale.x.ToString("F2")
                            + " active=" + sh.gameObject.activeSelf + " 填充在外框内=" + (ok ? "是" : "否"));
                        L("  ★判据:VR01_" + c.tag + "_护盾在血槽内=" + (ok ? "是" : "否"));
                    }
                    // 破盾后
                    u.Hp = 55f; u.Shield = 30f; sim.Step(); yield return null;
                    u.Shield = 0f; sim.Step(); yield return null; yield return null;
                    bool okB = inside();
                    Shot("vr01_after_break");
                    L("  ★判据:VR01_破盾后_护盾在血槽内=" + (okB ? "是" : "否"));
                    // 红方单位
                    var r = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 98));
                    r.Hero = HeroId.ZhenYue; r.MoveSpeed = 0f; r.Hp = 80f; r.MaxHp = 100f; r.Shield = 25f; r.ShieldMax = 60f;
                    r.Attack = 0f; r.AutoKind = 0;
                    sim.Step(); yield return null; yield return null;
                    Shot("vr01_red_unit");
                    L("  [red] 红方单位 Hp=80/100 Shield=25(阵营色血条+灰盾段)");
                    sim.RemoveEntity(r.Id);
                    // 双镜头:近景(独立相机,不干扰 CameraRig)
                    yield return ShotCloseup(uv.transform.position, "vr01_closeup", 5.5f, 1.6f);
                    sim.RemoveEntity(u.Id);
                }

                // ---------- ② VR02:磐石盾(runtime 求值) ----------
                var ps = sim.SpawnEntity(player, EntityKind.Unit, new GridPos(78, 102), false);
                ps.Hero = HeroId.PanShi; ps.IsSoldier = false; ps.MoveSpeed = 0f; ps.Hp = ps.MaxHp = 400f; ps.AutoKind = 0;
                ps.Attack = 30f; ps.AttackIntervalSec = 1.2f; ps.Range = 2.5f;   // Attack<=0 会被 CombatUpdate 直接 continue(实测 AttackSeq=0 的根因)
                sim.AnnounceSpawn(ps);
                sim.Step(); yield return null; yield return null;
                var psv = board.GetView(ps.Id);
                Shot("vr02_ps_idle_game");
                yield return ShotCloseup(psv.transform.position, "vr02_ps_idle_close", 3.2f, 1.25f);
                float idleTilt = ShieldTiltDeg(psv);
                L("  [idle] 盾纵轴与竖直夹角=" + idleTilt.ToString("F1") + "°(判据<20°)");
                L("  ★判据:VR02_待机盾近竖直=" + (idleTilt < 20f ? "是" : "否"));
                // runtime 待机前臂帧 dump(盾贴臂微调依据:局部三轴的世界方向 + 盾 socket 相对位置)
                foreach (var a2 in psv.GetComponentsInChildren<Animator>())
                {
                    var fa = a2.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                    if (fa == null) continue;
                    L("  [臂dump] 前臂pos=" + fa.position.ToString("F2")
                        + " X(世界)=" + (fa.rotation * Vector3.right).ToString("F2")
                        + " Y(世界)=" + (fa.rotation * Vector3.up).ToString("F2")
                        + " Z(世界)=" + (fa.rotation * Vector3.forward).ToString("F2"));
                    Transform wsq = null;
                    foreach (var t3 in psv.GetComponentsInChildren<Transform>())
                        if (t3.name.StartsWith("WS_磐石_盾")) wsq = t3;
                    if (wsq != null)
                        L("  [臂dump] 盾socket(前臂局部)=" + fa.InverseTransformPoint(wsq.position).ToString("F3")
                            + " 世界=" + wsq.position.ToString("F2"));
                }
                // 走:连续 8 帧(带 Sim 时间戳)
                ps.MoveSpeed = 3f;
                sim.SetMoveTarget(ps.Id, new GridPos(78, 93));
                float shieldMaxOff = 0f;
                for (int i = 0; i < 8; i++)
                {
                    for (int t = 0; t < 10; t++) sim.Step();
                    yield return null;
                    Shot("vr02_ps_walk_" + i);
                    shieldMaxOff = Mathf.Max(shieldMaxOff, ShieldOffArmDist(psv));
                }
                L("  [walk] 8帧 盾离前臂最大距离=" + shieldMaxOff.ToString("F3") + "m(判据<0.4m 不脱臂)");
                L("  ★判据:VR02_行走盾不离臂=" + (shieldMaxOff < 0.4f ? "是" : "否"));
                // 攻击:放敌人→连续 10 帧(前摇/接触/收招)
                var foe1 = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(78, 96));
                foe1.Attack = 0f; foe1.MoveSpeed = 0f; foe1.Hp = foe1.MaxHp = 3000f; foe1.AutoKind = 0;
                for (int t = 0; t < 120 && ps.State != EntityState.Attacking; t++) sim.Step();
                if (ps.State != EntityState.Attacking)
                {
                    ps.State = EntityState.Attacking;   // 自动接触未触发时强设(同 RunFacing;攻击发射由 CombatUpdate 真实执行)
                    ps.CurrentTargetId = foe1.Id;
                    ps.HasMoveTarget = false;
                }
                yield return null; yield return null;
                float atkTiltMax = 0f; float atkOffMax = 0f; long atkSeq = 0;
                for (int i = 0; i < 10; i++)
                {
                    // View 定向测试:每 step 压回攻击态(练习场 auto behavior 会重路由移动);
                    // AttackSeq 由 CombatUpdate 真实递增,发射本身走正式路径
                    for (int t = 0; t < 8; t++)
                    {
                        ps.State = EntityState.Attacking; ps.CurrentTargetId = foe1.Id; ps.HasMoveTarget = false;
                        sim.Step();
                    }
                    yield return null;
                    Shot("vr02_ps_attack_" + i);
                    if (i == 4) yield return ShotCloseup(psv.transform.position, "vr02_ps_attack_close", 3.2f, 1.25f);
                    atkTiltMax = Mathf.Max(atkTiltMax, ShieldTiltDeg(psv));
                    atkOffMax = Mathf.Max(atkOffMax, ShieldOffArmDist(psv));
                    atkSeq = ps.AttackSeq;
                }
                L("  [attack] 10帧 State=" + ps.State + " AttackSeq=" + atkSeq
                    + " 盾倾角最大=" + atkTiltMax.ToString("F1") + "° 离臂最大=" + atkOffMax.ToString("F3") + "m");
                L("  ★判据:VR02_攻击期发生真实攻击=" + (atkSeq > 0 ? "是(seq=" + atkSeq + ")" : "否"));
                L("  ★判据:VR02_攻击期盾不脱臂=" + (atkOffMax < 0.4f ? "是" : "否"));
                L("  ★判据:VR02_攻击期盾不翻面成横躺=" + (atkTiltMax < 60f ? "是" : "否(倾角" + atkTiltMax.ToString("F0") + "°)"));

                // ---------- ③ VR03:镇岳握锤(runtime 求值) ----------
                var zy = sim.SpawnEntity(player, EntityKind.Unit, new GridPos(76, 102), false);
                zy.Hero = HeroId.ZhenYue; zy.IsSoldier = false; zy.MoveSpeed = 0f; zy.Hp = zy.MaxHp = 400f; zy.AutoKind = 0;
                zy.Attack = 30f; zy.AttackIntervalSec = 1.2f; zy.Range = 2.5f;
                sim.AnnounceSpawn(zy);
                sim.Step(); yield return null; yield return null;
                var zyv = board.GetView(zy.Id);
                Shot("vr03_zy_idle_game");
                yield return ShotCloseup(zyv.transform.position, "vr03_zy_idle_close", 3.2f, 1.35f);
                // dump:镇岳子树(WS socket / SMR mesh),定位挂接状态
                L("  [dump] zyv 子Transform=" + zyv.GetComponentsInChildren<Transform>().Length);
                foreach (var t in zyv.GetComponentsInChildren<Transform>())
                    if (t.name.StartsWith("WS_") || t.name.Contains("锤")) L("    ws: " + t.name);
                foreach (var smr in zyv.GetComponentsInChildren<SkinnedMeshRenderer>())
                    L("    smr: " + smr.name + " mesh=" + (smr.sharedMesh != null ? smr.sharedMesh.name + "#" + smr.sharedMesh.vertexCount : "null"));
                // 握拳 mesh 应用判据(取最大顶点 SMR)
                string meshName = "?"; int bigVerts = 0;
                foreach (var smr in zyv.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (smr.sharedMesh != null && smr.sharedMesh.vertexCount > bigVerts)
                    { bigVerts = smr.sharedMesh.vertexCount; meshName = smr.sharedMesh.name; }
                L("  [idle] 主SMR mesh=" + meshName + "(应为 ZhenYue_grip)");
                L("  ★判据:VR03_握拳mesh已应用=" + (meshName == "ZhenYue_grip" ? "是" : "否"));
                // 接触:掌心-socket
                float contact = HammerPalmDist(zyv);
                L("  [idle] 锤握点-掌心距离=" + contact.ToString("F4") + "m(判据<0.023m=身高1%)");
                L("  ★判据:VR03_锤柄穿掌心=" + (contact < 0.023f ? "是" : "否"));
                // 走 + 攻击连续帧
                zy.MoveSpeed = 3f;
                sim.SetMoveTarget(zy.Id, new GridPos(76, 96));
                for (int i = 0; i < 6; i++)
                {
                    for (int t = 0; t < 10; t++) sim.Step();
                    yield return null;
                    Shot("vr03_zy_walk_" + i);
                }
                var foe2 = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(76, 93));
                foe2.Attack = 0f; foe2.MoveSpeed = 0f; foe2.Hp = foe2.MaxHp = 3000f; foe2.AutoKind = 0;
                for (int t = 0; t < 120 && zy.State != EntityState.Attacking; t++) sim.Step();
                if (zy.State != EntityState.Attacking)
                {
                    zy.State = EntityState.Attacking;
                    zy.CurrentTargetId = foe2.Id;
                    zy.HasMoveTarget = false;
                }
                yield return null; yield return null;
                float cMax = 0f; long zySeq = 0;
                for (int i = 0; i < 10; i++)
                {
                    for (int t = 0; t < 8; t++)
                    {
                        zy.State = EntityState.Attacking; zy.CurrentTargetId = foe2.Id; zy.HasMoveTarget = false;
                        sim.Step();
                    }
                    yield return null;
                    Shot("vr03_zy_attack_" + i);
                    if (i == 4) yield return ShotCloseup(zyv.transform.position, "vr03_zy_attack_close", 3.2f, 1.35f);
                    cMax = Mathf.Max(cMax, HammerPalmDist(zyv));
                    zySeq = zy.AttackSeq;
                }
                L("  [attack] 10帧 State=" + zy.State + " AttackSeq=" + zySeq + " 掌心接触最大距离=" + cMax.ToString("F4") + "m");
                L("  ★判据:VR03_攻击期发生真实攻击=" + (zySeq > 0 ? "是(seq=" + zySeq + ")" : "否"));
                L("  ★判据:VR03_攻击全程锤不滑柄(" + cMax.ToString("F3") + "m<0.046m=身高2%)=" + (cMax < 0.046f ? "是" : "否"));

                sim.RemoveEntity(ps.Id); sim.RemoveEntity(zy.Id);
                sim.RemoveEntity(foe1.Id); sim.RemoveEntity(foe2.Id);
            }

            /// <summary>盾 socket 纵轴与世界竖直的夹角(度);找不到盾时返回 999。</summary>
            private static float ShieldTiltDeg(EntityView v)
            {
                foreach (var t in v.GetComponentsInChildren<Transform>())
                    if (t.name.StartsWith("WS_磐石_盾"))
                        return Vector3.Angle(t.up, Vector3.up);
                return 999f;
            }

            /// <summary>盾挂点与左前臂骨的世界距离(贴臂/脱臂判定)。</summary>
            private static float ShieldOffArmDist(EntityView v)
            {
                Transform ws = null;
                foreach (var t in v.GetComponentsInChildren<Transform>())
                    if (t.name.StartsWith("WS_磐石_盾")) ws = t;
                if (ws == null) return 999f;
                foreach (var a in v.GetComponentsInChildren<Animator>())
                {
                    var fa = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                    if (fa != null) return Vector3.Distance(ws.position, fa.position);
                }
                return 999f;
            }

            /// <summary>锤握点(socket)与右掌心的世界距离。socket 挂掌心定义上恒 0,>0 即配置漂移。</summary>
            private static float HammerPalmDist(EntityView v)
            {
                Transform ws = null;
                foreach (var t in v.GetComponentsInChildren<Transform>())
                    if (t.name.StartsWith("WS_镇岳")) ws = t;
                if (ws == null) return 999f;
                foreach (var a in v.GetComponentsInChildren<Animator>())
                {
                    var hd = a.GetBoneTransform(HumanBodyBones.RightHand);
                    if (hd != null)
                        return Vector3.Distance(ws.position, hd.TransformPoint(new Vector3(-0.0015f, 0.1238f, 0.0422f)));
                }
                return 999f;
            }

            /// <summary>独立相机特写(不干扰 CameraRig):正面/背面各拍一张(防单角度被地形遮挡成灰图)。</summary>
            private IEnumerator ShotCloseup(Vector3 worldPos, string name, float dist, float height)
            {
                var cam = Camera.main;
                if (cam == null) yield break;
                var go = new GameObject("VrShotCam");
                var c2 = go.AddComponent<Camera>();
                c2.CopyFrom(cam);
                c2.targetTexture = null;
                c2.enabled = false;
                for (int face = 0; face < 2; face++)
                {
                    float dir = face == 0 ? -1f : 1f;   // 先正面(z-)再背面(z+)
                    c2.transform.position = worldPos + new Vector3(dist * 0.35f, height, dist * dir);
                    c2.transform.LookAt(worldPos + Vector3.up * height);
                    yield return new WaitForEndOfFrame();
                    c2.Render();
                    var bytes = RTBytes(c2, 900, 900);
                    if (bytes != null)
                    {
                        Directory.CreateDirectory(Dir);
                        File.WriteAllBytes(Dir + "/" + name + (face == 0 ? "" : "_back") + ".png", bytes);
                        Debug.Log("[accept] 特写 " + name + (face == 0 ? "" : "_back"));
                    }
                }
                Object.Destroy(go);
                yield return null;
            }

            private static byte[] RTBytes(Camera c, int w, int h)
            {
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                c.targetTexture = rt;
                c.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                var bytes = tex.EncodeToPNG();
                RenderTexture.active = null;
                c.targetTexture = null;
                Object.Destroy(tex);
                Object.Destroy(rt);
                return bytes;
            }

            // ================= 朝向专项(GPT②) =================
            private IEnumerator RunFacing()
            {
                L("---- 朝向专项(八方向/停止保持/攻击朝目标/静止出生) ----");
                var boot = Boot0();
                if (boot == null || boot.Sim == null) { L("  !! 没有战斗场景"); yield break; }
                var sim = boot.Sim;
                var player = AppFlow.PlayerSide;
                var board = boot.Board;
                if (board == null) { L("  !! 没有 BoardView"); yield break; }

                // ① 静止出生:产一个将→首帧→朝向应为默认±90°(不误判为大位移)
                var idle = sim.SpawnEntity(player, EntityKind.Unit, new GridPos(80, 100));
                idle.Hero = HeroId.PanShi; idle.MoveSpeed = 0f; idle.Hp = idle.MaxHp = 400f;
                sim.Step(); yield return null;
                var idleView = board.GetView(idle.Id);
                float idleYaw = idleView != null ? idleView.transform.eulerAngles.y : 999f;
                float expectIdle = player == Side.Blue ? 90f : -90f;
                L("  ①静止出生: yaw=" + idleYaw.ToString("F0") + " 期望=" + expectIdle.ToString("F0")
                    + (Mathf.Abs(Mathf.DeltaAngle(idleYaw, expectIdle)) < 5f ? " √" : " ×"));
                L("  ★判据:静止出生不误判朝向=" + (Mathf.Abs(Mathf.DeltaAngle(idleYaw, expectIdle)) < 5f ? "是" : "否"));

                // ② 八方向移动:下令往 8 个方向走,每步验模型朝向与行进方向一致
                var mover = sim.SpawnEntity(player, EntityKind.Unit, new GridPos(80, 95));   // 不与 idle 同格(出生点过近)
                mover.Hero = HeroId.ZhenYue; mover.MoveSpeed = 2f; mover.Hp = mover.MaxHp = 400f;
                int dirOk = 0, dirTotal = 0;
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * Mathf.PI * 2f / 8f;
                    int dx = Mathf.RoundToInt(Mathf.Cos(ang) * 10);
                    int dy = Mathf.RoundToInt(Mathf.Sin(ang) * 10);
                    var dest = new GridPos(80 + dy, 95 + dx);  // Sim row=Y, col=X
                    if (!TerrainMap.IsInside(dest)) continue;
                    sim.SetMoveTarget(mover.Id, dest);
                    for (int t = 0; t < 30; t++) sim.Step();   // 走3秒
                    yield return null;
                    var mv = board.GetView(mover.Id);
                    float yaw = mv != null ? mv.transform.eulerAngles.y : 999f;
                    float expected = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;  // Sim(dx,dy)→Unity(dx,0,-dy)
                    bool ok = Mathf.Abs(Mathf.DeltaAngle(yaw, expected)) < 20f;
                    dirTotal++; if (ok) dirOk++;
                    if (k == 0 || k == 2 || k == 4 || k == 6)
                        L("  ②方向" + k + ": 朝(" + dx + "," + dy + ") yaw=" + yaw.ToString("F0") + " 期望=" + expected.ToString("F0") + (ok ? " √" : " ×"));
                }
                L("  ★判据:八方向移动朝向≥6/8正确=" + (dirOk >= 6 ? "是(" + dirOk + "/" + dirTotal + ")" : "否(" + dirOk + "/" + dirTotal + ")"));

                // ③ 停止保持:到达后不 Step sim(避免 auto behavior 干扰),只过渲染帧
                for (int t = 0; t < 200 && mover.HasMoveTarget; t++) sim.Step();
                mover.State = EntityState.Idle;
                mover.TargetBuildingId = 0; mover.AutoKind = 0; mover.AutoTargetId = 0;
                yield return null;
                for (int t = 0; t < 90; t++) yield return null;   // 90帧≈1.5秒,让 LerpAngle 完全收敛
                float yawBefore = board.GetView(mover.Id) != null ? board.GetView(mover.Id).transform.eulerAngles.y : 0f;
                for (int t = 0; t < 30; t++) yield return null;   // 30 帧纯渲染,Sim 不动
                float yawAfter = board.GetView(mover.Id) != null ? board.GetView(mover.Id).transform.eulerAngles.y : 0f;
                bool holdOk = Mathf.Abs(Mathf.DeltaAngle(yawBefore, yawAfter)) < 5f;
                L("  ③停止保持: 停前=" + yawBefore.ToString("F0") + " 停后=" + yawAfter.ToString("F0") + (holdOk ? " √" : " ×"));
                L("  ★判据:停止后朝向保持不变=" + (holdOk ? "是" : "否"));

                // ④ 攻击朝目标:靠近敌兵→进入攻击态→朝向应朝敌兵(非默认方向)
                var foePos = new GridPos(mover.Pos.Row, mover.Pos.Col + 1);
                var foe = sim.SpawnEntity(Side.Red, EntityKind.Unit, foePos);
                foe.Attack = 0f; foe.MoveSpeed = 0f; foe.Hp = foe.MaxHp = 2000f;
                for (int t = 0; t < 30; t++) sim.Step();
                if (mover.State != EntityState.Attacking)
                {
                    mover.State = EntityState.Attacking;
                    mover.CurrentTargetId = foe.Id;
                    mover.HasMoveTarget = false;
                }
                for (int t = 0; t < 10; t++) sim.Step();
                yield return null;
                yield return null;
                var atkView = board.GetView(mover.Id);
                float atkYaw = atkView != null ? atkView.transform.eulerAngles.y : 999f;
                // mover 在 (80,~105),foe 在 (80,110) → 应朝 Sim(0,+5) → yaw=Atan2(0,-5)=180
                float expectAtk = foe != null ? Mathf.Atan2(foe.PosF.X - mover.PosF.X, -(foe.PosF.Y - mover.PosF.Y)) * Mathf.Rad2Deg : 0f;
                bool atkOk = Mathf.Abs(Mathf.DeltaAngle(atkYaw, expectAtk)) < 20f;
                L("  ④攻击朝目标: State=" + mover.State + " CurrentTargetId=" + mover.CurrentTargetId
                    + " yaw=" + atkYaw.ToString("F0") + " 期望=" + expectAtk.ToString("F0") + (atkOk ? " √" : " ×"));
                L("  ★判据:攻击时朝向实际目标=" + (atkOk ? "是" : "否"));

                // 5(side-back attack): A attacks B while C hits from side -> face stays toward B
                var extraFoe = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(mover.Pos.Row - 3, mover.Pos.Col));
                extraFoe.Attack = 0.1f; extraFoe.MoveSpeed = 0f; extraFoe.Hp = extraFoe.MaxHp = 2000f;
                extraFoe.Range = 5f; extraFoe.AttackIntervalSec = 1f;
                for (int t = 0; t < 30; t++) sim.Step();
                yield return null;
                yield return null;
                var atkView5 = board.GetView(mover.Id);
                float yaw5 = atkView5 != null ? atkView5.transform.eulerAngles.y : 999f;
                bool face5 = mover.CurrentTargetId == foe.Id && Mathf.Abs(Mathf.DeltaAngle(yaw5, expectAtk)) < 20f;
                L("  [5]side-back: yaw=" + yaw5.ToString("F0") + " expect=" + expectAtk.ToString("F0")
                    + " CurrentTarget=" + mover.CurrentTargetId + (face5 ? " OK" : " X"));
                L("  ★判据:攻击B时被C侧后方打不转向C=" + (face5 ? "是" : "否"));
                sim.RemoveEntity(extraFoe.Id);

                // 6(spawn no attack trigger): fresh entity AttackSeq=0 -> no SetTrigger on first frame
                var fresh = sim.SpawnEntity(player, EntityKind.Unit, new GridPos(75, 95));
                fresh.Hero = HeroId.DuanFeng; fresh.MoveSpeed = 0f; fresh.Hp = fresh.MaxHp = 400f;
                long seqBefore = fresh.AttackSeq;
                sim.Step();
                yield return null;
                L("  [6]spawn-no-attack: AttackSeq=" + seqBefore + " (should=0)");
                L("  ★判据:静止出生不触发攻击动画=" + (seqBefore == 0 ? "是" : "否"));
                sim.RemoveEntity(fresh.Id);

                // cleanup
                sim.RemoveEntity(idle.Id);
                sim.RemoveEntity(mover.Id);
                sim.RemoveEntity(foe.Id);
            }

            // ================= 换边 =================
            private IEnumerator RunSwap()
            {
                L("---- 换边(训练场原地换边) ----");
                var boot = Boot0();
                if (boot == null || boot.Sim == null) { L("  !! 没有战斗场景(需要 IC_MODE=practice 进练习场)"); yield break; }
                yield return null;
                var camBefore = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                var rotBefore = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;
                var sideBefore = AppFlow.PlayerSide;
                var rigBefore = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
                L("  换边前 操控方=" + sideBefore + " 相机=" + Fmt(camBefore)
                    + " UIDocument=" + CountOf<UIDocument>() + " 小地图=" + CountOf<MinimapPanel>()
                    + " 屏障血条=" + CountOf<WallHealthView>() + " WallHpBars物体=" + CountTransforms("WallHpBars")
                    + " SelPaths=" + CountTransforms("SelPaths") + " simTick=" + boot.Sim.Tick
                    + " 有个小地图在画=" + (CountOf<MinimapPanel>() > 0));
                Shot("acc_swap_before");

                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                var btn = FindButton(ui, "切换操控");
                Click(btn, "切换操控", _log);

                // 两段式重建:第 N 帧拆、第 N+1 帧建 → 等几帧再看
                for (int i = 0; i < 6; i++) yield return null;
                var camAfter = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                var rigAfter = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
                float camDelta = Vector3.Distance(camBefore, camAfter);
                L("  换边后 操控方=" + sideBefore + "→" + AppFlow.PlayerSide + " 相机=" + Fmt(camAfter)
                    + " UIDocument=" + CountOf<UIDocument>() + " 小地图=" + CountOf<MinimapPanel>()
                    + " 屏障血条=" + CountOf<WallHealthView>() + " WallHpBars物体=" + CountTransforms("WallHpBars")
                    + " SelPaths=" + CountTransforms("SelPaths") + " simTick=" + boot.Sim.Tick
                    + " Board=" + (boot.Board != null ? "有" : "无"));
                // 判据(制作人口径:换边只切阵营与视野状态,**不改相机位置**)
                L("  ★判据:操控方是否切换=" + (AppFlow.PlayerSide != sideBefore ? "是" : "否")
                    + " 相机是否未动=" + (camDelta <= 0.01f ? "是" : "否(位移 " + camDelta.ToString("F2") + "m)")
                    + " 相机旋转是否未动=" + (Quaternion.Angle(rotBefore, Camera.main.transform.rotation) <= 0.01f ? "是" : "否")
                    + " rig 是否同一个=" + (ReferenceEquals(rigBefore, rigAfter) ? "是" : "否")
                    + " 是否没多出副本=" + (CountOf<UIDocument>() == 1 && CountOf<MinimapPanel>() == 1
                        && CountTransforms("WallHpBars") == 1 ? "是" : "否")
                    + " 迷雾平面数=" + CountTransforms("FogQuad") + "(必须恒为 1,否则叠暗)");
                yield return new WaitForSecondsRealtime(1.0f);
                Shot("acc_swap_after");

                // 再切回来(连续切换不能再叠一份;相机仍应原地不动)
                var camMid = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                var ui2 = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                Click(FindButton(ui2, "切换操控"), "切换操控(切回)", _log);
                for (int i = 0; i < 6; i++) yield return null;
                var camBack = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                L("  切回后 操控方=" + AppFlow.PlayerSide + " UIDocument=" + CountOf<UIDocument>()
                    + " 小地图=" + CountOf<MinimapPanel>() + " WallHpBars物体=" + CountTransforms("WallHpBars")
                    + " 迷雾平面数=" + CountTransforms("FogQuad")
                    + " simTick=" + boot.Sim.Tick
                    + " 相机是否仍未动=" + (Vector3.Distance(camMid, camBack) <= 0.01f ? "是" : "否"));

                // 频繁换边(制作人:"频繁切换阵营时全地图异常变暗")—— 连切 4 次,迷雾平面不得累积
                for (int k = 0; k < 4; k++)
                {
                    var uiK = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                    Click(FindButton(uiK, "切换操控"), "连续换边#" + (k + 1), _log);
                    for (int i2 = 0; i2 < 4; i2++) yield return null;
                }
                L("  连切 4 次后 操控方=" + AppFlow.PlayerSide + " 迷雾平面数=" + CountTransforms("FogQuad")
                    + " UIDocument=" + CountOf<UIDocument>() + " 小地图=" + CountOf<MinimapPanel>()
                    + " WallHpBars物体=" + CountTransforms("WallHpBars")
                    + " ★判据:迷雾平面恒为1=" + (CountTransforms("FogQuad") == 1 ? "是" : "否"));
                yield return new WaitForSecondsRealtime(1.0f);
                Shot("acc_swap_rapid");
                yield return new WaitForSecondsRealtime(1.0f);
                Shot("acc_swap_back");
            }

            // ================= 道具 / 海克斯 =================
            private IEnumerator RunItems()
            {
                L("---- 道具背包 / 海克斯 ----");
                var boot = Boot0();
                if (boot == null || boot.Sim == null) { L("  !! 没有战斗场景"); yield break; }
                var sim = boot.Sim;
                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;

                // ① 攒够 EP:推进仿真到 EP>=3(EP 按 90 秒 1 点;道具 1 点/次,海克斯抽取 2 点)
                AdvanceUntil(sim, () => sim.EpOf(AppFlow.PlayerSide) >= 3f, 400.0);
                L("  仿真=" + SimSec(sim) + "s EP=" + sim.EpOf(AppFlow.PlayerSide));
                var tool = boot.GetComponent<ToolCommandPanel>();

                // ② 海克斯先做(窗口是限时的,只有 10 秒):推进到"在窗口内且 EP 够"
                var hex = boot.GetComponent<HexCommandPanel>();
                var probe = new HexCommandModel(sim, AppFlow.PlayerSide);
                AdvanceUntil(sim, () => HexSystem.DrawWindowMinuteOf(sim) > 0
                    && sim.EpOf(AppFlow.PlayerSide) >= probe.Describe().DrawCost, 400.0);
                yield return null;
                var hexBtn = FindButton(ui, "海克斯");
                Click(hexBtn, "海克斯入口", _log);
                yield return null;
                Shot("acc_hex_panel");
                var state0 = probe.Describe();
                L("  仿真=" + SimSec(sim) + "s EP=" + state0.Ep + " 在窗口(第" + state0.WindowMinute + "分钟)="
                    + state0.InWindow + " 可抽=" + state0.CanDraw + " 下一窗口=" + state0.NextDrawMinute);
                var draw = FindButton(ui, "抽取三张");
                Click(draw, "抽取三张", _log);
                yield return null;
                var state1 = probe.Describe();
                var pick = FindButton(ui, "选择「");
                L("  抽取后 候选=" + state1.PendingChoices.Count + " 面板上的『选择』按钮=" + (pick != null)
                    + "(这就是原来『能看不能抽』的现场:三选一按钮出不来)");
                Shot("acc_hex_drawn");

                // ③ 真按钮点三选一 → 手牌;再点释放
                Click(pick, "三选一第一张", _log);
                yield return null;
                var state2 = probe.Describe();
                var release = FindButton(ui, "释放「");
                L("  选择后 手牌=" + state2.Hand.Count + " 面板上的『释放』按钮=" + (release != null));
                Shot("acc_hex_hand");

                // ④ 道具背包:打开道具入口(真按钮)
                var toolBtn = FindButton(ui, "道具");
                Click(toolBtn, "道具入口", _log);
                yield return null;
                yield return null;   // 等一帧布局落定,世界矩形才是最终值
                var slot = FindButtonInRow(ui, "攻城锥");
                L("  道具背包渲染=" + (slot != null) + "(攻城锥那一行找到了按钮) 面板矩形=" + RectStr(ToolCommandPanel.LastPanelRect)
                    + " EP=" + sim.EpOf(AppFlow.PlayerSide));
                Shot("acc_tool_panel");

                // ⑤ 点背包里的道具 → 进入目标模式
                Click(slot, "攻城锥(背包格)", _log);
                yield return null;
                L("  目标模式 HasPendingTarget=" + (tool != null && tool.HasPendingTarget));

                // ⑥ 让一座敌方建筑进入视野(派己方单位过去),再用世界点击"点敌方建筑"
                int redB = 0; SimVec2 redPos = default;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e.Kind == EntityKind.Building && e.Side == Side.Red) { redB = e.Id; redPos = e.PosF; break; }
                }
                AdvanceUntil(sim, () => sim.EpOf(AppFlow.PlayerSide) >= 1f, 200.0);
                var scout = SpawnAtFreeCell(sim, AppFlow.PlayerSide, EntityKind.Unit,
                    new GridPos((int)redPos.Y, (int)(redPos.X - 6f)), "RunItems·侦察");
                if (scout == null) { L("  !! 侦察单位生不出来,跳过道具世界点击"); yield break; }
                scout.Hero = HeroId.PanShi; scout.IsSoldier = false;
                for (int i = 0; i < 3 * sim.Config.TickRate; i++) sim.Step();
                yield return null;
                // 2026-09-16 三十七轮:目标模式战场可发现性判据(制作人"不知道怎么用"的修复验证)
                yield return new WaitForSecondsRealtime(0.35f);   // TargetingFeedback 0.25s 重算合法集
                bool hintOk = !string.IsNullOrEmpty(TargetingFeedback.HintText);
                bool feedbackOk = TargetingFeedback.MarkCount > 0 && hintOk && TargetingFeedback.CursorActive;
                L("  目标模式战场反馈:目标环数=" + TargetingFeedback.MarkCount
                    + " 提示行=\"" + (TargetingFeedback.HintText ?? "-") + "\" 准星=" + TargetingFeedback.CursorActive);
                L("  ★判据:目标模式战场反馈(高亮+提示+光标)=" + (feedbackOk ? "是" : "否"));
                Shot("acc_tool_targetfx");
                float hpBefore = sim.GetEntity(redB) != null ? sim.GetEntity(redB).Hp : -1f;
                bool handled = tool != null && tool.TryHandleWorldClick(true, new SimVec2(redPos.X, redPos.Y));
                yield return null;
                float hpAfter = sim.GetEntity(redB) != null ? sim.GetEntity(redB).Hp : -1f;
                L("  世界点击敌方建筑#" + redB + " 被道具消费=" + handled
                    + " 建筑血 " + hpBefore.ToString("F1") + "→" + hpAfter.ToString("F1")
                    + " 反馈=\"" + (tool != null ? tool.LastFeedback : "-") + "\"");
                L("  点完后是否仍在目标模式=" + (tool != null && tool.HasPendingTarget) + "(成功应回到 false)");
                yield return new WaitForSecondsRealtime(0.35f);   // 等 TargetingFeedback 撤标记
                L("  用掉后战场反馈清零:目标环数=" + TargetingFeedback.MarkCount
                    + " 提示行=" + (TargetingFeedback.HintText != null ? "仍在(错)" : "已撤")
                    + " ★判据:成功使用后反馈清零=" + (TargetingFeedback.MarkCount == 0 && TargetingFeedback.HintText == null ? "是" : "否"));
                Shot("acc_tool_used");

                // ⑦ 小地图选目标:重新选中道具 → 用小地图坐标点同一座建筑
                AdvanceUntil(sim, () => sim.EpOf(AppFlow.PlayerSide) >= 1f, 200.0);
                var slot2 = FindButtonInRow(ui, "攻城锥");
                Click(slot2, "攻城锥(背包格,第二次)", _log);
                yield return null;
                float hp2 = sim.GetEntity(redB) != null ? sim.GetEntity(redB).Hp : -1f;
                bool mmHandled = tool != null && tool.TryHandleMinimapClick(redPos);
                yield return null;
                L("  小地图点同一建筑 被道具消费=" + mmHandled
                    + " 建筑血 " + hp2.ToString("F1") + "→"
                    + (sim.GetEntity(redB) != null ? sim.GetEntity(redB).Hp.ToString("F1") : "已毁")
                    + " 反馈=\"" + (tool != null ? tool.LastFeedback : "-") + "\"");
                Shot("acc_tool_minimap");

                // ⑧ 临时阵地:真实放置(2026-09-16 拍板规则:三路/己方广场/最前建筑视野)+ 地堡渲染判据(1c)
                AdvanceUntil(sim, () => sim.EpOf(AppFlow.PlayerSide) >= 1f, 200.0);
                Entity baseEnt = null;
                foreach (var kv in sim.Entities)
                {
                    var b = kv.Value;
                    if (b != null && b.Kind == EntityKind.Building && b.Side == AppFlow.PlayerSide
                        && b.Building == BuildingId.Base) { baseEnt = b; break; }
                }
                bool placedF = false; string fMsg = "-"; bool fortHintShown = false;
                if (baseEnt != null)
                {
                    // 候选点:己方基地同排(=中路)向敌侧逐个试,被占/失败就重选道具换下一个
                    float bx = baseEnt.PosF.X, by = baseEnt.PosF.Y;
                    int dir = AppFlow.PlayerSide == Side.Blue ? 1 : -1;
                    foreach (var dx in new[] { 6f, 10f, 14f, 18f, 22f, 26f, 30f })
                    {
                        var slotF = FindButtonInRow(ui, "临时阵地");
                        Click(slotF, "临时阵地(背包格)", _log);
                        yield return null;
                        yield return new WaitForSecondsRealtime(0.35f);   // 等反馈组件与提示行
                        // 提示行必须在**瞄准中**读:放置成功后目标模式退出、提示随之撤下
                        if (TargetingFeedback.HintText != null && TargetingFeedback.HintText.Contains("临时阵地"))
                            fortHintShown = true;
                        var pt = new SimVec2(bx + dir * dx, by);
                        if (!TerrainMap.IsInside(pt)) continue;
                        bool consumedF = tool != null && tool.TryHandleWorldClick(true, pt);
                        yield return null;
                        if (tool != null && !tool.HasPendingTarget && consumedF)
                        { placedF = true; fMsg = tool.LastFeedback ?? "-"; break; }
                        AdvanceUntil(sim, () => sim.EpOf(AppFlow.PlayerSide) >= 1f, 200.0);
                    }
                }
                Entity fortEnt = null;
                foreach (var kv in sim.Entities)
                {
                    var b = kv.Value;
                    if (b != null && b.IsFort && b.Side == AppFlow.PlayerSide) { fortEnt = b; break; }
                }
                bool bunkerRender = false;
                if (fortEnt != null && boot.Board != null)
                {
                    var fv = boot.Board.GetView(fortEnt.Id);
                    if (fv != null)
                        foreach (var t in fv.GetComponentsInChildren<Transform>())
                            if (t.name.StartsWith("Bunker") || t.name.Contains("地堡")) { bunkerRender = true; break; }
                }
                L("  临时阵地:放置=" + (placedF ? "成功" : "失败") + " 反馈=\"" + fMsg + "\""
                    + " 目标模式提示行=" + (fortHintShown ? "有" : "无")
                    + " 堡垒id=" + (fortEnt != null ? fortEnt.Id : 0)
                    + " 渲染=" + (bunkerRender ? "地堡(非箭塔)√" : (fortEnt != null ? "仍是箭塔 ×" : "未生成 ×")));
                // 2026-09-17:镜头对准堡垒再拍 —— 此前截图相机停在别处、时机又赶在目标模式收起前,目视证据不可用
                var rigF = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
                if (fortEnt != null && rigF != null) rigF.SetFocus(fortEnt.PosF.X, fortEnt.PosF.Y);
                for (int k = 0; k < 10; k++) yield return null;   // 镜头过渡 + 目标模式收起都走完
                Shot("acc_tool_fort");
                L("  ★判据:临时阵地可按新规则放置=" + (placedF ? "是" : "否")
                    + " ★判据:堡垒渲染走地堡而非箭塔=" + (bunkerRender ? "是" : "否"));
                yield break;
            }

            private static double SimSec(SimCore sim) => sim.Tick / (double)sim.Config.TickRate;

            // ================= 海克斯逐卡实机判据(三十七轮交接任务) =================
            /// <summary>每个抽取窗口走**真按钮链**(抽取→三选一→释放),对当次抽到的卡断言一条
            /// sim 状态签名。抽到什么由 RNG 决定,判据口径 = "本局抽到的卡全部生效";
            /// 多跑几局可累积覆盖 8 张卡。沙包需先单选单位,本场景不抽它(规则层 HexTests.T09 已覆盖)。
            /// 第 3 分钟节点已被道具场景用掉,这里用 5/7/9 三个窗口。</summary>
            private IEnumerator RunHexEffects()
            {
                L("---- 海克斯逐卡实机判据 ----");
                var boot = Boot0();
                if (boot == null || boot.Sim == null) { L("  !! 没有战斗场景"); yield break; }
                var sim = boot.Sim;
                var player = AppFlow.PlayerSide;
                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                int released = 0, verified = 0;
                var seen = new List<string>();
                foreach (int minute in new[] { 5, 7, 9 })
                {
                    AdvanceUntil(sim, () => HexSystem.DrawWindowMinuteOf(sim) == minute, 700);
                    if (HexSystem.DrawWindowMinuteOf(sim) != minute)
                    { L("  第" + minute + "分钟窗口未到达,跳过"); continue; }
                    Click(FindButton(ui, "海克斯"), "海克斯入口", _log);
                    for (int k = 0; k < 3; k++) yield return null;
                    Click(FindButton(ui, "抽取三张"), "抽取三张", _log);
                    for (int k = 0; k < 3; k++) yield return null;
                    var choice = FirstChoiceButton(ui, "沙包");
                    if (choice == null)
                    {
                        L("  第" + minute + "分钟:三选一只有沙包(需单选单位),跳过释放");
                        Click(FindButton(ui, "海克斯"), "海克斯(收起)", _log);
                        continue;
                    }
                    Click(choice, "三选一 " + choice.text, _log);
                    for (int k = 0; k < 3; k++) yield return null;
                    var rel = FindButton(ui, "释放「");
                    Click(rel, "释放 " + (rel != null ? rel.text : "?"), _log);
                    for (int k = 0; k < 3; k++) yield return null;

                    var card = HexSystem.ActiveOf(sim, player);
                    if (card == null)
                    {
                        L("  第" + minute + "分钟 释放失败(Active 仍为空),反馈链异常");
                        Click(FindButton(ui, "海克斯"), "海克斯(收起)", _log);
                        continue;
                    }
                    released++;
                    seen.Add(card.ToString());
                    string sig;
                    bool ok = HexCardSignature(sim, player, card.Value, out sig);
                    if (ok) verified++;
                    L("  第" + minute + "分钟 释放=" + card + " 生效签名:" + sig + (ok ? " √" : " ×"));
                    Shot("acc_hexfx_" + minute);
                    Click(FindButton(ui, "海克斯"), "海克斯(收起)", _log);
                    for (int k = 0; k < 3; k++) yield return null;
                }
                L("  本局抽到并释放:" + (seen.Count > 0 ? string.Join(",", seen) : "无")
                    + "(第3分钟节点由道具场景占用)");

                // ===== 补两张卡的确定性判据(训练场自由抽取 → 定向抽卡,与 RNG 无关;2026-09-17) =====
                if (sim.HexFreeDraw)
                {
                    var model = new HexCommandModel(sim, player);
                    Side foe = player == Side.Blue ? Side.Red : Side.Blue;

                    // 陷阵之志:定向抽到 → 释放 → 制造一次己方主将阵亡 → 断言友军输出加成(每层+5%)
                    if (DrawUntil(sim, player, HexId.Vengeance, 40))
                    {
                        AssertQuiet(model.Use(HexId.Vengeance, null), "释放陷阵之志");
                        var victim = SpawnAtFreeCell(sim, player, EntityKind.Unit, new GridPos(80, 60), "hexfx·victim");
                        var killer = SpawnAtFreeCell(sim, foe, EntityKind.Unit, new GridPos(80, 62), "hexfx·killer");
                        if (victim != null && killer != null)
                        {
                            victim.Hero = HeroId.PanShi; victim.Hp = victim.MaxHp = 60f; victim.MoveSpeed = 0f; victim.Attack = 0f;
                            killer.Attack = 80f; killer.AttackIntervalSec = 1f; killer.Range = 2f; killer.MoveSpeed = 0f;
                            killer.Hp = killer.MaxHp = 9999f;
                            int guard = 0;
                            while (victim.State != EntityState.Dead && guard++ < 200) sim.Step();
                            float bonus = 0f;
                            foreach (var kv in sim.Entities)
                            {
                                var e2 = kv.Value;
                                if (e2 != null && e2.Side == player && e2.Kind == EntityKind.Unit && !e2.IsSoldier
                                    && e2.State != EntityState.Dead && e2.Id != victim.Id)
                                { bonus = HexSystem.OutputBonusOf(sim, e2); break; }
                            }
                            bool vok = victim.State == EntityState.Dead && bonus > 0.01f;
                            L("  定向:陷阵之志释放后一名主将阵亡 → 幸存友军输出加成=" + bonus.ToString("F2") + "(应 +0.05/层)"
                                + " ★判据:陷阵之志阵亡叠层实机生效=" + (vok ? "是" : "否"));
                        }
                        if (killer != null) sim.RemoveEntity(killer.Id);   // 工具单位不留给后续场景
                    }

                    // 沙包:定向抽到 → 以单选口径释放 → 断言目标承伤减半/输出减半
                    Entity sandbagTarget = null;
                    foreach (var kv in sim.Entities)
                    {
                        var e2 = kv.Value;
                        if (e2 != null && e2.Side == player && e2.Kind == EntityKind.Unit && !e2.IsSoldier
                            && e2.State != EntityState.Dead) { sandbagTarget = e2; break; }
                    }
                    if (sandbagTarget != null && DrawUntil(sim, player, HexId.Sandbag, 40))
                    {
                        var fb = model.Use(HexId.Sandbag, new List<int> { sandbagTarget.Id });
                        float dr = HexSystem.DamageReductionOf(sim, sandbagTarget);
                        float ob = HexSystem.OutputBonusOf(sim, sandbagTarget);
                        bool sok = fb.Success && dr < 0.6f && ob < -0.4f;
                        L("  定向:沙包以单选口径释放(目标#" + sandbagTarget.Id + ") 承伤乘子=" + dr.ToString("F2")
                            + "(应 0.5) 输出加成=" + ob.ToString("F2") + "(应 -0.5)"
                            + " ★判据:沙包对选中单位实机生效=" + (sok ? "是" : "否"));
                        for (int w = 0; w < 320; w++) sim.Step();   // 等沙包过期(30s),不给后续场景留减益
                    }
                }

                L("  ★判据:抽到的海克斯释放后全部生效="
                    + (released > 0 && verified == released ? "是(" + verified + "/" + released + ")" : "否(" + verified + "/" + released + ")"));
            }

            /// <summary>训练场自由抽取定向抽卡:反复 抽取→三选一,直到目标卡入手(每轮 Step 一次让 EP 补满)。</summary>
            private static bool DrawUntil(SimCore sim, Side side, HexId want, int maxDraws)
            {
                for (int i = 0; i < maxDraws; i++)
                {
                    if (HexSystem.HandOf(sim, side).Contains(want)) return true;
                    var d = HexSystem.TryDraw(sim, side);
                    if (!d.Success) return false;
                    HexSystem.TryChoose(sim, side, d.Choices.Contains(want) ? want : d.Choices[0]);
                    sim.Step();   // EP 覆盖值按 tick 补满,两连抽之间走一拍
                }
                return HexSystem.HandOf(sim, side).Contains(want);
            }

            private static void AssertQuiet(HexCommandFeedback fb, string what)
            {
                if (!fb.Success)
                    Debug.Log("[accept] 定向释放失败(" + what + "):" + fb.Message);
            }

            /// <summary>三选一按钮里取第一个非避开卡(沙包释放需先单选单位,本场景不覆盖)。</summary>
            private static Button FirstChoiceButton(VisualElement ui, string avoid)
            {
                Button best = null;
                ui.Query<Button>().ToList().ForEach(b =>
                {
                    if (best != null || string.IsNullOrEmpty(b.text) || !b.text.StartsWith("选择「")) return;
                    if (!string.IsNullOrEmpty(avoid) && b.text.Contains(avoid)) return;
                    best = b;
                });
                return best;
            }

            /// <summary>逐卡签名:用**游戏决策层真实在读的查询**断言(查询→真实系统的接线由
            /// HexIntegrationTests 的真实路径用例钉死,这里验的是实机释放链 + 当前对局状态)。
            /// 找不到可测单位时降级为"激活确认"并在签名里说明,不冒充通过。</summary>
            private static bool HexCardSignature(SimCore sim, Side player, HexId card, out string sig)
            {
                Entity anyOwn = null, heavy = null;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Side != player || e.Kind != EntityKind.Unit || e.IsSoldier
                        || e.State == EntityState.Dead) continue;
                    if (anyOwn == null) anyOwn = e;
                    if (heavy == null && e.Hero != null && e.Hero.HasValue)
                    {
                        var h = sim.Game.Heroes[(int)e.Hero.Value];
                        if (h != null && h.Tag != null && h.Tag.StartsWith("重装")) heavy = e;
                    }
                }
                var nearBldg = OwnUnitNearOwnBuilding(sim, player);
                switch (card)
                {
                    case HexId.SteelTide:
                    {
                        bool v = anyOwn != null && HexSystem.DebuffImmune(sim, anyOwn);
                        sig = "全队免疫负面=" + v;
                        return v;
                    }
                    case HexId.RapidDeploy:
                    {
                        double m = HexSystem.ProductionSecondsMultiplier(sim, player);
                        sig = "产时倍率=" + m.ToString("F2") + "(应 0.71)";
                        return m < 0.75;
                    }
                    case HexId.Vengeance:
                        sig = "激活确认(叠层需主将阵亡,规则层 HexTests.T06 已覆盖)";
                        return true;
                    case HexId.LightMobility:
                    {
                        if (nearBldg == null) { sig = "无近建筑可测单位,仅激活确认"; return true; }
                        float m = HexSystem.MoveMultiplierOf(sim, nearBldg);
                        sig = "控区内单位移速×2=" + (m >= 1.95f) + "(实测 " + m.ToString("F2") + ")";
                        return m >= 1.95f;
                    }
                    case HexId.IronWall:
                    {
                        if (heavy == null) { sig = "场上无重装主将,仅激活确认"; return true; }
                        float b = HexSystem.DamageTakenBonusOf(sim, heavy);
                        sig = "重装承伤-30%=" + (b <= -0.29f) + "(实测 " + b.ToString("F2") + ")";
                        return b <= -0.29f;
                    }
                    case HexId.HoldFast:
                    {
                        if (nearBldg == null) { sig = "无近建筑可测单位,仅激活确认"; return true; }
                        float b = HexSystem.OutputBonusOf(sim, nearBldg);
                        sig = "控区内输出+10%=" + (b >= 0.09f) + "(实测 " + b.ToString("F2") + ")";
                        return b >= 0.09f;
                    }
                    case HexId.HiddenPath:
                    {
                        bool v = HexSystem.TowerExempt(sim, player);
                        sig = "塔锁豁免(非交战不被锁)=" + v;
                        return v;
                    }
                    default:
                        sig = "未识别卡型";
                        return false;
                }
            }

            /// <summary>找一名"距己方完好建筑 ≤10 米"的己方主将(以逸待劳/轻装机动的签名测量点)。</summary>
            private static Entity OwnUnitNearOwnBuilding(SimCore sim, Side side)
            {
                var bs = new List<SimVec2>();
                foreach (var kv in sim.Entities)
                {
                    var b = kv.Value;
                    if (b != null && b.Kind == EntityKind.Building && b.Side == side && !b.Rubbled)
                        bs.Add(SimVec2.FromGrid(b.Pos));
                }
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Side != side || e.Kind != EntityKind.Unit || e.IsSoldier
                        || e.State == EntityState.Dead) continue;
                    foreach (var bp in bs)
                        if (SimVec2.Distance(e.PosF, bp) <= 10f) return e;
                }
                return null;
            }

            /// <summary>推进仿真直到条件成立(上限 seconds 秒;足够覆盖 EP 累积与抽取窗口)。</summary>
            private static void AdvanceUntil(SimCore sim, System.Func<bool> cond, double seconds)
            {
                int max = (int)(seconds * sim.Config.TickRate);
                for (int i = 0; i < max; i++)
                {
                    if (cond()) return;
                    sim.Step();
                }
            }

            // ================= 生产六将(真按钮点击 → 入队 → 出将) =================
            /// <summary>制作人两次确认"不管什么将领生产出来都是磐石"。前一个窗口只验了属性链与视图层级名,
            /// 没验过**玩家真正点的那一排按钮**。这里把六个按钮逐个真点一遍,记下"入队的是谁"与"出来的是谁"。</summary>
            private IEnumerator RunProd()
            {
                L("---- 生产六将(真按钮) ----");
                var boot = Boot0();
                if (boot == null || boot.Sim == null) { L("  !! 没有战斗场景"); yield break; }
                var sim = boot.Sim;
                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                Click(FindButton(ui, "总览"), "总览入口", _log);
                yield return null;

                int baseId = 0;
                foreach (var kv in sim.Entities)
                    if (kv.Value.Kind == EntityKind.Building && kv.Value.Building == BuildingId.Base
                        && kv.Value.Side == AppFlow.PlayerSide) { baseId = kv.Key; break; }
                L("  己方主基地 id=" + baseId);

                var pairs = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("将:磐石", "PanShi"),
                    new KeyValuePair<string, string>("将:镇岳", "ZhenYue"),
                    new KeyValuePair<string, string>("将:断峰", "DuanFeng"),
                    new KeyValuePair<string, string>("将:逐风", "ZhuFeng"),
                    new KeyValuePair<string, string>("将:坠日", "ZhuiRi"),
                    new KeyValuePair<string, string>("将:星落", "XingLuo"),
                };
                for (int i = 0; i < pairs.Count; i++)
                {
                    Click(FindButton(ui, pairs[i].Key), pairs[i].Key, _log);
                    yield return null;
                    L("  点「" + pairs[i].Key + "」后 队列=" + QueueDump(sim, baseId) + "(期望含 " + pairs[i].Value + ")");
                }
                Shot("acc_prod_panel");

                var before = new HashSet<int>();
                foreach (var kv in sim.Entities) before.Add(kv.Key);
                for (int i = 0; i < 200 * sim.Config.TickRate; i++) sim.Step();
                yield return null;
                L("  出将(含**实际模型层级** —— 制作人三次报的就是这一栏):");
                int n = 0, bad = 0;
                int sampleId = 0;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || before.Contains(e.Id) || e.IsSoldier) continue;
                    n++;
                    int kids = 0; bool kidOk = true; string kidModel = "-";
                    foreach (var kv2 in sim.Entities)
                    {
                        var s = kv2.Value;
                        if (!s.IsSoldier || s.GeneralId != e.Id) continue;
                        kids++;
                        if (kids == 1)
                        {
                            var sv = Boot0() != null && Boot0().Board != null ? Boot0().Board.GetView(s.Id) : null;
                            kidModel = sv != null ? Chain(sv.gameObject) : "(无视图)";
                            kidOk = kidModel.Contains("SoldierModel");
                        }
                    }
                    var gv = Boot0() != null && Boot0().Board != null ? Boot0().Board.GetView(e.Id) : null;
                    string gm = gv != null ? Chain(gv.gameObject) : "(无视图)";
                    bool ok = gm.Contains("Heroes/" + e.Hero) && kidOk;
                    if (!ok) bad++;
                    L("    id=" + e.Id + " hero=" + e.Hero + " 兵数=" + kids + " 模型=" + gm
                        + " 兵模型=" + kidModel + " → " + (ok ? "OK" : "✗ 模型与英雄不符"));
                    if (e.Hero == HeroId.ZhuFeng) sampleId = e.Id;
                }
                L("  合计出将=" + n + " 模型违规=" + bad + " (期望 违规 0)");

                // 镜头对准一个"新生产的兵团"(优先 逐风:骑马 + 长枪,最容易一眼分辨)
                if (sampleId == 0)
                    foreach (var kv in sim.Entities)
                        if (kv.Value.Kind == EntityKind.Unit && !kv.Value.IsSoldier
                            && !before.Contains(kv.Key)) { sampleId = kv.Key; break; }
                var gen = sim.GetEntity(sampleId);
                if (gen != null)
                {
                    var rig = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
                    if (rig != null) rig.SetFocus(gen.PosF.X, gen.PosF.Y);
                    else if (Camera.main != null)
                        Camera.main.transform.position =
                            new Vector3(gen.PosF.X, 10f, 3f - gen.PosF.Y - 16f);
                    L("  近照机位对准 id=" + sampleId + " hero=" + gen.Hero + " @" + gen.PosF.X.ToString("F1") + "," + gen.PosF.Y.ToString("F1"));
                }
                yield return new WaitForSecondsRealtime(1.0f);
                yield return null;
                Shot("acc_prod_regiment");
            }

            // ================= 占领箭塔:军队不被"自己的城"吃掉 =================
            /// <summary>制作人报障:「我占领对方箭塔后,将军队派出,但是三个将只剩一个,剩的哪一个将也只有几个兵,
            /// 不是满编」。旧码在占领时把攻方士兵按配置容量(箭塔 30)登记为正式驻军(注册即被视图隐藏),
            /// 交换中被消耗的单位停在 `Exchanging` 而该状态被全系统跳过 → 永久僵死;且占领后不清 Intent,
            /// 后到的自家士兵会继续被拉进"已属于自己的城",和自家驻军按 1:1 互换 —— 兵被自己的城一个个吃掉。
            /// 判据:①塔易主 ②全场景 0 个 Exchanging ③无单位挂在塔上 ④塔 Intent 清零
            /// ⑤自动驻军 ≤9 ⑥3 名主将全部存活、在城外、且**收到移动令真的会走** ⑦城外士兵 ≥ 幸存−9。</summary>
            private IEnumerator RunCapture()
            {
                L("---- 占领箭塔(三将满编 → 军队派出) ----");
                var boot = Boot0();
                if (boot == null || boot.Sim == null) { L("  !! 没有战斗场景"); yield break; }
                var sim = boot.Sim;
                var player = AppFlow.PlayerSide;

                yield return CaptureOneTower(sim, player, 0, "空塔", "acc_capture_empty");
                yield return CaptureOneTower(sim, player, 12, "有守军(交换)", "acc_capture_held");
            }

            /// <summary>玩家方在城外的单位数(主将 + 士兵都算,用于核"整团出城/收回")。</summary>
            private static int CountOutsideUnits(SimCore sim, Side player)
            {
                int n = 0;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Unit || e.Side != player) continue;
                    if (e.GarrisonBuildingId == 0 && e.State != EntityState.Dead) n++;
                }
                return n;
            }

            /// <summary>给一座敌方箭塔摆一场"三将满编攻城",占领后再下令军队派出,逐条判据。
            /// garrison=守军人数(0=空塔,走无人建筑直接占领;>0=走进城交换)。</summary>
            private IEnumerator CaptureOneTower(SimCore sim, Side player, int garrison, string tag, string shot)
            {
                // 1) 找一座敌方箭塔(离玩家出生点最近的那座)
                Entity tower = null; float best = float.MaxValue;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Building) continue;
                    if (e.Building != BuildingId.Tower || e.Side == player || e.Rubbled) continue;
                    float d = e.PosF.X;
                    if (d < best) { best = d; tower = e; }
                }
                if (tower == null) { L("  !! 找不到敌方箭塔(" + tag + ")"); yield break; }
                var towerPos = tower.PosF;
                L("  [" + tag + "] 目标敌塔 id=" + tower.Id + " @" + towerPos.X.ToString("F1") + "," + towerPos.Y.ToString("F1")
                    + " 归属=" + tower.Side);

                // 2) 摆位:三将满编移到塔西侧 6~8 格(自检摆位,等价于玩家把军队开到塔前)
                var gens = new List<Entity>();
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier) continue;
                    if (e.Side != player || e.State == EntityState.Dead || e.GarrisonBuildingId != 0) continue;
                    gens.Add(e);
                    if (gens.Count >= 3) break;
                }
                if (gens.Count == 0) { L("  !! 玩家没有主将(" + tag + ")"); yield break; }
                float col0 = towerPos.X - 7f;
                for (int i = 0; i < gens.Count; i++)
                {
                    gens[i].PosF = new SimVec2(col0, towerPos.Y - 2f + i * 2f);
                    gens[i].State = EntityState.Idle;
                    gens[i].HasMoveTarget = false;
                    gens[i].TargetBuildingId = 0;
                }
                foreach (var kv in sim.Entities)
                {
                    var s = kv.Value;
                    if (s == null || !s.IsSoldier || s.Side != player) continue;
                    bool mine = false;
                    foreach (var g in gens) if (s.GeneralId == g.Id) { mine = true; break; }
                    if (!mine) continue;
                    s.PosF = new SimVec2(col0 - 1.5f, towerPos.Y - 6f + (s.Id % 12) * 1.1f);
                    s.State = EntityState.Idle;
                    s.HasMoveTarget = false;
                    s.TargetBuildingId = 0;
                }

                // 3) 给塔注入守军(自检摆位,走与游戏一致的正式驻军登记路径)
                if (garrison > 0)
                {
                    var dg = RegimentSystem.SpawnGeneral(sim, player == Side.Blue ? Side.Red : Side.Blue,
                        HeroId.PanShi, new GridPos((int)towerPos.Y, (int)(towerPos.X + 6f)));
                    // 守军按**整团**注入(主将 + 其士兵):与"驻军=整团"的口径一致,
                    // 免得自检自己造出一个"兵在城里、主将在城外"的敌城状态。
                    dg.GarrisonBuildingId = tower.Id;
                    tower.GarrisonFormal.Add(dg.Id);
                    int added = 0;
                    foreach (var kv in sim.Entities)
                    {
                        var s = kv.Value;
                        if (s == null || !s.IsSoldier || s.GeneralId != dg.Id) continue;
                        s.GarrisonBuildingId = tower.Id;
                        tower.GarrisonFormal.Add(s.Id);
                        if (++added >= garrison) break;
                    }
                    L("  注入守军 " + (added + 1) + " 名(整团:主将 1 + 士兵 " + added + "),塔驻军=" + tower.GarrisonFormal.Count);
                }

                int beforeGens = 0, beforeSoldiers = 0;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Unit || e.Side != player) continue;
                    if (e.IsSoldier) beforeSoldiers++; else beforeGens++;
                }
                L("  战前:玩家方 将=" + beforeGens + " 兵=" + beforeSoldiers);

                // 4) 真令:兵团攻城(与 UI 右键同一条入口)
                var ids = new List<int>();
                foreach (var g in gens) ids.Add(g.Id);
                var res = SiegeSystem.TryIssueRegimentOrder(sim, ids, tower.Id, player, SiegeIntent.Capture);
                L("  TryIssueRegimentOrder → Success=" + res.Success + " error=" + res.Error
                    + " 展开士兵=" + res.SoldierIds.Count);

                // 5) 推进到易主
                int maxTicks = 120 * sim.Config.TickRate;
                int used = 0;
                bool unstuck = false;
                for (; used < maxTicks; used++)
                {
                    if (tower.Side == player) break;
                    sim.Step();
                    // 2026-09-17 卡位解救:本场景用批量传送摆位,主将可能被友军人堆挤在集结点动不了
                    // (实测一局:三将 120 秒只挪 0.3 格 → 主将不到位 → 士兵永远拿不到标记 → 交换不开始)。
                    // 15 秒仍无人进城(攻方仍在城外)时,把主将直接放到占领集结点,视为"走到位"。
                    // 这是**摆位伪影的补偿**,不是游戏逻辑;游戏侧"集结点可被友军挤死"已记 PROJECT_STATE 观察项。
                    if (!unstuck && used == 150 && tower.GarrisonFormal.Count > 0 && tower.Side != player)
                    {
                        bool anyInside = false;
                        foreach (var g in gens)
                        {
                            var gg = sim.GetEntity(g.Id);
                            if (gg != null && gg.State == EntityState.Exchanging) { anyInside = true; break; }
                        }
                        if (!anyInside)
                        {
                            unstuck = true;
                            L("  (15 秒无人进城:主将被传送摆位挤住,解卡 = 直接放到占领集结点)");
                            foreach (var g in gens)
                            {
                                var gg = sim.GetEntity(g.Id);
                                if (gg == null) continue;
                                gg.PosF = new SimVec2(towerPos.X - 4.6f, towerPos.Y);
                                gg.HasMoveTarget = false;
                                gg.State = EntityState.Idle;
                            }
                        }
                    }
                }
                for (int i = 0; i < 120; i++) sim.Step(); // 再跑 4 秒:看有没有后到的自家兵继续进城

                // 6) 判据
                int exchanging = 0, tied = 0, aliveGens = 0, aliveSoldiers = 0, outsideSoldiers = 0;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Unit || e.Side != player) continue;
                    if (e.State == EntityState.Exchanging) exchanging++;
                    if (e.TargetBuildingId == tower.Id) tied++;
                    if (e.IsSoldier) { aliveSoldiers++; if (e.GarrisonBuildingId == 0) outsideSoldiers++; }
                    else aliveGens++;
                }
                // 驻军口径(2026-09-14 制作人):「每个军团要么全留,要么全出,不要主将出去兵还在城里」
                // → 驻军要么为空,要么**恰好一个完整兵团**(主将 + 它的全部存活士兵)。这里逐条核这个不变量。
                int garrisonTotal = aliveSoldiers - outsideSoldiers, cityCount = 0, halfRegiments = 0;
                foreach (var kv in sim.Entities)
                {
                    var b = kv.Value;
                    if (b == null || b.Kind != EntityKind.Building || b.Side != player) continue;
                    if (b.GarrisonFormal.Count == 0 && b.GarrisonTemp.Count == 0) continue;
                    cityCount++;
                    // 判据口径(2026-09-14):"半团"指**把主将和它的兵拆开放进/放出**——
                    // ①驻军士兵的主将还活着却不在同一驻军里(主将在城外、兵在城里)= 半团;
                    // ②驻军主将的存活士兵还有在城外的 = 半团(同样是拆开)。
                    // 主将在守城交换中**阵亡**(守方主将值 10/12 点,会死)不算半团:那是战损,
                    // 此时它的残余士兵留在城里是正常结果。
                    var garGens = 0; var mismatch = 0;
                    int keeperId = 0;
                    foreach (var id in b.GarrisonFormal)
                    {
                        var u = sim.GetEntity(id);
                        if (u == null) continue;
                        if (!u.IsSoldier) { garGens++; keeperId = id; continue; }
                        var gOwner = sim.GetEntity(u.GeneralId);
                        if (gOwner != null && !b.GarrisonFormal.Contains(gOwner.Id)) mismatch++;
                    }
                    if (keeperId != 0)
                        foreach (var kv2 in sim.Entities)
                        {
                            var sOwner = kv2.Value;
                            if (sOwner == null || !sOwner.IsSoldier || sOwner.GeneralId != keeperId) continue;
                            if (!b.GarrisonFormal.Contains(sOwner.Id)) mismatch++;
                        }
                    if (garGens > 1 || mismatch > 0) halfRegiments++;
                }
                string comp = "空";
                if (tower.GarrisonFormal.Count > 0)
                {
                    int inGens = 0, inSoldiers = 0, liveSol = 0;
                    foreach (var id in tower.GarrisonFormal)
                    {
                        var u = sim.GetEntity(id);
                        if (u == null) continue;
                        if (u.IsSoldier) inSoldiers++; else inGens++;
                    }
                    foreach (var kv in sim.Entities)
                        if (kv.Value.IsSoldier && sim.GetEntity(kv.Value.GeneralId) != null
                            && tower.GarrisonFormal.Contains(kv.Value.GeneralId)) liveSol++;
                    comp = "主将 " + inGens + " + 士兵 " + inSoldiers + "(该团存活士兵共 " + liveSol + " 名)";
                }
                L("  占领耗时 " + (used / (float)sim.Config.TickRate).ToString("F1") + " 秒  塔归属=" + tower.Side
                    + " 驻军(正式=" + tower.GarrisonFormal.Count + " 临时=" + tower.GarrisonTemp.Count + ")=" + comp
                    + " 塔 Intent=" + tower.Intent);
                L("  ★判据:塔已归我=" + (tower.Side == player ? "是" : "否")
                    + " ★判据:无 Exchanging 僵尸=" + (exchanging == 0 ? "是" : "否(" + exchanging + ")")
                    + " ★判据:无单位挂在塔上=" + (tied == 0 ? "是" : "否(" + tied + ")")
                    + " ★判据:塔意图已清=" + (tower.Intent == SiegeIntent.None ? "是" : "否")
                    + " ★判据:驻军整团或不留(无半团)=" + (halfRegiments == 0 ? "是" : "否(" + halfRegiments + " 城半团)"));
                // 占领这一刻"在场的将"快照(下面那条判据只核这些人之后没被吃掉;别处的战损不算)
                var gensAliveAtCapture = new HashSet<int>();
                foreach (var g in gens) if (sim.GetEntity(g.Id) != null) gensAliveAtCapture.Add(g.Id);
                L("  战后:玩家方 将=" + aliveGens + " 兵=" + aliveSoldiers + "(城外 " + outsideSoldiers
                    + " 驻城合计 " + garrisonTotal + " 分 " + cityCount + " 城)"
                    // 判据修正:只核"**占领这一刻在场的将,之后没被吃掉**"(交换永不杀主将)。
                    // 原来拿"整场战前/战后的将在场数"比 —— `IC_ACCEPT=all` 前前后后打了十几分钟,
                    // 别处的战斗里阵亡的将也会算成"否"(脚本假阳性,不是游戏 bug)。
                    + " ★判据:占领后无主将被吃=" + (gens.All(g => sim.GetEntity(g.Id) != null || !gensAliveAtCapture.Contains(g.Id)) ? "是" : "否")
                    + " ★判据:兵员无失踪(城外+驻城=存活)=" + (outsideSoldiers + garrisonTotal == aliveSoldiers ? "是" : "否"));

                // 6b) 占领瞬间留一张(军队还在塔下,便于目视"塔已变蓝 + 三将满编在城外")
                var rig0 = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
                if (rig0 != null) rig0.SetFocus(towerPos.X, towerPos.Y);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return null;
                Shot(shot + "_at");

                // 7) 军队派出:每名幸存主将收到移动令都必须真的走(旧码钉死在 Exchanging)
                int blocked = 0, stayedInTown = 0, dead = 0;
                foreach (var g in gens)
                {
                    var ent = sim.GetEntity(g.Id);
                    if (ent == null) { dead++; L("    将 id=" + g.Id + " 已阵亡(不在场)"); continue; }
                    if (ent.GarrisonBuildingId != 0)
                    {   // 按整团口径留城守城的那名主将:本来就该待在城里,不属于"随军出城"
                        stayedInTown++;
                        L("    将 id=" + ent.Id + " hero=" + ent.Hero + " 在城内(整团留城守城,不随军出城)");
                        continue;
                    }
                    var from = ent.PosF;
                    // 目标格必须**夹进地图**(2026-09-14 实测:高 Y 处的塔 +14 会越界,SetMoveTarget 直接抛异常,
                    // 把自检协程打断 → 播放器卡死)
                    int awayRow = UnityEngine.Mathf.Clamp((int)(towerPos.Y + 14f), 2, LaneGrid.Rows - 3);
                    int awayCol = UnityEngine.Mathf.Clamp((int)(towerPos.X - 22f), 2, LaneGrid.Cols - 3);
                    sim.SetMoveTarget(ent.Id, new GridPos(awayRow, awayCol), true);
                    for (int i = 0; i < 8 * sim.Config.TickRate; i++) sim.Step();
                    // 位移只作**记录**:僵死的机器口径是 `Exchanging`(上面已核),而位移 0 更常见的原因是
                    // 场上单位拥挤把主将卡住(IC_ACCEPT=all 时场上有上百个单位);同一套代码在单场景
                    // `IC_ACCEPT=capture` 里三将都能走出 15 格以上。
                    // 2026-09-14:这 8 秒里主将**可能阵亡**(同 tick 挥砍语义下阵亡 tick 仍会出手),
                    // 所以位移必须**重新取实体**后再算 —— 之前拿着 240 tick 前的引用算 `ent.PosF` 会在
                    // 实体被移除后抛异常,把整个自检协程打断(实测播放器卡死)。
                    var cur = sim.GetEntity(ent.Id);
                    if (cur == null) { dead++; L("    将 id=" + ent.Id + " 在派出途中阵亡"); continue; }
                    float movedBy = SimVec2.Distance(from, cur.PosF);
                    if (movedBy < 1f) blocked++;
                    L("    将 id=" + ent.Id + " hero=" + ent.Hero + " 派出位移=" + movedBy.ToString("F1") + " 格"
                        + (movedBy < 1f ? "(受阻滞)" : ""));
                }
                L("  留城主将=" + stayedInTown + " 名(塔驻军 " + tower.GarrisonFormal.Count
                    + " 人 = 该团全部,已在上行『驻军整团』判据核过),阵亡=" + dead + " 名,受阻滞=" + blocked
                    + " 名(非僵死:全局 Exchanging=0 已在上行核过)");

                // 8) 面板观感(玩家点开建筑看到的驻军数)+ 自动驻军可一键收回
                //    (制作人"军队被吃掉"的另一半:点开塔 → 驻军切片 → [释放出城] 把人要回来)
                int tid = BuildingCommandModel.PickBuildingAny(sim, towerPos);
                var bp = Boot0();
                var panel = bp != null ? bp.GetComponent<BuildingCommandPanel>() : null;
                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                L("  点选塔 PickBuildingAny=" + tid + "(期望 " + tower.Id + ")");
                // 2026-09-17 判据恒定化:士兵抢先即时占领时塔为空,旧版此时**跳过**"整团收回"判据
                // → 判据总数随对局状态漂移(31~36)。改为用与守军注入同款的**正式驻军登记路径**
                // 把一名下令主将放进去,判据前提每局固定。
                if (tower.GarrisonFormal.Count == 0)
                {
                    Entity keeper = null;
                    foreach (var g in gens)
                    {
                        var gg = sim.GetEntity(g.Id);
                        if (gg != null && gg.State != EntityState.Dead) { keeper = gg; break; }
                    }
                    if (keeper != null)
                    {
                        keeper.GarrisonBuildingId = tower.Id;
                        keeper.State = EntityState.Idle;
                        keeper.HasMoveTarget = false;
                        tower.GarrisonFormal.Add(keeper.Id);
                        L("  (塔被即时占领后无人驻守,按正式路径登记主将#" + keeper.Id + " 入驻以固定判据前提)");
                    }
                }
                int g0 = tower.GarrisonFormal.Count;
                int outsideUnitsBefore = CountOutsideUnits(sim, player);
                if (panel != null && ui != null && g0 > 0)
                {
                    panel.SetTarget(tower.Id);
                    yield return null; yield return null;
                    // 驻军行是「标签(正式 磐石 ×9) + 按钮(释放出城)」,按**按钮文字**找;
                    // FindButtonInRow 是先找标签再取同行按钮,对这套行不适用。
                    var rel = FindButton(ui, "释放出城");
                    if (rel == null) rel = FindButtonInRow(ui, "释放出城");
                    // 点之前记下城内这批人的 id —— 收回是否成立,直接盯**这批人的去向**,不靠全局计数
                    var releaseSnapshot = new List<int>(tower.GarrisonFormal);
                    foreach (int t2 in tower.GarrisonTemp) if (!releaseSnapshot.Contains(t2)) releaseSnapshot.Add(t2);
                    Click(rel, "释放出城(" + tag + " 驻军 " + g0 + " 人)", _log);
                    for (int i = 0; i < 30; i++) sim.Step();
                    yield return null; yield return null;
                    int still = tower.GarrisonFormal.Count;
                    int backOutside = CountOutsideUnits(sim, player);   // 含主将:整团收回时主将也出城
                    // 2026-09-15 判据改精确:`(全局城外单位数之差) == 原驻军数` 在 `IC_ACCEPT=all` 里**不可靠** ——
                    // 实测出现过 `塔驻军 1→0` 但 `87→87`:本塔确实清空了,而**别处**恰好有一个单位在这段窗口里
                    // 进驻/出城,把差值抹平了。这正是本条注释原本担心的"别的城也可能驻着兵",只是方向相反。
                    // 现在直接盯**这一批人的去向**:点[释放出城]前记下城内每个 id,点完逐个核对
                    // "这个人在城外、活着、不在交换态" —— 这才是"整团收回"的语义。全局数只留作读数,不作判据。
                    var strays = new List<string>();
                    foreach (int sid in releaseSnapshot)
                    {
                        var u = sim.GetEntity(sid);
                        if (u == null) { strays.Add(sid + "=不存在"); continue; }
                        if (u.State == EntityState.Dead) { strays.Add(sid + "=阵亡"); continue; }
                        if (u.GarrisonBuildingId != 0) strays.Add(sid + "=仍在驻军" + u.GarrisonBuildingId);
                        else if (u.State == EntityState.Exchanging) strays.Add(sid + "=仍在交换态");
                    }
                    L("  驻军收回后 塔驻军=" + still + "(城内 " + releaseSnapshot.Count + " 人)"
                        + " 全局城外单位=" + outsideUnitsBefore + "→" + backOutside + "(读数,会被别处进驻/出城带动)"
                        + " 城内那批人是否全部出城且存活=" + (strays.Count == 0 ? "是" : "否 → " + string.Join(";", strays))
                        + " ★判据:驻军可整团收回=" + (still == 0 && strays.Count == 0 ? "是" : "否"));
                }

                var rig = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
                if (rig != null) rig.SetFocus(towerPos.X - 10f, towerPos.Y);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return null;
                Shot(shot);
            }

            // ================= ESC 暂停浮层 =================
            /// <summary>制作人:"当游戏中没有需要 esc 关闭的东西时,按 esc → 大字:游戏暂停(仅 ai/训练场生效),
            /// 下面一个按钮:返回大厅"。判据:①有面板开着时 ESC 只关面板、不暂停;②没东西可关时 ESC 才暂停,
            /// 且**仿真真的冻住**;③再按 ESC 继续;④浮层里有"返回大厅"按钮。</summary>
            private IEnumerator RunPause()
            {
                L("---- ESC 暂停浮层 ----");
                var boot = Boot0();
                if (boot == null) { L("  !! 没有战斗场景"); yield break; }
                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                var pause = boot.GetComponent<PauseOverlayPanel>();
                if (pause == null) { L("  !! 没有 PauseOverlayPanel"); yield break; }

                // ① 开着中央面板:ESC 只关面板
                Click(FindButton(ui, "道具"), "道具入口", _log);
                yield return null; yield return null;
                bool wasOpen = UiRoot.Current.HasOpenCenter;
                UiRoot.Current.HandleEscPressed();
                yield return null;
                L("  ①开着面板时 ESC: 原本开着=" + wasOpen + " 现在开着=" + UiRoot.Current.HasOpenCenter
                    + " 是否暂停=" + pause.IsPaused
                    + " ★判据:只关面板不暂停=" + (wasOpen && !UiRoot.Current.HasOpenCenter && !pause.IsPaused ? "是" : "否"));

                // ② 没东西可关:ESC 暂停 + 仿真冻住
                UiRoot.Current.HandleEscPressed();
                yield return null;
                long tick0 = boot.Sim.Tick;
                yield return new WaitForSecondsRealtime(0.6f);
                long tick1 = boot.Sim.Tick;
                L("  ②没东西可关时 ESC: 暂停=" + pause.IsPaused + " boot.Paused=" + boot.Paused
                    + " 0.6 秒内仿真 tick " + tick0 + "→" + tick1
                    + " ★判据:暂停且仿真冻住=" + (pause.IsPaused && boot.Paused && tick0 == tick1 ? "是" : "否"));
                Shot("acc_pause");
                bool hasLobbyBtn = FindButton(ui, "返回大厅") != null;
                L("  浮层里的『返回大厅』按钮=" + hasLobbyBtn);

                // ③ 再按一次 ESC:继续,仿真恢复
                UiRoot.Current.HandleEscPressed();
                yield return new WaitForSecondsRealtime(0.6f);
                L("  ③再按 ESC: 暂停=" + pause.IsPaused + " boot.Paused=" + boot.Paused
                    + " 仿真 tick " + boot.Sim.Tick
                    + " ★判据:继续且仿真恢复=" + (!pause.IsPaused && !boot.Paused && boot.Sim.Tick > tick1 ? "是" : "否"));

                // ④ 倍速控件(2026-09-17 制作人拍板 ×1/×2/×4/×8):量 ×1 基线 → 点两次 [+] 到 ×4 → 量推进比
                Button plus = null, minus = null;
                var scRoot = ui.Q<VisualElement>("SpeedControl");
                if (scRoot != null)
                    scRoot.Query<Button>().ToList().ForEach(b =>
                    {
                        if (b.text == "+" && plus == null) plus = b;
                        if (b.text == "−" && minus == null) minus = b;
                    });
                if (plus == null || minus == null)
                {
                    L("  ④倍速:找不到 [−]/[+] 按钮(控件未装配?) ★判据:倍速控件存在=否");
                    yield break;
                }
                var simP = boot.Sim;
                long a0 = simP.Tick;
                yield return new WaitForSecondsRealtime(0.7f);
                double perSecX1 = (simP.Tick - a0) / 0.7;
                Click(plus, "倍速+(第一下→×2)", _log);
                Click(plus, "倍速+(第二下→×4)", _log);
                yield return null;
                int gearShown = SpeedControlPanel.Latest;
                long a1 = simP.Tick;
                yield return new WaitForSecondsRealtime(0.7f);
                double perSecX4 = (simP.Tick - a1) / 0.7;
                L("  ④倍速:×1 每秒≈" + perSecX1.ToString("F0") + " tick,连点两下[+]后每秒≈"
                    + perSecX4.ToString("F0") + " tick(比 " + (perSecX1 > 0 ? (perSecX4 / perSecX1).ToString("F1") : "-") + "×)"
                    + " 档位显示=" + gearShown
                    + " ★判据:倍速档位生效(×4≈4 倍推进)=" + (perSecX1 > 0 && perSecX4 / perSecX1 > 3.0 && gearShown == 4 ? "是" : "否"));
                // 收尾:退回 ×1,不影响后续场景节奏
                Click(minus, "倍速−(回×2)", _log);
                Click(minus, "倍速−(回×1)", _log);
                yield return null;
            }

            // ================= AI 对局:总览只列玩家选的三个英雄 =================
            /// <summary>制作人 2026-09-14:"AI 对局中,总览应该是玩家选择的三个英雄,而不是六英雄"。
            /// 自检:模拟 BP 结果(设 AppFlow.BpBluePicks)→ 进对局 → 开总览,数"将:"按钮的个数与名字。</summary>
            private IEnumerator RunProdThree()
            {
                L("---- 总览:AI 对局只列玩家选的三将(需 IC_BP_PICKS) ----");
                if (Boot0() == null) { L("  !! 没有战斗场景"); yield break; }
                L("  本局 BP:蓝方=" + (AppFlow.BpBluePicks == null ? "无(练习场)" : Join(AppFlow.BpBluePicks)));
                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                Click(FindButton(ui, "总览"), "总览入口", _log);
                yield return null; yield return null;
                var names = new List<string>();
                CollectButtonTexts(ui, "将:", names);
                L("  总览里的生产按钮=" + Join(names) + "(共 " + names.Count + " 个)");
                // 判据要分清两种正确形态:有 BP → 恰好 3 个;练习场无 BP → 六个全开放
                bool hasPicks = AppFlow.BpBluePicks != null || AppFlow.BpRedPicks != null;
                L("  ★判据:" + (hasPicks
                    ? ("只列玩家选的三将=" + (names.Count == 3 ? "是" : "否"))
                    : ("无 BP(练习场)→ 六将全开放=" + (names.Count == 6 ? "是" : "否"))));
                Shot("acc_prod_three");
            }

            private static List<HeroId> ParseHeroes(string csv)
            {
                var list = new List<HeroId>();
                if (string.IsNullOrEmpty(csv)) return list;
                foreach (var raw in csv.Split(','))
                {
                    var t = raw.Trim();
                    HeroId h;
                    if (System.Enum.TryParse(t, out h)) list.Add(h);
                }
                return list;
            }

            private static string Join<T>(List<T> list)
            {
                return list == null ? "-" : string.Join(",", System.Array.ConvertAll(list.ToArray(), x => x.ToString()));
            }

            private static void CollectButtonTexts(VisualElement root, string prefix, List<string> outp)
            {
                if (root == null) return;
                var b = root as Button;
                if (b != null && !string.IsNullOrEmpty(b.text) && b.text.Contains(prefix)
                    && !outp.Contains(b.text)) outp.Add(b.text.Replace("将:", ""));
                foreach (var c in root.Children()) CollectButtonTexts(c, prefix, outp);
            }

            // ================= 面板固定居中(制作人:"过几秒没管就会往下一截") =================
            /// <summary>制作人:"下面那四个按钮(以及所有面板)点开在中间,**过几秒没管就会往下一截**,
            /// 按钮还能点但点了不生效,再点才生效 → 改成固定中间"。
            /// 判据:打开面板后连续采样它的矩形 4 秒,**不得位移**;
            /// 同时采样 UiRoot 的 Screen/Center 尺寸,确认几何不再发生相位切换。</summary>
            /// <summary>
            /// 2026-09-14 制作人:"下方按钮展开的面板还是会出现不稳定的情况,包括边框变动,点一下才恢复,
            /// 而且点的这一下还不算数" —— 真因是**正文每秒被重建**(重绘键里含 `(int)EP`,EP 持续累积):
            /// 重建瞬间高度变化=边框变动,点在重建瞬间的按钮上=那一下不算数。
            /// 这里做客观判据:打开每个面板后静置 4 秒,统计正文**重建次数**(应为 0),并在面板内真点一下按钮,
            /// 确认这一下**算数**(总览=队列里真的多了单子)。
            /// </summary>
            private IEnumerator RunPanelRebuild()
            {
                L("---- 面板正文重建(制作人:边框变动/点一下不算数) ----");
                var boot = Boot0();
                if (boot == null) { L("  !! 没有战斗场景"); yield break; }
                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                var sim = boot.Sim;

                yield return PanelRebuildOne(ui, sim, "总览", () => BarracksOverviewPanel.Rebuilds, true);
                yield return PanelRebuildOne(ui, sim, "英雄升级", () => UpgradeCommandPanel.Rebuilds, false);
                yield return PanelRebuildOne(ui, sim, "海克斯", () => HexCommandPanel.Rebuilds, false);
                yield return PanelRebuildOne(ui, sim, "道具", () => ToolCommandPanel.Rebuilds, false);
            }

            private IEnumerator PanelRebuildOne(VisualElement ui, SimCore sim, string name,
                System.Func<int> counter, bool checkInnerClick)
            {
                Click(FindButton(ui, name), name + "入口", _log);
                for (int i = 0; i < 4; i++) yield return null;
                int c0 = counter();
                int slotsBefore = -1, slotsAfter = -1;

                if (checkInnerClick)
                {
                    int baseId = 0;
                    foreach (var kv in sim.Entities)
                        if (kv.Value.Kind == EntityKind.Building && kv.Value.Building == BuildingId.Base
                            && kv.Value.Side == AppFlow.PlayerSide) { baseId = kv.Key; break; }
                    var b = sim.GetEntity(baseId);
                    slotsBefore = b != null && b.Prod != null
                        ? CountUsedSlots(b.Prod) : -1;
                    Click(FindButton(ui, "将:磐石"), name + "内[将:磐石]", _log);
                    yield return null;
                    slotsAfter = b != null && b.Prod != null ? CountUsedSlots(b.Prod) : -1;
                }

                yield return new WaitForSecondsRealtime(4f);   // EP 在这 4 秒里会累积(旧实现会因此反复重绘)
                int c1 = counter();
                L("  [" + name + "] 4 秒内正文重建次数=" + (c1 - c0)
                    + " ★判据:面板不再反复重建=" + (c1 - c0 == 0 ? "是" : "否")
                    + (checkInnerClick
                        ? "  面板内点[将:磐石]:占用槽 " + slotsBefore + "→" + slotsAfter
                          + " ★判据:面板内点击算数=" + (slotsAfter > slotsBefore ? "是" : "否")
                        : ""));
                Click(FindButton(ui, name), name + "入口(收起)", _log);
                for (int i = 0; i < 3; i++) yield return null;
            }

            private static int CountUsedSlots(ProductionSet prod)
            {
                int n = 0;
                for (int i = 0; i < prod.Slots.Length; i++)
                    if (prod.Slots[i].Current.HasValue || prod.Slots[i].Pending.Count > 0) n++;
                return n;
            }

            /// <summary>2026-09-14 制作人:"训练场 EP 无限"。判据:EP 钉在上限(99)、花掉后立刻补满。</summary>
            /// <summary>
            /// 2026-09-14 制作人:"近战挥砍听感极差,换成金属清脆碰撞音"。
            /// 听觉无法自动判定,这里判**接线**是否走到新音效:真打一场近战,数 `AudioSystem.MeleePlays`。
            /// (音色本身的客观指标在 `MeleeSfxTool` 里量:起振 0.258s→0.006s、谱重心 337→1961Hz、高频占比 3%→48%。)
            /// </summary>
            private IEnumerator RunMeleeSfx()
            {
                L("---- 近战音效(金属清脆碰撞) ----");
                var boot = Boot0();
                if (boot == null) { L("  !! 没有战斗场景"); yield break; }
                var sim = boot.Sim;
                int before = AudioSystem.MeleePlays;

                // 在己方一名主将旁边放一对敌对单位(2 米内 → 双方视野内 → 自动互砍)
                Entity anchor = null;
                foreach (var kv in sim.Entities)
                    if (kv.Value.Kind == EntityKind.Unit && !kv.Value.IsSoldier && kv.Value.Side == AppFlow.PlayerSide)
                    { anchor = kv.Value; break; }
                bool spawned = false;
                if (anchor != null)
                {
                    var foe = AppFlow.PlayerSide == Side.Blue ? Side.Red : Side.Blue;
                    try
                    {
                        var mine = sim.SpawnEntity(AppFlow.PlayerSide, EntityKind.Unit,
                            new GridPos(anchor.Pos.Row + 3, anchor.Pos.Col));
                        var theirs = sim.SpawnEntity(foe, EntityKind.Unit,
                            new GridPos(anchor.Pos.Row + 3, anchor.Pos.Col + 2));
                        mine.Hero = HeroId.PanShi; mine.Hp = mine.MaxHp = 3000f; mine.Attack = 6f;
                        mine.AttackIntervalSec = 0.5f; mine.Range = 2f; mine.MoveSpeed = 0f;
                        theirs.Hero = HeroId.PanShi; theirs.Hp = theirs.MaxHp = 3000f; theirs.Attack = 6f;
                        theirs.AttackIntervalSec = 0.5f; theirs.Range = 2f; theirs.MoveSpeed = 0f;
                        spawned = true;
                    }
                    catch (System.Exception ex) { L("  摆位失败:" + ex.Message); }
                }
                if (spawned)
                    for (int i = 0; i < (int)(3f * sim.Config.TickRate); i++) sim.Step();
                L("  近战命中音播放次数 " + before + " → " + AudioSystem.MeleePlays
                    + " ★判据:近战走了新音效(sfx_melee)=" + (AudioSystem.MeleePlays > before ? "是" : "否"));
                yield return null;
            }

            private IEnumerator RunPracticeEp()
            {
                L("---- 训练场特权(EP 无限 / 海克斯自由抽取) ----");
                var boot = Boot0();
                if (boot == null) { L("  !! 没有战斗场景"); yield break; }
                var sim = boot.Sim;
                var side = AppFlow.PlayerSide;
                int cap = sim.EpCapEffective;
                L("  训练场(模式=" + AppFlow.Current + ") EP=" + sim.EpOf(side) + " 上限=" + cap
                    + " ★判据:EP 钉在上限=" + (cap > 5 && sim.EpOf(side) >= cap ? "是" : "否(cap=" + cap + ")"));
                sim.SpendEp(side, 5f);
                float after = sim.EpOf(side);
                sim.Step();
                L("  花 5 后=" + after + " → 下一 tick=" + sim.EpOf(side)
                    + " ★判据:花完立刻补满=" + (after < cap && sim.EpOf(side) >= cap ? "是" : "否"));

                // 2026-09-17 制作人拍板:"训练场海克斯抽卡不限时间不限次" —— 此刻远未到第 3 分钟窗口,连抽两次
                var d1 = HexSystem.TryDraw(sim, side);
                bool free1 = d1.Success;
                if (free1) HexSystem.TryChoose(sim, side, d1.Choices[0]);
                var d2 = HexSystem.TryDraw(sim, side);
                L("  自由抽取(tick=" + sim.Tick + ",窗口外):第一次=" + (free1 ? "成功" : d1.Error.ToString())
                    + " 第二次=" + (d2.Success ? "成功" : d2.Error.ToString())
                    + " HexFreeDraw=" + sim.HexFreeDraw
                    + " ★判据:训练场海克斯不限时不限次=" + (free1 && d2.Success ? "是" : "否"));
                if (d2.Success) HexSystem.TryChoose(sim, side, d2.Choices[0]);   // 清掉待选,不影响后续场景
                yield return null;
            }

            /// <summary>
            /// 2026-09-14 制作人:"之前下面按钮 ui 大小局内变化的问题又回来了"。
            /// 逐帧量**四个底部入口按钮**的矩形(尺寸/位置),任何 >0.5px 的变化都记下来。
            /// </summary>
            private IEnumerator RunBottomUiSize()
            {
                L("---- 底部按钮尺寸稳定性 ----");
                if (Boot0() == null) { L("  !! 没有战斗场景"); yield break; }
                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                string[] names = { "总览", "英雄升级", "海克斯", "道具" };
                var r0 = new Dictionary<string, Rect>();
                foreach (var n in names)
                {
                    var b = FindButton(ui, n);
                    r0[n] = b != null ? b.worldBound : Rect.zero;
                }
                for (int i = 0; i < 16; i++)   // 8 秒,每 0.5 秒采一次
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    foreach (var n in names)
                    {
                        var b = FindButton(ui, n);
                        if (b == null) continue;
                        var cur = b.worldBound;
                        var old = r0[n];
                        float dW = Mathf.Abs(cur.width - old.width), dH = Mathf.Abs(cur.height - old.height);
                        float dX = Mathf.Abs(cur.x - old.x), dY = Mathf.Abs(cur.y - old.y);
                        if (dW > 0.5f || dH > 0.5f || dX > 0.5f || dY > 0.5f)
                        {
                            L("  [" + n + "] 变化 t=" + (i * 0.5f).ToString("F1") + "s"
                                + " 尺寸 " + old.width.ToString("F1") + "x" + old.height.ToString("F1")
                                + "→" + cur.width.ToString("F1") + "x" + cur.height.ToString("F1")
                                + " 位置 (" + old.x.ToString("F0") + "," + old.y.ToString("F0")
                                + ")→(" + cur.x.ToString("F0") + "," + cur.y.ToString("F0") + ")");
                            r0[n] = cur;
                        }
                    }
                    if (i == 3 || i == 15)   // 第 2 秒/第 8 秒各报一次"当前值",便于看漂移
                    {
                        var b = FindButton(ui, names[0]);
                        if (b != null)
                            L("  t=" + (i * 0.5f).ToString("F1") + "s [总览]=" + b.worldBound.width.ToString("F1")
                                + "x" + b.worldBound.height.ToString("F1")
                                + " @(" + b.worldBound.x.ToString("F0") + "," + b.worldBound.y.ToString("F0") + ")");
                    }
                }
                L("  ★判据:8 秒内底部按钮尺寸/位置不变=见上面的『变化』行(没有=是)");

                // 2026-09-15 制作人:"按钮点出来的面板大小变化…能不能给它定死大小" → 逐个打开量尺寸,必须一致
                var sizes = new List<string>();
                foreach (var n in names)
                {
                    Click(FindButton(ui, n), n + "入口", _log);
                    for (int k = 0; k < 4; k++) yield return null;
                    var r = UiRoot.LastCenterRect;
                    sizes.Add(n + "=" + r.width.ToString("F0") + "x" + r.height.ToString("F0"));
                    Click(FindButton(ui, n), n + "入口(收起)", _log);
                    for (int k = 0; k < 3; k++) yield return null;
                }
                L("  各面板展开尺寸:" + string.Join(" | ", sizes)
                    + "  ★判据:四个面板尺寸一致且定死(" + UiRoot.PanelWidth + "x" + UiRoot.PanelHeight + ")="
                    + (sizes.All(x => x.Contains("x") && Mathf.Abs(float.Parse(x.Split('=')[1].Split('x')[0]) - UiRoot.PanelWidth) < 2f) ? "是" : "否"));

                // 2026-09-16 三十七轮:制作人"包括里面的按钮之类的,大小都会莫名其妙地微变" ——
                // 旧自检只量了入口按钮和外框,**面板内部元素从没量过**(检测空白)。
                // 逐面板打开,采样中央框内全部按钮的 worldBound,开面板采样一次、
                // 挂机 6 秒(EP 累积/队列变化驱动内容文本变化)再采一次,对比漂移。
                L("---- 面板内部按钮尺寸稳定性(新增) ----");
                int driftTotal = 0;
                foreach (var n in names)
                {
                    Click(FindButton(ui, n), n + "入口(内部采样)", _log);
                    for (int k = 0; k < 4; k++) yield return null;
                    var before = SampleCenterButtons(ui);
                    yield return new WaitForSecondsRealtime(6f);   // 期间 EP/补兵池/队列文本在变
                    var after = SampleCenterButtons(ui);
                    int drift = 0;
                    var lines = new List<string>();
                    foreach (var kv in before)
                    {
                        if (!after.TryGetValue(kv.Key, out var b2)) { drift++; continue; }
                        var b1 = kv.Value;
                        if (Mathf.Abs(b1.width - b2.width) > 0.5f || Mathf.Abs(b1.height - b2.height) > 0.5f
                            || Mathf.Abs(b1.x - b2.x) > 0.5f || Mathf.Abs(b1.y - b2.y) > 0.5f)
                        {
                            drift++;
                            if (lines.Count < 4)
                                lines.Add(kv.Key + " " + b1.width.ToString("F0") + "x" + b1.height.ToString("F0")
                                    + "@(" + b1.x.ToString("F0") + "," + b1.y.ToString("F0") + ")→"
                                    + b2.width.ToString("F0") + "x" + b2.height.ToString("F0")
                                    + "@(" + b2.x.ToString("F0") + "," + b2.y.ToString("F0") + ")");
                        }
                    }
                    driftTotal += drift;
                    L("  [" + n + "] 框内按钮=" + before.Count + " 漂移=" + drift
                        + (lines.Count > 0 ? " 明细:" + string.Join(" | ", lines) : ""));
                    Click(FindButton(ui, n), n + "入口(收起)", _log);
                    for (int k = 0; k < 3; k++) yield return null;
                }
                L("  ★判据:6 秒内面板内部按钮尺寸/位置不变=" + (driftTotal == 0 ? "是" : "否(共 " + driftTotal + " 处)"));

                // 2026-09-17 操作中采样(只记日志不设判据):总览开着 → 真点一次[将:磐石]入队 →
                // 对比**同名按钮**的边界。入队会新增一行,新增行下方元素的位移属合法;此日志用于
                // 制作人报"按钮莫名微变"时对照 —— 若同名按钮(不该动的那些)也位移,就是布局没定死。
                Click(FindButton(ui, "总览"), "总览入口(操作采样)", _log);
                for (int k = 0; k < 4; k++) yield return null;
                var beforeAct = SampleCenterButtons(ui);
                var enqueue = FindButton(ui, "将:磐石");
                Click(enqueue, "入队 将:磐石(操作采样)", _log);
                for (int k = 0; k < 4; k++) yield return null;
                var afterAct = SampleCenterButtons(ui);
                int moved = 0, same = 0;
                foreach (var kv in beforeAct)
                {
                    if (!afterAct.TryGetValue(kv.Key, out var b2)) continue;
                    same++;
                    if (Mathf.Abs(b2.width - kv.Value.width) > 0.5f || Mathf.Abs(b2.height - kv.Value.height) > 0.5f
                        || Mathf.Abs(b2.x - kv.Value.x) > 0.5f || Mathf.Abs(b2.y - kv.Value.y) > 0.5f) moved++;
                }
                L("  [操作中采样] 入队前后同名按钮=" + same + " 其中尺寸/位置变动=" + moved
                    + "(信息读数:新增行引起的行位移属合法;若非新增行也动,则是布局未定死)");
                Click(FindButton(ui, "总览"), "总览入口(收起)", _log);
                for (int k = 0; k < 3; k++) yield return null;
            }

            /// <summary>采样当前中央框内全部按钮的世界矩形(键=可见文本,内容驱动尺寸变化会体现在宽高/位置上)。</summary>
            private static Dictionary<string, Rect> SampleCenterButtons(VisualElement ui)
            {
                var result = new Dictionary<string, Rect>();
                var frame = UiRoot.LastCenterRect;
                if (ui == null || frame.width < 1f) return result;
                ui.Query<Button>().ToList().ForEach(b =>
                {
                    var r = b.worldBound;
                    if (r.width < 1f || r.height < 1f) return;                       // 不可见的不算
                    if (!frame.Overlaps(r)) return;                                   // 只看中央框内(面板本体)
                    string key = string.IsNullOrEmpty(b.text) ? b.name : b.text;
                    if (!result.ContainsKey(key)) result[key] = r;                    // 同名取第一个
                });
                return result;
            }

            /// <summary>
            /// 2026-09-14 制作人:"断峰我下令攻击下路的箭塔,他和他的兵全部在箭塔那里发呆"。
            /// 复刻:选一座**敌方箭塔**(优先下路,即行号大的一侧),给三将下令占领,逐秒记录
            /// 状态/是否在打/离塔距离/塔血/守军/是否被屏障挡住/墙段血量。
            /// </summary>
            private IEnumerator RunOrderTowerAttack()
            {
                L("---- 下令打敌塔(复现'发呆') ----");
                var boot = Boot0();
                if (boot == null) { L("  !! 没有战斗场景"); yield break; }
                var sim = boot.Sim;
                var player = AppFlow.PlayerSide;

                // 选一座敌方箭塔:优先"行号最大"(下路),没有就随便一座
                Entity tower = null; float bestRow = -1f;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Building) continue;
                    if (e.Building != BuildingId.Tower || e.Side == player || e.Rubbled) continue;
                    if (e.PosF.Y > bestRow) { bestRow = e.PosF.Y; tower = e; }
                }
                if (tower == null) { L("  !! 找不到敌方箭塔"); yield break; }
                L("  目标塔 id=" + tower.Id + " @" + tower.PosF.X.ToString("F1") + "," + tower.PosF.Y.ToString("F1")
                    + " 守军(正式=" + tower.GarrisonFormal.Count + " 临时=" + tower.GarrisonTemp.Count + ")");

                // 三将摆到塔西侧 10 格(自检摆位,等价于玩家把军队开过去)
                var gens = new List<Entity>();
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier) continue;
                    if (e.Side != player || e.GarrisonBuildingId != 0) continue;
                    gens.Add(e);
                    if (gens.Count >= 3) break;
                }
                foreach (var g in gens)
                {
                    g.PosF = new SimVec2(tower.PosF.X - 10f, tower.PosF.Y);
                    g.HasMoveTarget = false; g.State = EntityState.Idle; g.TargetBuildingId = 0;
                }
                foreach (var kv in sim.Entities)
                {
                    var s0 = kv.Value;
                    if (s0 == null || !s0.IsSoldier || s0.Side != player) continue;
                    if (!gens.Any(g => g.Id == s0.GeneralId)) continue;
                    s0.PosF = new SimVec2(tower.PosF.X - 12f, tower.PosF.Y + (s0.Id % 8) - 4);
                    s0.HasMoveTarget = false; s0.State = EntityState.Idle; s0.TargetBuildingId = 0;
                }

                var res = SiegeSystem.TryIssueRegimentOrder(sim, gens.Select(g => g.Id).ToList(), tower.Id, player, SiegeIntent.Capture);
                L("  下令:TryIssueRegimentOrder Success=" + res.Success + " error=" + res.Error + " 展开士兵=" + res.SoldierIds.Count);
                var foeBase = WallSystem.BaseCenter(player == Side.Blue ? Side.Red : Side.Blue);
                for (int t = 1; t <= 20; t++)
                {
                    for (int k = 0; k < sim.Config.TickRate; k++) sim.Step();
                    if (t % 4 != 0 && t > 4) continue;
                    string st = "";
                    foreach (var g in gens)
                    {
                        var e = sim.GetEntity(g.Id);
                        if (e == null) { st += " [阵亡]"; continue; }
                        st += " {态" + e.State + "/令" + (e.HasMoveTarget ? 1 : 0) + "/塔标" + e.TargetBuildingId
                            + "/距塔" + SimVec2.Distance(e.PosF, tower.PosF).ToString("F1") + "}";
                    }
                    float shell = System.Math.Abs(SimVec2.Distance(foeBase, gens[0].PosF) - WallSystem.Radius);
                    L("  t=" + t + "s 塔归属=" + tower.Side + " 塔血=" + tower.Hp.ToString("F0")
                        + " 守军=" + tower.GarrisonFormal.Count + " 塔距敌基地中心="
                        + SimVec2.Distance(tower.PosF, foeBase).ToString("F1") + "(半径" + WallSystem.Radius + ")"
                        + " 将与敌基地中心距=" + SimVec2.Distance(gens[0].PosF, foeBase).ToString("F1")
                        + " 离屏障皮=" + shell.ToString("F1") + st);
                }
                Shot("acc_order_tower");
            }

            /// <summary>
            /// 2026-09-15 制作人实机:「点敌方建筑无法展开面板,框选自己军队失灵」。
            /// 两个症状共用一个闸门 —— `SelectionSystem.MouseOverPanel`(`IsPointerOverUi`):
            /// 鼠标**按下**时它为真 → 框选根本不开始拖;鼠标**抬起**时它为真 → 点选/建筑点击直接 return。
            /// 而它读的是 8 个面板的静态 `LastPanelRect`,即各面板 `LateUpdate` 回填的 `worldBound`。
            /// UI Toolkit 的 `worldBound` 在元素**脱离可视化树**后**不重置**(保留上一次布局的旧值),
            /// 而中央面板关闭时是"框被 RemoveFromHierarchy + 面板 display:none" —— 于是屏幕正中
            /// 永久留下一块 560x520 的**幽灵拦截区**,把落在上面的点击/框选全吞掉。
            ///
            /// 本场景给三份证据:
            ///   ① **幽灵存在性**:逐个面板打印"原始 worldBound(旧口径)"对比"门禁后矩形(新口径)"。
            ///      已关闭的面板若原始矩形非零 → 就是那块幽灵;门禁后必须 0x0。
            ///   ② **全屏扫描**:9x9 网格铺满屏幕,统计每个点被谁拦 —— 允许出现的只有**真的可见**的
            ///      顶栏/小地图/底部条;绝不允许出现任何已关闭的面板。
            ///   ③ **真实点击链路**:反射拿到 `SelectionInteractionCoordinator`,按左键点敌方建筑
            ///      走同一条 `HandleWorldClick` → 建筑信息面板必须开出来。
            /// </summary>
            private IEnumerator RunUiHitAudit()
            {
                L("---- UI 拦截审计(点敌建/框选被吞) ----");
                var boot = Boot0();
                if (boot == null || boot.Sim == null) { L("  !! 没有战斗场景"); yield break; }
                var sim = boot.Sim;
                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                if (ui == null) { L("  !! 没有 UiRoot.Screen"); yield break; }

                float kUnitPerPx = 1f;
                float pw = ui.resolvedStyle.width;
                if (!float.IsNaN(pw) && pw > 1f && Screen.width > 0) kUnitPerPx = pw / Screen.width;
                L("  屏幕=" + Screen.width + "x" + Screen.height + " 面板宽=" + pw.ToString("F0")
                    + " 系数k=" + kUnitPerPx.ToString("F3"));

                // 复现条件:四个中央面板各开→关两轮(幽灵矩形就是这么留下的)
                var names = new[] { "总览", "英雄升级", "海克斯", "道具" };
                for (int round = 0; round < 2; round++)
                {
                    foreach (var n in names)
                    {
                        Click(FindButton(ui, n), n + "(开#" + (round + 1) + ")", _log);
                        for (int k = 0; k < 4; k++) yield return null;
                        Click(FindButton(ui, n), n + "(关#" + (round + 1) + ")", _log);
                        for (int k = 0; k < 4; k++) yield return null;
                    }
                }
                for (int k = 0; k < 3; k++) yield return null;

                // ① 幽灵存在性:旧口径(裸 worldBound) vs 新口径(门禁后)
                L("  ① 原始 worldBound(旧口径) vs 门禁后矩形(新口径) —— 全部面板均已关闭:");
                int ghosts = 0;
                foreach (var pr in PanelRoots(boot))
                {
                    if (pr.Value == null) { L("    " + Pad(pr.Key) + " 拿不到 _root"); continue; }
                    var raw = pr.Value.worldBound;
                    var gated = UiKit.VisibleRectOrZero(pr.Value);
                    bool ghost = raw.width > 1f && raw.height > 1f && gated.width <= 0f;
                    if (ghost) ghosts++;
                    var centerUi = new Vector2(Screen.width * 0.5f * kUnitPerPx, Screen.height * 0.5f * kUnitPerPx);
                    L("    " + Pad(pr.Key) + " 原始=" + RectStr(raw)
                        + (ghost ? "  ← 幽灵拦截区(旧口径会吞掉这块输入)" : "")
                        + (ghost && raw.Contains(centerUi) ? "  ★含屏幕中心" : "")
                        + "  门禁后=" + RectStr(gated));
                }
                L("    原始非零但门禁归零的面板数=" + ghosts + " → 每个都需要门禁才不吞输入");
                L(SelectionInteractionCoordinator.DumpBlockingRects(
                    new Vector2(Screen.width * 0.5f * kUnitPerPx, Screen.height * 0.5f * kUnitPerPx)));

                // ② 全屏扫描:9x9 网格,统计被谁拦
                var tally = new Dictionary<string, int>();
                var closedHit = new List<string>();
                var closedSet = new HashSet<string> { "总览面板", "英雄升级面板", "海克斯面板", "道具面板", "建筑面板", "暂停遮罩" };
                for (int gy = 0; gy < 9; gy++)
                    for (int gx = 0; gx < 9; gx++)
                    {
                        float sx = Screen.width * (gx + 0.5f) / 9f;
                        float sy = Screen.height * (gy + 0.5f) / 9f;
                        var uiPt = new Vector2(sx * kUnitPerPx, sy * kUnitPerPx);
                        // 选中 1 个(让"撤退按钮"分支也参与),撤退按钮矩形按 SelectionSystem.OnGUI 的真实值
                        var retreat = new Rect(20f, Screen.height - 60f, 120f, 40f);
                        string why = SelectionInteractionCoordinator.BlockingRectName(uiPt, retreat.Contains(new Vector2(sx, sy)));
                        if (why == null) continue;
                        tally[why] = (tally.TryGetValue(why, out var c) ? c : 0) + 1;
                        if (closedSet.Contains(why))
                            closedHit.Add("(" + sx.ToString("F0") + "," + sy.ToString("F0") + ")被[" + why + "]");
                    }
                var parts = new List<string>();
                foreach (var kv in tally) parts.Add(kv.Key + "x" + kv.Value);
                L("  ② 全屏 9x9=81 点扫描:被拦点=" + (parts.Count == 0 ? "无" : string.Join(" ", parts)));
                L("     被**已关闭面板**吞掉的点=" + (closedHit.Count == 0 ? "0(正确)" : closedHit.Count + " → " + string.Join(";", closedHit)));

                // 反事实:拿"已关闭面板的**原始** worldBound"再扫同一张网格 —— 那就是修复前旧口径的拦截面,
                // 同一次运行里把"改前会吞多少 / 改后吞多少"两个数摆在一起(红-绿对照,不用回滚代码)。
                int wouldSwallow = 0;
                var wouldList = new List<string>();
                foreach (var gy in Enumerable.Range(0, 9))
                    foreach (var gx in Enumerable.Range(0, 9))
                    {
                        float sx = Screen.width * (gx + 0.5f) / 9f;
                        float sy = Screen.height * (gy + 0.5f) / 9f;
                        var uiPt = new Vector2(sx * kUnitPerPx, sy * kUnitPerPx);
                        foreach (var pr in PanelRoots(boot))
                        {
                            if (pr.Value == null) continue;
                            var raw = pr.Value.worldBound;
                            if (raw.width <= 1f || raw.height <= 1f) continue;          // 本来就没内容
                            if (UiKit.VisibleRectOrZero(pr.Value).width > 0f) continue; // 真的可见(顶栏/小地图)不算幽灵
                            if (!raw.Contains(uiPt)) continue;
                            wouldSwallow++;
                            wouldList.Add("[" + pr.Key + "](" + sx.ToString("F0") + "," + sy.ToString("F0") + ")");
                            break;
                        }
                    }
                L("     [红-绿对照] 同一张网上,修复前的旧口径会吞掉 " + wouldSwallow + " 点 / 修复后吞掉 0 点");
                if (wouldList.Count > 0)
                    L("       旧口径吞掉的点(前 12 个):" + string.Join(";", wouldList.GetRange(0, System.Math.Min(12, wouldList.Count))));
                L("  ★判据一:已关闭面板吞掉的点数为 0 = " + (closedHit.Count == 0 ? "是" : "否")
                    + "  ★判据一b:旧口径确实会吞(证明修复必要)=" + (wouldSwallow > 0 ? "是(" + wouldSwallow + " 点)" : "否"));

                // ③ 真实点击链路:左键点敌方建筑 = SelectionSystem → coordinator.HandleWorldClick
                Entity foeB = null; float bestD = float.MaxValue;
                var mid = new SimVec2(TerrainMap.CenterX, TerrainMap.CenterY);
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Building || e.Side == AppFlow.PlayerSide) continue;
                    if (e.Rubbled) continue;
                    float d = SimVec2.Distance(e.PosF, mid);
                    if (d < bestD) { bestD = d; foeB = e; }
                }
                var panel = boot.GetComponent<BuildingCommandPanel>();
                bool clickOk = false;
                if (foeB != null && panel != null)
                {
                    panel.ClearTarget("audit_reset");
                    var sel = boot.GetComponent<SelectionSystem>();
                    object inter = null;
                    if (sel != null)
                    {
                        var f = typeof(SelectionSystem).GetField("_interaction",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f != null) inter = f.GetValue(sel);
                    }
                    var coord = inter as SelectionInteractionCoordinator;
                    if (coord != null)
                    {
                        // 世界点→屏幕点仍在屏外也算数:走的是**命中判定**本身,不是像素采样
                        coord.HandleWorldClick(0, true, foeB.PosF);
                        for (int k = 0; k < 2; k++) yield return null;
                        clickOk = panel.HasTarget && panel.TargetId == foeB.Id;
                        L("  ③ 真实链路点敌方建筑#" + foeB.Id + "(" + foeB.Building + ") → 面板目标="
                            + panel.TargetId + "(期望 " + foeB.Id + ") HasTarget=" + panel.HasTarget);
                        // 顺带:同一座建筑用**同一套坐标换算**问一次"会不会被拦"
                        var cam = Camera.main;
                        if (cam != null)
                        {
                            var sp = cam.WorldToScreenPoint(BoardView.WorldPos(foeB.PosF));
                            bool onScreen = sp.z > 0f && sp.x >= 0f && sp.x <= Screen.width
                                && sp.y >= 0f && sp.y <= Screen.height;
                            L("     该建筑投影屏幕=(" + sp.x.ToString("F0") + "," + (Screen.height - sp.y).ToString("F0")
                                + ") " + (onScreen ? "在屏内" : "在屏外(相机此刻没看那边,不影响命中判定)"));
                        }
                    }
                    else L("  !! 拿不到 SelectionInteractionCoordinator(反射失败),跳过真实链路");
                }
                L("  ★判据二:点敌方建筑能展开信息面板=" + (clickOk ? "是" : "否"));
                Shot("acc_ui_hit");
            }

            private static string Pad(string s) => s.Length >= 8 ? s : s + new string(' ', 8 - s.Length);

            /// <summary>各面板的 `_root` 元素(反射取名,只为审计打印;拿不到就是 null)。</summary>
            private static List<KeyValuePair<string, VisualElement>> PanelRoots(GameBootstrap boot)
            {
                var list = new List<KeyValuePair<string, VisualElement>>();
                Add<BarracksOverviewPanel>("总览");
                Add<UpgradeCommandPanel>("英雄升级");
                Add<HexCommandPanel>("海克斯");
                Add<ToolCommandPanel>("道具");
                Add<BuildingCommandPanel>("建筑");
                Add<MinimapPanel>("小地图");
                Add<TopBarPanel>("顶栏");
                Add<PauseOverlayPanel>("暂停遮罩");
                return list;

                void Add<T>(string label) where T : Component
                {
                    var comp = Object.FindFirstObjectByType<T>();
                    if (comp == null) { list.Add(new KeyValuePair<string, VisualElement>(label, null)); return; }
                    var f = typeof(T).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance);
                    list.Add(new KeyValuePair<string, VisualElement>(label,
                        f != null ? f.GetValue(comp) as VisualElement : null));
                }
            }

            private IEnumerator RunPanelStability()
            {
                L("---- 面板固定居中 ----");
                if (Boot0() == null) { L("  !! 没有战斗场景"); yield break; }
                var ui = UiRoot.Current != null ? UiRoot.Current.Screen : null;
                Click(FindButton(ui, "道具"), "道具入口", _log);
                for (int i = 0; i < 3; i++) yield return null;
                var first = ToolCommandPanel.LastPanelRect;
                var scr0 = UiRoot.Current != null && UiRoot.Current.Screen != null
                    ? UiRoot.Current.Screen.worldBound : Rect.zero;
                float maxShift = 0f, maxSize = 0f; Rect last = first;
                for (int i = 0; i < 40; i++)   // 4 秒,每 0.1 秒采一次
                {
                    yield return new WaitForSecondsRealtime(0.1f);
                    var r = ToolCommandPanel.LastPanelRect;
                    maxShift = Mathf.Max(maxShift,
                        Mathf.Max(Mathf.Abs(r.x - first.x), Mathf.Abs(r.y - first.y)));
                    maxSize = Mathf.Max(maxSize,
                        Mathf.Max(Mathf.Abs(r.width - first.width), Mathf.Abs(r.height - first.height)));
                    last = r;
                }
                var scr1 = UiRoot.Current != null && UiRoot.Current.Screen != null
                    ? UiRoot.Current.Screen.worldBound : Rect.zero;
                L("  面板矩形 首=" + RectStr(first) + " 末=" + RectStr(last));
                L("  Screen 首=" + RectStr(scr0) + " 末=" + RectStr(scr1));
                L("  ★判据:4 秒内面板不位移=" + (maxShift < 1f ? "是" : "否(最大位移 " + maxShift.ToString("F1") + ")")
                    + " 尺寸不变=" + (maxSize < 1f ? "是" : "否(最大变化 " + maxSize.ToString("F1") + ")"));
                Shot("acc_panel_center");
            }

            // ================= 点敌方建筑 → 信息面板(驻军情报切片) =================
            /// <summary>制作人口径:"点击敌方建筑显示信息面板:驻军切片(标记信息获取时间),没得到过信息显示？,
            /// 显示占领/摧毁,删除现在的占领/摧毁ui。" 这里用**与点击同一条链路**验证:
            /// 取点(BuildingCommandModel.PickBuildingAny,与世界点击同一函数)→ SetTarget(点击后协调层调的就是它)。</summary>
            private IEnumerator RunInfoPanel()
            {
                L("---- 点敌方建筑:信息面板 ----");
                var boot = Boot0();
                if (boot == null || boot.Sim == null) { L("  !! 没有战斗场景"); yield break; }
                var sim = boot.Sim;
                var panel = boot.GetComponent<BuildingCommandPanel>();
                yield return null;

                // 找一座**从未侦察过**的敌方建筑
                int unknownId = 0; SimVec2 unknownAt = default;
                int knownId = 0; SimVec2 knownAt = default;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Building || e.Side == AppFlow.PlayerSide) continue;
                    if (VisionSystem.IsBuildingKnown(sim, AppFlow.PlayerSide, e.Id)) continue;
                    unknownId = e.Id; unknownAt = e.PosF; break;
                }
                L("  未侦察的敌方建筑 id=" + unknownId + "(情报为'？'的那种)");
                if (unknownId != 0 && panel != null)
                {
                    int picked = BuildingCommandModel.PickBuildingAny(sim, unknownAt);
                    L("  点选(与世界点击同一函数)PickBuildingAny=" + picked + "(期望 " + unknownId + ")");
                    panel.SetTarget(picked);
                    yield return null; yield return null;
                    Shot("acc_info_unknown");
                }

                // 派侦察兵到另一座敌建筑旁 → 产生带时间的情报,再看面板
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e == null || e.Kind != EntityKind.Building || e.Side == AppFlow.PlayerSide) continue;
                    if (e.Id == unknownId) continue;
                    knownId = e.Id; knownAt = e.PosF; break;
                }
                if (knownId != 0)
                {
                    var scout = SpawnAtFreeCell(sim, AppFlow.PlayerSide, EntityKind.Unit,
                        new GridPos((int)knownAt.Y, (int)(knownAt.X - 5f)), "RunInfoPanel·侦察");
                    if (scout == null) { L("  !! 侦察单位生不出来,跳过情报刷新"); goto afterInfoScout; }
                    scout.Hero = HeroId.PanShi; scout.Hp = scout.MaxHp = 100f;
                    for (int i = 0; i < 4 * sim.Config.TickRate; i++) sim.Step();
                    yield return null;
                    var snap = VisionSystem.LastKnownBuilding(sim, AppFlow.PlayerSide, knownId);
                    L("  侦察后 快照=" + (snap == null ? "无" : ("驻军 " + snap.GarrisonCount
                        + " 获取于 tick " + snap.Tick)) + " 当前 tick=" + sim.Tick);
                    if (panel != null)
                    {
                        panel.SetTarget(BuildingCommandModel.PickBuildingAny(sim, knownAt));
                        yield return null; yield return null;
                        Shot("acc_info_known");
                    }
                }
                afterInfoScout: ;
                L("  (截图 acc_info_unknown.png / acc_info_known.png 供目视:前者驻军应显示'？',后者应带情报时间)");

                // ---- 左键=只开面板 / 右键=按已设偏好直接进攻(制作人 2026-09-14) ----
                var sel = boot.GetComponent<SelectionSystem>();
                var selList = SelectionOf(sel);
                int target = unknownId != 0 ? unknownId : knownId;
                if (selList != null && target != 0 && panel != null)
                {
                    selList.Clear();
                    foreach (var kv in sim.Entities)
                    {
                        var e = kv.Value;
                        if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier) continue;
                        if (e.Side != AppFlow.PlayerSide || e.State == EntityState.Dead) continue;
                        if (e.GarrisonBuildingId != 0) continue;
                        selList.Add(e.Id);
                        if (selList.Count >= 3) break;
                    }
                    L("  已选兵团=" + string.Join(",", System.Array.ConvertAll(selList.ToArray(), x => x.ToString())));

                    // ① 左键:只开面板,不得顺带给兵团下令
                    // 2026-09-14 度量修正:`HasMoveTarget` 不能当代理量 —— 孤儿士兵(主将阵亡后)会自己走回
                    // 兵营变补兵、自带移动目标,会被误判成"左键顺带下令"。改为比对**点击前后同一帧的 MoveTarget**。
                    var beforeMove = new Dictionary<int, GridPos>();
                    foreach (var id in selList) { var e0 = sim.GetEntity(id); if (e0 != null) beforeMove[id] = e0.MoveTarget; }
                    panel.SetTarget(BuildingCommandModel.PickBuildingAny(sim, TargetPosOf(sim, target)));
                    yield return null;
                    int moved = 0;
                    foreach (var id in selList)
                    {
                        var e0 = sim.GetEntity(id);
                        if (e0 == null) continue;
                        GridPos prev;
                        if (beforeMove.TryGetValue(id, out prev) && e0.MoveTarget != prev) moved++;
                    }
                    L("  ①左键点建筑:面板已开=" + (panel.TargetId == target) + " 被顺带下令的兵团数=" + moved
                        + " ★判据:左键只开面板=" + (moved == 0 ? "是" : "否"));

                    // ② 右键:该建筑**没有**偏好 → 应被拒(并提示先设偏好)
                    var b0 = sim.GetEntity(target);
                    b0.Intent = SiegeIntent.None;
                    bool denied = panel.TryOrderPreferredOn(target);
                    L("  ②右键(无偏好):下令被拒=" + (!denied) + " 反馈=\"" + panel.LastFeedback + "\""
                        + " ★判据:无偏好不下令=" + (!denied ? "是" : "否"));

                    // ③ 设偏好(占领)后再右键 → 应成功且兵团拿到行军令
                    string ierr;
                    bool setOk = SiegeSystem.SetIntent(sim, target, AppFlow.PlayerSide, SiegeIntent.Capture, out ierr);
                    foreach (var id in selList) sim.GetEntity(id).HasMoveTarget = false;
                    bool ordered = panel.TryOrderPreferredOn(target);
                    yield return null;
                    int movingNow = 0;
                    foreach (var id in selList) if (sim.GetEntity(id).HasMoveTarget) movingNow++;
                    L("  ③设偏好(占领)后右键:设偏好=" + setOk + " 下令成功=" + ordered
                        + " 拿到行军令的兵团数=" + movingNow + " 反馈=\"" + panel.LastFeedback + "\""
                        + " ★判据:有偏好则直接进攻=" + (ordered && movingNow > 0 ? "是" : "否"));

                    // ④ 2026-09-15 制作人:"**还是不攻击建筑,全在发呆**"(附图:近战主将站在敌方箭塔旁不动)。
                    // 真机复现:把偏好改成**摧毁**、对同一座敌方建筑右键下令,然后**在游戏里推 25 秒**,
                    // 看它到底掉不掉血。判据两条:①意图是 Destroy 且没被自己人顺手占领作废;
                    // ②建筑血真的往下走(旧口径:全军走到"碰撞半径+5"停住,一点血都不掉)。
                    var tower2 = sim.GetEntity(target);
                    if (tower2 != null && tower2.Kind == EntityKind.Building)
                    {
                        tower2.Rubbled = false;
                        tower2.Side = AppFlow.PlayerSide == Side.Blue ? Side.Red : Side.Blue;
                        tower2.Hp = System.Math.Max(200f, tower2.Hp);
                        // 制作人截图里的状态是"军队已经在建筑旁边",所以先把这三支兵团摆到建筑西侧 9 米
                        // (自检等价于玩家自己把军队开过去)。**远征距离不是本条判据要测的东西** ——
                        // 上一版没摆位,25 秒压根走不到,判据假红。
                        var twPos0 = new SimVec2(tower2.Pos.Col + 0.5f, tower2.Pos.Row + 0.5f);
                        for (int i = 0; i < selList.Count; i++)
                        {
                            var e0 = sim.GetEntity(selList[i]);
                            if (e0 == null) continue;
                            e0.PosF = new SimVec2(twPos0.X - 9f - i * 1.5f, twPos0.Y);
                            e0.Pos = e0.PosF.ToGrid();
                            e0.HasMoveTarget = false; e0.State = EntityState.Idle;
                            e0.TargetBuildingId = 0; e0.Detour = null;
                        }
                        // **士兵必须一起搬**:上一版只搬了主将,士兵留在老家 → 主将走两步就"回去找自己的兵"
                        // (编队跟随/支援规则都没问题,是**自检摆位**造了个游戏里不存在的状态),
                        // 现场表现为"走到 3.75 米又退回 12 米不动"。这里按兵团整体搬到建筑西侧。
                        int movedSoldiers = 0;
                        foreach (var kv in sim.Entities)
                        {
                            var sd = kv.Value;
                            if (sd == null || !sd.IsSoldier) continue;
                            if (!selList.Contains(sd.GeneralId)) continue;
                            if (sd.State == EntityState.Dead || sd.GarrisonBuildingId != 0) continue;
                            int k = movedSoldiers++;
                            sd.PosF = new SimVec2(twPos0.X - 12f - (k % 6) * 1.2f, twPos0.Y - 4f + (k / 6) * 1.6f);
                            sd.Pos = sd.PosF.ToGrid();
                            sd.HasMoveTarget = false; sd.State = EntityState.Idle;
                            sd.TargetBuildingId = 0; sd.Detour = null;
                        }
                        L("     摆位:主将 " + selList.Count + " 名 + 随行士兵 " + movedSoldiers + " 名,全部搬到建筑西侧");
                        string derr;
                        bool dset = SiegeSystem.SetIntent(sim, target, AppFlow.PlayerSide, SiegeIntent.Destroy, out derr);
                        bool dordered = panel.TryOrderPreferredOn(target);
                        L("  ④摆位后设偏好(摧毁)后右键:摆位=建筑西侧9米 设偏好=" + dset + " 下令成功=" + dordered
                            + " 建筑血(下令时)=" + tower2.Hp.ToString("F0") + " 反馈=\"" + panel.LastFeedback + "\"");
                        float hp0 = tower2.Hp;
                        for (int t = 1; t <= 5; t++)   // 25 秒(每拍 5 秒)
                        {
                            for (int k = 0; k < sim.Config.TickRate * 5; k++) sim.Step();
                            var tw = sim.GetEntity(target);
                            if (tw == null) break;
                            float dmin = float.MaxValue;
                            foreach (var id in selList)
                            {
                                var e0 = sim.GetEntity(id);
                                if (e0 == null || e0.State == EntityState.Dead) continue;
                                dmin = Mathf.Min(dmin, SimVec2.Distance(e0.PosF, new SimVec2(tw.Pos.Col + 0.5f, tw.Pos.Row + 0.5f)));
                            }
                            L("     t=" + (t * 5) + "s 建筑血=" + tw.Hp.ToString("F0") + " 归属=" + tw.Side
                                + " 意图=" + tw.Intent + " 废墟=" + tw.Rubbled
                                + " 最近主将距塔心=" + (dmin == float.MaxValue ? "-" : dmin.ToString("F2")));
                        }
                        var twEnd = sim.GetEntity(target);
                        float hpEnd = twEnd != null ? twEnd.Hp : 0f;
                        bool intentAlive = twEnd != null && (twEnd.Intent == SiegeIntent.Destroy || twEnd.Rubbled);
                        L("  ④判据:摧毁令没被顺手占领作废=" + (intentAlive ? "是" : "否")
                            + " ★判据:建筑真的掉血=" + (hpEnd < hp0 ? "是(" + hp0.ToString("F0") + "→" + hpEnd.ToString("F0") + ")" : "否(旧口径=原地发呆)"));

                        // ⑤ 2026-09-15 制作人:"**被摧毁的箭塔还是会攻击,有点抽象**"。
                        // 就在这座塔刚被打成废墟的状态下,推 5 秒,数**以这座塔为攻击者**的伤害事件。
                        // 判据:必须是 0(旧口径:废墟照常开火,而且它 `Side=Neutral` 谁都不拦 = 敌我一起打)。
                        var tw5 = sim.GetEntity(target);
                        if (tw5 != null && tw5.Rubbled)
                        {
                            int fromTower = 0;
                            System.Action<SimEvent> tap = ev =>
                            {
                                if (ev.Type == SimEventType.DamageDealt && ev.AttackerId == target) fromTower++;
                            };
                            sim.AddEventListener(tap);
                            for (int k = 0; k < sim.Config.TickRate * 5; k++) sim.Step();
                            sim.RemoveEventListener(tap);
                            L("  ⑤废墟后 5 秒内以该塔为攻击者的伤害事件=" + fromTower
                                + " 塔Rubbled=" + tw5.Rubbled + " 塔Side=" + tw5.Side
                                + " ★判据:废墟不再攻击=" + (fromTower == 0 ? "是" : "否(废墟还在开火)"));
                        }
                        else L("  ⑤该塔已不是废墟(被重建/被占),跳过废墟攻击检查");
                    }
                }
                else L("  !! 无法验证左右键分工(没选到兵团/没有敌方建筑)");
            }

            /// <summary>
            /// 自检专用出生:先试想要的那格,被真实单位占了(0.5 米内有人)就在附近螺旋找一格空的。
            /// 2026-09-15:自检里"派个侦察单位过去"原来写死 `(redPos.X - 6, redPos.Y)`,一旦那格正好被
            /// 战场上走过来的单位占着,`SpawnEntity` 直接抛 `出生点过近` —— 整条自检链就断在这里。
            /// 那是**自检摆位**的问题,不是游戏问题,所以这里兜住并明确记录换到了哪一格。
            /// </summary>
            private static Entity SpawnAtFreeCell(SimCore sim, Side side, EntityKind kind, GridPos want, string tag)
            {
                for (int r = 0; r <= 6; r++)
                {
                    for (int dr = -r; dr <= r; dr++)
                    {
                        for (int dc = -r; dc <= r; dc++)
                        {
                            if (r > 0 && Mathf.Abs(dr) != r && Mathf.Abs(dc) != r) continue;   // 只扫这一圈的边框
                            var c = new GridPos(want.Row + dr, want.Col + dc);
                            if (c.Row < 2 || c.Row >= LaneGrid.Rows - 2 || c.Col < 2 || c.Col >= LaneGrid.Cols - 2) continue;
                            try
                            {
                                var e = sim.SpawnEntity(side, kind, c);
                                if (r > 0 || dr != 0 || dc != 0)
                                    Debug.Log("[accept]   (" + tag + ")出生点 " + want + " 被占 → 换到 " + c);
                                return e;
                            }
                            catch (System.InvalidOperationException) { }
                        }
                    }
                }
                Debug.LogWarning("[accept]   (" + tag + ")附近找不到空位,放弃这次出生");
                return null;
            }

            private static SimVec2 TargetPosOf(SimCore sim, int id)
            {
                var e = sim.GetEntity(id);
                return e != null ? e.PosF : default;
            }

            /// <summary>取 SelectionSystem 的实时选择列表(它同时就是建筑面板用的那份 _selectedIds)。
            /// 自检无法注入鼠标,只能直接改这份列表来模拟"框选"。</summary>
            private static List<int> SelectionOf(SelectionSystem sel)
            {
                if (sel == null) return null;
                var f = typeof(SelectionSystem).GetField("_selected",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                return f != null ? f.GetValue(sel) as List<int> : null;
            }

            /// <summary>视图下的模型层级(只取前几层,够判断是英雄模型还是程序化士兵)。</summary>
            private static string Chain(GameObject go)
            {
                var parts = new List<string>();
                var t = go.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c.name == "HpBar" || c.name == "CountLabel") continue;
                    parts.Add(c.name);
                    // 逐风是骑乘体:外层是 Mount/Horse,英雄模型在它里面 → 必须多看两层
                    for (int j = 0; j < c.childCount; j++)
                    {
                        var d = c.GetChild(j);
                        parts.Add(c.name + "/" + d.name);
                        for (int k = 0; k < d.childCount; k++) parts.Add(c.name + "/" + d.name + "/" + d.GetChild(k).name);
                    }
                }
                return string.Join(" ", parts.ToArray());
            }

            private static string QueueDump(SimCore sim, int baseId)
            {
                var b = sim.GetEntity(baseId);
                if (b == null || b.Prod == null) return "无生产集";
                var parts = new List<string>();
                for (int i = 0; i < b.Prod.Slots.Length; i++)
                {
                    var s = b.Prod.Slots[i];
                    var q = new List<string>();
                    foreach (var h in s.Pending) q.Add(h.ToString());
                    parts.Add("槽" + i + "[" + (s.Current.HasValue ? s.Current.Value.ToString() : "-")
                        + "|" + string.Join(",", q.ToArray()) + "]");
                }
                return string.Join(" ", parts.ToArray());
            }

            private static string Fmt(Vector3 v)
            {
                return "(" + v.x.ToString("F1") + "," + v.y.ToString("F1") + "," + v.z.ToString("F1") + ")";
            }

            private static string RectStr(Rect r)
            {
                if (r.width <= 0f || r.height <= 0f) return "空(未上报)";
                return "(" + r.x.ToString("F0") + "," + r.y.ToString("F0") + ","
                    + r.width.ToString("F0") + "x" + r.height.ToString("F0") + ")";
            }
        }
    }
}
