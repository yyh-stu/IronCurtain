using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine.SceneManagement;

namespace IronCurtain.View
{
    /// <summary>流程路由(2026-09-12 主界面定稿;2026-09-13 加 BP):主界面 → BP 选将 → 对局。
    /// 模式与操控阵营存静态字段,跨场景加载与"再来一局"重载自然保持;回主界面时复位为默认。
    /// 练习场换边=重载式(制作人定稿):翻转阵营后重载战斗场景,不做运行时热切换。</summary>
    public static class AppFlow
    {
        public enum Mode { Menu, AiSimple, AiHard, Practice }

        public static Mode Current = Mode.Menu;
        public static Side PlayerSide = Side.Blue;

        /// <summary>BP 选将结果(2026-09-13):null=未走 BP(测试/默认开局),否则 3 将/方。
        /// 允许蓝红选同一英雄;同方 3 轮不重复(BP 规则)。进战斗由 MatchSetup 消费。</summary>
        public static List<HeroId> BpBluePicks;
        public static List<HeroId> BpRedPicks;

        public const string MenuScenePath = "Assets/Scenes/Menu.unity";
        public const string BpScenePath = "Assets/Scenes/BP.unity";
        public const string BattleScenePath = "Assets/Scenes/Main.unity";

        public static void EnterMenu()
        {
            Current = Mode.Menu;
            PlayerSide = Side.Blue;
            BpBluePicks = BpRedPicks = null;   // 回主界面清 BP 结果
            SceneManager.LoadScene(MenuScenePath);
        }

        public static void StartAi(bool hard)
        {
            Current = hard ? Mode.AiHard : Mode.AiSimple;
            PlayerSide = Side.Blue;
            SceneManager.LoadScene(BpScenePath);   // 2026-09-13:开始游戏先 BP 选将再入局
        }

        public static void StartPractice()
        {
            Current = Mode.Practice;
            PlayerSide = Side.Blue;
            // 2026-09-13 制作人:训练场无 BP(直接入局;开局默认三将,六个将均可生产、名额上限无限制)
            BpBluePicks = BpRedPicks = null;
            SceneManager.LoadScene(BattleScenePath);
        }

        /// <summary>BP 完成 → 进战斗(BpBootstrap 调;模式与选将已就位)。</summary>
        public static void EnterBattle() => SceneManager.LoadScene(BattleScenePath);

        /// <summary>练习场重载式换边:翻转操控阵营后重载战斗场景。</summary>
        /// <summary>
        /// 2026-09-13 制作人:"训练场换边不是重新载入,是换个边继续"。
        /// 改为**原地换边**:保留当前对局(兵力/比分/时间继续),只把视图/输入层按新阵营重建
        /// (见 GameBootstrap.BuildView/TeardownView)。找不到在跑的 bootstrap 时兜底走旧重载路径。
        /// </summary>
        public static void SwitchPracticeSide()
        {
            PlayerSide = PlayerSide == Side.Blue ? Side.Red : Side.Blue;
            // 2026-09-13/14 制作人口径(三次明确):"训练场换边不是重载,是换个边继续"、"只切换阵营和视野状态,
            // **不要改相机位置**" → 原地换边;相机与 CameraRig 一律不拆、不重建、不归位(见 GameBootstrap.TeardownView)。
            var boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            if (boot != null && boot.Sim != null)
            {
                UnityEngine.Debug.Log("[AppFlow] 原地换边 side=" + PlayerSide + "(保留对局)");
                boot.RequestSideRebuild();
                return;
            }
            SceneManager.LoadScene(BattleScenePath);   // 兜底:找不到实例才走重载
        }
    }
}
