using System.Collections.Generic;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class FormationTests
    {
        private static GameConfig _cfg;

        private static SimCore NewSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig(), new SeededRng(1));
            sim.Game = _cfg;
            return sim;
        }

        private static bool RunUntil(SimCore sim, System.Func<bool> cond, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (cond()) return true;
                sim.Step();
            }
            return cond();
        }

        [TearDown]
        public void TearDown()
        {
            AiSystem.Reset();
            VisionSystem.Reset();
            HexSystem.Reset();
            SiegeSystem.Reset();
        }

        [Test]
        public void T1_LineFormation_NoEnemy()
        {
            var sim = NewSim();
            var heavy = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            var warrior = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.DuanFeng, new GridPos(15, 108));
            var archer = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhuiRi, new GridPos(15, 116));

            var plan = RegimentFormationSystem.PlanFormation(sim, new List<int> { heavy.Id, warrior.Id, archer.Id });
            Assert.IsFalse(plan.ScreenMode, "无敌应走常规模式");

            var ht = SimVec2.FromGrid(plan.Targets[heavy.Id]);
            var wt = SimVec2.FromGrid(plan.Targets[warrior.Id]);
            var at = SimVec2.FromGrid(plan.Targets[archer.Id]);
            Assert.Greater(ht.X, wt.X, "磐石(重装)应最靠 +x(前排)");
            Assert.Less(at.X, wt.X, "坠日(射手)应最靠 -x(后排)");
            Assert.AreEqual(RowSpacingX(ht, wt), 6f, 1.01f, "前-中排 X 间隔 = RowSpacing");
            Assert.AreEqual(RowSpacingX(wt, at), 6f, 1.01f, "中-后排 X 间隔 = RowSpacing");
            Assert.AreEqual(ht.Y, wt.Y, 0.01f, "三者 Y 同线");
            Assert.AreEqual(wt.Y, at.Y, 0.01f);
            Assert.AreEqual(1f, plan.Facings[heavy.Id].X, 0.001f, "朝向 +x");
            Assert.AreEqual(0f, plan.Facings[heavy.Id].Y, 0.001f);
            Assert.AreEqual(plan.Facings[heavy.Id], plan.Facings[warrior.Id]);
            Assert.AreEqual(plan.Facings[warrior.Id], plan.Facings[archer.Id]);
        }

        private static float RowSpacingX(SimVec2 a, SimVec2 b) { return System.Math.Abs(a.X - b.X); }

        [Test]
        public void T2_FacingEnemy()
        {
            var sim = NewSim();
            var heavy = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            var warrior = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.DuanFeng, new GridPos(15, 108));
            var archer = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhuiRi, new GridPos(15, 116));
            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 124)); // 距坠日 8 ≤9 可见

            var plan = RegimentFormationSystem.PlanFormation(sim, new List<int> { heavy.Id, warrior.Id, archer.Id });
            Assert.IsFalse(plan.ScreenMode, "单向受敌不屏风");
            var fac = plan.Facings[heavy.Id];
            Assert.Greater(fac.X, 0.9f, "朝向应近似 +x(敌在东)");
            Assert.Less(System.Math.Abs(fac.Y), 0.1f);

            var ht = SimVec2.FromGrid(plan.Targets[heavy.Id]);
            var wt = SimVec2.FromGrid(plan.Targets[warrior.Id]);
            var at = SimVec2.FromGrid(plan.Targets[archer.Id]);
            Assert.Greater(ht.X, wt.X + 3f, "Heavy 应排在 +fac 侧(前排)");
            Assert.Less(at.X, wt.X - 3f, "Archer 应排在 -fac 侧(后排)");
        }

        [Test]
        public void T3_ScreenMode()
        {
            var sim = NewSim();
            var heavy = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(10, 100)); // 行距 5,阵型(纵 4 横 5)互不压格
            var warrior = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.DuanFeng, new GridPos(15, 100));
            var archer = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhuiRi, new GridPos(20, 100));

            var a = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 92));
            a.Hero = HeroId.PanShi; a.Hp = a.MaxHp = 5000f; a.Attack = 0f;
            var b = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 108));
            b.Hero = HeroId.PanShi; b.Hp = b.MaxHp = 5000f; b.Attack = 0f;
            // 断峰(15,100) 东西敌各距 8 ≤ 视野 10;坠日(20,100) 距两敌 8 ≤ 9

            var plan = RegimentFormationSystem.PlanFormation(sim, new List<int> { heavy.Id, warrior.Id, archer.Id });
            Assert.IsTrue(plan.ScreenMode, "方位差 180°>120° 应屏风");

            var c = (heavy.PosF + warrior.PosF + archer.PosF) * (1f / 3f);
            var at = SimVec2.FromGrid(plan.Targets[archer.Id]);
            Assert.Less(SimVec2.Distance(at, c), 1.5f, "射手应居质心");

            var ht = SimVec2.FromGrid(plan.Targets[heavy.Id]);
            var wt = SimVec2.FromGrid(plan.Targets[warrior.Id]);
            bool east = System.Math.Abs(ht.X - (c.X + 4f)) < 1.5f || System.Math.Abs(wt.X - (c.X + 4f)) < 1.5f;
            bool west = System.Math.Abs(ht.X - (c.X - 4f)) < 1.5f || System.Math.Abs(wt.X - (c.X - 4f)) < 1.5f;
            Assert.IsTrue(east, "应有一近战在东簇方向 ScreenRadius 处");
            Assert.IsTrue(west, "应有一近战在西簇方向 ScreenRadius 处");
            Assert.AreEqual(6f, System.Math.Abs(ht.X - wt.X), 2f, "两近战分居东西两簇");
        }

        [Test]
        public void T4_FacingOf_RetreatKeepsEnemyDir()
        {
            var sim = NewSim();
            var blue = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            blue.FacingOverride = null;
            sim.SetMoveTarget(blue.Id, new GridPos(15, 90)); // 后撤(-x)
            Assert.AreEqual(-1.2f, RegimentSystem.SlotOffset(blue, 0).X, 0.001f,
                "蓝后撤不转身:士兵仍在主将 -x 侧(面朝 +x 的背后,3列纵队首排)");

            var red = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 180));
            red.FacingOverride = null;
            sim.SetMoveTarget(red.Id, new GridPos(15, 190)); // 红后撤(+x)
            Assert.AreEqual(1.2f, RegimentSystem.SlotOffset(red, 0).X, 0.001f,
                "红后撤不转身:士兵在主将 +x 侧(面朝 -x 的背后,3列纵队首排)");
        }

        [Test]
        public void T5_Apply()
        {
            var sim = NewSim();
            var heavy = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(9, 100));  // 三行分离,行军路径互不交叉(避免对向穿廊死锁)
            var warrior = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.DuanFeng, new GridPos(21, 108));
            var archer = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhuiRi, new GridPos(15, 116));
            var ids = new List<int> { heavy.Id, warrior.Id, archer.Id };

            RegimentFormationSystem.Apply(sim, ids);
            foreach (var id in ids)
            {
                var g = sim.GetEntity(id);
                Assert.IsTrue(g.FacingOverride.HasValue, "Apply 应设 FacingOverride");
                Assert.IsTrue(g.HasMoveTarget, "Apply 应下发移动目标");
                Assert.AreEqual(0, g.AutoKind, "手动级应清 AutoKind");
            }

            bool done = RunUntil(sim, () =>
                SimVec2.Distance(heavy.PosF, SimVec2.FromGrid(heavy.MoveTarget)) < 1.5f &&
                SimVec2.Distance(warrior.PosF, SimVec2.FromGrid(warrior.MoveTarget)) < 1.5f &&
                SimVec2.Distance(archer.PosF, SimVec2.FromGrid(archer.MoveTarget)) < 1.5f, 3000);
            Assert.IsTrue(done, "三将应各自抵达布阵目标(<1.5m)");
        }

        [Test]
        public void T6_ClassOf()
        {
            Assert.AreEqual(HeroClass.Heavy, RegimentFormationSystem.ClassOf(HeroId.PanShi));
            Assert.AreEqual(HeroClass.Heavy, RegimentFormationSystem.ClassOf(HeroId.ZhenYue));
            Assert.AreEqual(HeroClass.Warrior, RegimentFormationSystem.ClassOf(HeroId.DuanFeng));
            Assert.AreEqual(HeroClass.Warrior, RegimentFormationSystem.ClassOf(HeroId.ZhuFeng));
            Assert.AreEqual(HeroClass.Archer, RegimentFormationSystem.ClassOf(HeroId.ZhuiRi));
            Assert.AreEqual(HeroClass.Archer, RegimentFormationSystem.ClassOf(HeroId.XingLuo));
        }
    }
}
