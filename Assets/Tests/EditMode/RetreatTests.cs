using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using IronCurtain.Sim;

namespace IronCurtain.Tests
{
    [TestFixture]
    public class RetreatTests
    {
        private static GameConfig _cfg; // 静态缓存 gameconfig(参考 RegimentBehaviorTests)
        private SimCore sim;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null) _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        }

        [TearDown]
        public void TearDown()
        {
            AiSystem.Reset();
            VisionSystem.Reset();
            HexSystem.Reset();
            SiegeSystem.Reset();
            sim = null;
        }

        private SimCore NewSim()
        {
            return new SimCore(new SimConfig(), new SeededRng(1)) { Game = _cfg };
        }

        private SimCore NewAutoSim()
        {
            var s = NewSim();
            s.Config.AutoBehavior = true;
            return s;
        }

        private static void StripSoldiers(SimCore s, Entity g)
        {
            foreach (var e in new List<Entity>(s.Entities.Values))
                if (e.IsSoldier && e.GeneralId == g.Id) s.RemoveEntity(e.Id);
        }

        private static List<Entity> SoldiersOf(SimCore s, Entity g)
        {
            var list = new List<Entity>();
            foreach (var e in s.Entities.Values)
                if (e.IsSoldier && e.GeneralId == g.Id) list.Add(e);
            return list;
        }

        [Test]
        public void T1_基本撤退()
        {
            sim = NewSim();
            var b = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 116));
            Assert.IsTrue(RetreatSystem.OrderRetreat(sim, g.Id));
            Assert.AreEqual(EntityState.Routing, g.State);
            Assert.IsTrue(g.HasMoveTarget);
            float half = Math.Max(BuildingFootprint.HalfW(b), BuildingFootprint.HalfH(b));
            float d = SimVec2.Distance(SimVec2.FromGrid(g.MoveTarget), b.PosF);
            Assert.Less(d, half + RetreatSystem.StandoffOffset + 1.0f); // ToGrid 取整余量
            Assert.IsTrue(g.RetreatHold);
            foreach (var s in SoldiersOf(sim, g))
                Assert.AreEqual(EntityState.Routing, s.State);
        }

        [Test]
        public void T2_撤退减益_输出降低25()
        {
            sim = NewSim();
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhuFeng, new GridPos(15, 116));
            var c = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhuFeng, new GridPos(15, 150));
            var ta = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.ZhuFeng, new GridPos(15, 117));
            var tc = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.ZhuFeng, new GridPos(15, 151));
            StripSoldiers(sim, a); StripSoldiers(sim, c); StripSoldiers(sim, ta); StripSoldiers(sim, tc);
            a.Hp = a.MaxHp = 99999f; c.Hp = c.MaxHp = 99999f;
            ta.Hp = ta.MaxHp = 99999f; tc.Hp = tc.MaxHp = 99999f;
            Assert.IsTrue(RetreatSystem.OrderRetreat(sim, a.Id));
            a.HasMoveTarget = false; // 冻结位置便于测量(减益乘区只读 State)
            float da = 0f, dc = 0f;
            sim.AddEventListener(e =>
            {
                if (e.Type != SimEventType.DamageDealt) return;
                if (e.SubjectId == ta.Id) da += e.Amount;
                else if (e.SubjectId == tc.Id) dc += e.Amount;
            });
            Assert.IsTrue(TestUtil.RunUntil(sim, () => da > 10f && dc > 10f, 6000));
            Assert.AreEqual(0.75f, da / dc, 0.02f);
        }

        private float MeasureTakenDamage(bool routing)
        {
            var s = NewSim();
            s.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
            var def = RegimentSystem.SpawnGeneral(s, Side.Blue, HeroId.ZhuFeng, new GridPos(15, 116));
            var atk = RegimentSystem.SpawnGeneral(s, Side.Red, HeroId.ZhuFeng, new GridPos(15, 117));
            StripSoldiers(s, def); StripSoldiers(s, atk);
            def.Hp = def.MaxHp = 99999f; atk.Hp = atk.MaxHp = 99999f;
            if (routing)
            {
                Assert.IsTrue(RetreatSystem.OrderRetreat(s, def.Id));
                def.HasMoveTarget = false; // 冻结位置,只考察承伤乘区
            }
            float dmg = 0f;
            s.AddEventListener(e => { if (e.Type == SimEventType.DamageDealt && e.SubjectId == def.Id) dmg += e.Amount; });
            Assert.IsTrue(TestUtil.RunUntil(s, () => dmg > 0f, 3000));
            return dmg;
        }

        [Test]
        public void T3_承伤提高25()
        {
            SetUp(); // 确保配置已加载
            float r = MeasureTakenDamage(true);
            float n = MeasureTakenDamage(false);
            Assert.AreEqual(1.25f, r / n, 0.02f);
        }

        [Test]
        public void T4_到达站定()
        {
            sim = NewSim();
            var b = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 116));
            Assert.IsTrue(RetreatSystem.OrderRetreat(sim, g.Id));
            Assert.IsTrue(TestUtil.RunUntil(sim, () => g.State == EntityState.Idle && !g.HasMoveTarget, 20000));
            float half = Math.Max(BuildingFootprint.HalfW(b), BuildingFootprint.HalfH(b));
            Assert.LessOrEqual(SimVec2.Distance(g.PosF, b.PosF), half + RetreatSystem.StandoffOffset + 1.5f);
        }

        [Test]
        public void T5_防拉回_手动命令解除()
        {
            sim = NewAutoSim();
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 116));
            var r = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 122));
            g.Hp = g.MaxHp = 99999f; r.Hp = r.MaxHp = 99999f;
            Assert.IsTrue(RetreatSystem.OrderRetreat(sim, g.Id));
            Assert.IsTrue(TestUtil.RunUntil(sim, () => g.State == EntityState.Idle && !g.HasMoveTarget, 20000));
            for (int i = 0; i < 100; i++) sim.Step(); // 到达后 100 tick,RetreatHold 压制
            Assert.AreEqual(0, g.AutoKind);
            Assert.IsFalse(g.HasMoveTarget);
            sim.SetMoveTarget(g.Id, new GridPos(15, 120), true); // 玩家再下令
            Assert.IsFalse(g.RetreatHold);
            Assert.IsTrue(TestUtil.RunUntil(sim, () => g.HasMoveTarget && g.AutoKind == 1, 20000)); // 自动层恢复接管
        }

        [Test]
        public void T6_撤退中不被自动层覆盖()
        {
            sim = NewAutoSim();
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 116));
            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 122));
            Assert.IsTrue(RetreatSystem.OrderRetreat(sim, g.Id));
            var orig = g.MoveTarget;
            for (int i = 0; i < 100; i++) sim.Step();
            Assert.AreEqual(orig, g.MoveTarget);
            Assert.AreEqual(0, g.AutoKind);
        }

        [Test]
        public void T7_士兵同步复位()
        {
            sim = NewSim();
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 116));
            Assert.IsTrue(RetreatSystem.OrderRetreat(sim, g.Id));
            Assert.IsTrue(TestUtil.RunUntil(sim, () => g.State == EntityState.Idle, 20000));
            foreach (var s in SoldiersOf(sim, g))
                Assert.AreEqual(EntityState.Idle, s.State); // RegimentSystem 同 Step 内复位
        }

        [Test]
        public void T8_边界()
        {
            sim = NewSim();
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 116));
            Assert.Throws<InvalidOperationException>(() => RetreatSystem.OrderRetreat(sim, 999999));
            Assert.IsFalse(RetreatSystem.OrderRetreat(sim, SoldiersOf(sim, g)[0].Id)); // 士兵 → false
            var g2 = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 132));
            g2.State = EntityState.Exchanging;
            Assert.IsFalse(RetreatSystem.OrderRetreat(sim, g2.Id)); // 交换中 → false
            var s2 = NewSim(); // ④ 场景内无任何己方建筑
            var g3 = RegimentSystem.SpawnGeneral(s2, Side.Blue, HeroId.PanShi, new GridPos(15, 116));
            Assert.IsFalse(RetreatSystem.OrderRetreat(s2, g3.Id));
            Assert.AreEqual(EntityState.Idle, g3.State); // 状态未变
        }

        [Test]
        public void T9_最近据点()
        {
            sim = NewSim();
            var bBase = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
            var bBar = sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(15, 140));
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 148));
            Assert.IsTrue(RetreatSystem.OrderRetreat(sim, g.Id));
            var t = SimVec2.FromGrid(g.MoveTarget);
            Assert.Less(SimVec2.Distance(t, bBar.PosF), SimVec2.Distance(t, bBase.PosF));
        }
    }
}
