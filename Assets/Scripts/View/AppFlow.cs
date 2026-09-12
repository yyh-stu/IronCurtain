using IronCurtain.Sim;
using UnityEngine.SceneManagement;

namespace IronCurtain.View
{
    /// <summary>流程路由(2026-09-12 主界面定稿):主界面 → AI 对战(简单/困难)或练习场。
    /// 模式与操控阵营存静态字段,跨场景加载与"再来一局"重载自然保持;回主界面时复位为默认。
    /// 练习场换边=重载式(制作人定稿):翻转阵营后重载战斗场景,不做运行时热切换。</summary>
    public static class AppFlow
    {
        public enum Mode { Menu, AiSimple, AiHard, Practice }

        public static Mode Current = Mode.Menu;
        public static Side PlayerSide = Side.Blue;

        public const string MenuScenePath = "Assets/Scenes/Menu.unity";
        public const string BattleScenePath = "Assets/Scenes/Main.unity";

        public static void EnterMenu()
        {
            Current = Mode.Menu;
            PlayerSide = Side.Blue;
            SceneManager.LoadScene(MenuScenePath);
        }

        public static void StartAi(bool hard)
        {
            Current = hard ? Mode.AiHard : Mode.AiSimple;
            PlayerSide = Side.Blue;
            SceneManager.LoadScene(BattleScenePath);
        }

        public static void StartPractice()
        {
            Current = Mode.Practice;
            PlayerSide = Side.Blue;
            SceneManager.LoadScene(BattleScenePath);
        }

        /// <summary>练习场重载式换边:翻转操控阵营后重载战斗场景。</summary>
        public static void SwitchPracticeSide()
        {
            PlayerSide = PlayerSide == Side.Blue ? Side.Red : Side.Blue;
            SceneManager.LoadScene(BattleScenePath);
        }
    }
}
