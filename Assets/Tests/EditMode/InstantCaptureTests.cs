using System.Linq;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-16 三十七轮(实机判据"塔已归我=否"追因):
    /// 三十七轮第一次 IC_ACCEPT=all 里,[有守军(交换)] 场景 25.2 秒占领后 12 秒内又被翻回红。
    /// 逐路径排查定位机制 —— **即时占领路径不设保护期**:`CapturedUntilTick` 只在
    /// SiegeSystem 的交换占领处设置,而它的注释语义("刚占领:3 秒内不被即时夺回,防来回翻面")
    /// 明确适用于**任何**刚易主的建筑。慢攻时进攻方士兵常先于主将一步"接触即占领"空塔
    /// (无驻军、无保护),主将尚未进城、身边的己方单位又被自动层拉走去打被逐守军 →
    /// "无人防守"窗口敞开 → 刚被赶出来的守军原地夺回,占领白打。
    /// 本套把"即时占领也有 3 秒保护期、保护期后仍可正常翻回"钉成回归。
    /// </summary>
    public sealed class InstantCaptureTests
    {
        private static GameConfig _cfg;

        private static SimCore NewSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            return new SimCore(new SimConfig(), new SeededRng(11)) { Game = _cfg };
        }

        private static Entity Unit(SimCore sim, Side side, GridPos pos)
        {
            var u = sim.SpawnEntity(side, EntityKind.Unit, pos);
            u.MoveSpeed = 0f;   // 摆位后不动,占领判定只看接触距离
            u.Hp = u.MaxHp = 400f;
            return u;
        }

        private static void Step(SimCore sim, int n) { for (int i = 0; i < n; i++) sim.Step(); }

        [Test]
        public void T1_即时占领后3秒内不被夺回()
        {
            var sim = NewSim();
            var tower = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 100)); // 空塔(无守军无驻军)
            var blue = Unit(sim, Side.Blue, new GridPos(80, 101));   // 贴塔(1.0 米 ≤ 接触半径)
            var red = Unit(sim, Side.Red, new GridPos(10, 10));     // 初始远离,不干扰蓝方占领

            Step(sim, 2);
            Assert.AreEqual(Side.Blue, tower.Side, "前提:蓝方贴塔应即时占领空塔");
            long capturedTick = sim.Tick;

            // 蓝方占领者走开(等价实战:被自动层拉走去打别处),红方贴上来想夺回
            blue.PosF = new SimVec2(10.5f, 40.5f);
            red.PosF = new SimVec2(100.5f, 80.5f + 1.0f);

            Step(sim, 25);   // 保护期内(3 秒 = 30 tick;占领可能发生在首 tick,留足边界)
            Assert.AreEqual(Side.Blue, tower.Side,
                "即时占领同样应获得 3 秒保护期(旧口径:蓝方单位一走开,塔立刻被翻回 → 占领白打)");

            Step(sim, 10);   // 过保护期,红方仍贴着
            Assert.AreEqual(Side.Red, tower.Side, "保护期过后应可正常夺回(保护不是永久)");
            Assert.GreaterOrEqual(sim.Tick, capturedTick + 30, "时序自检:确实越过了 30 tick");
        }

        [Test]
        public void T2_有人守着的塔不会被接触占领_反向保护()
        {
            var sim = NewSim();
            var tower = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 100));
            var defender = Unit(sim, Side.Red, new GridPos(80, 101));  // 红方守在塔边
            Unit(sim, Side.Blue, new GridPos(80, 102));                 // 蓝方贴着也没用

            Step(sim, 50);
            Assert.AreEqual(Side.Red, tower.Side, "当前归属方单位在半径内 = 有人守,不得被接触占领");
        }
    }
}
