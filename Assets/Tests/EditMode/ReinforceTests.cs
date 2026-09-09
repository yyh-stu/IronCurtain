using System.Collections.Generic;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class ReinforceTests
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

        private static List<Entity> SoldiersOf(SimCore sim, int generalId)
        {
            var list = new List<Entity>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Unit && e.IsSoldier && e.GeneralId == generalId && e.State != EntityState.Dead)
                    list.Add(e);
            return list;
        }

        [Test]
        public void T1_PoolAccumulatesAndCaps()
        {
            var sim = NewSim();
            for (int i = 0; i < 40; i++) sim.Step(); // 4s
            Assert.AreEqual(2, ReinforceSystem.PoolOf(sim, Side.Blue), "2s/兵,4s 应积累 2");

            sim.ReinforcePool[Side.Blue] = 59;
            sim.ReinforceAccum[Side.Blue] = 0f;
            for (int i = 0; i < 40; i++) sim.Step();
            Assert.AreEqual(60, ReinforceSystem.PoolOf(sim, Side.Blue), "池应封顶 60");
        }

        [Test]
        public void T2_SJF_MostDepletedFirst()
        {
            var sim = NewSim();
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            var b = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhenYue, new GridPos(15, 140));
            for (int i = 0; i < 40; i++) sim.Step(); // 脱战计时越过 3s

            var aSoldiers = SoldiersOf(sim, a.Id);
            Assert.AreEqual(12, aSoldiers.Count);
            for (int i = 0; i < 7; i++) sim.RemoveEntity(aSoldiers[i].Id); // A 团剩 5(12 兵制)
            Assert.AreEqual(5, SoldiersOf(sim, a.Id).Count);

            sim.ReinforcePool[Side.Blue] = 30;
            sim.ReinforceAccum[Side.Blue] = 0f;
            string error;
            bool ok = ReinforceSystem.RequestReinforce(sim, Side.Blue, out error);
            Assert.IsTrue(ok, "应补兵成功: " + error);
            Assert.IsNull(error);

            var after = SoldiersOf(sim, a.Id);
            Assert.AreEqual(12, after.Count, "A 团应补满 12");
            Assert.AreEqual(12, SoldiersOf(sim, b.Id).Count, "B 团满编不补");
            Assert.AreEqual(23, ReinforceSystem.PoolOf(sim, Side.Blue), "池剩 30-7=23(12 兵制:补 7)");

            var slots = new HashSet<int>();
            foreach (var s in after)
            {
                Assert.AreEqual(a.Id, s.GeneralId);
                Assert.AreEqual(a.Hero, s.Hero, "变装=主将 Hero");
                Assert.IsTrue(slots.Add(s.FormationSlot), "FormationSlot 不重复");
            }
            Assert.AreEqual(12, slots.Count, "槽号 0..11 各一次");
        }

        [Test]
        public void T3_CombatExcluded()
        {
            var sim = NewSim();
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            for (int i = 0; i < 40; i++) sim.Step();

            var soldiers = SoldiersOf(sim, a.Id);
            for (int i = 0; i < 7; i++) sim.RemoveEntity(soldiers[i].Id); // 12 兵制杀 7 剩 5
            a.SecondsSinceCombat = 0.5f; // 交战中
            foreach (var s in SoldiersOf(sim, a.Id)) s.SecondsSinceCombat = 0.5f;

            sim.ReinforcePool[Side.Blue] = 10;
            sim.ReinforceAccum[Side.Blue] = 0f;
            string error;
            bool ok = ReinforceSystem.RequestReinforce(sim, Side.Blue, out error);
            Assert.IsFalse(ok, "交战兵团不可补兵");
            Assert.IsNotNull(error);
            StringAssert.Contains("交战", error);
            Assert.AreEqual(5, SoldiersOf(sim, a.Id).Count, "A 团士兵数不变");
            Assert.AreEqual(10, ReinforceSystem.PoolOf(sim, Side.Blue), "池不消耗");
        }

        [Test]
        public void T4_OutOfCombatRejoins()
        {
            var sim = NewSim();
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            for (int i = 0; i < 40; i++) sim.Step();

            var soldiers = SoldiersOf(sim, a.Id);
            for (int i = 0; i < 7; i++) sim.RemoveEntity(soldiers[i].Id); // 12 兵制杀 7 剩 5
            a.SecondsSinceCombat = 4f; // 已脱战
            foreach (var s in SoldiersOf(sim, a.Id)) s.SecondsSinceCombat = 4f;

            sim.ReinforcePool[Side.Blue] = 10;
            sim.ReinforceAccum[Side.Blue] = 0f;
            string error;
            bool ok = ReinforceSystem.RequestReinforce(sim, Side.Blue, out error);
            Assert.IsTrue(ok, "脱战 3s 后应可补兵: " + error);
            Assert.AreEqual(12, SoldiersOf(sim, a.Id).Count, "补满 12 编制");
            Assert.AreEqual(3, ReinforceSystem.PoolOf(sim, Side.Blue), "池剩 10-7=3(12 兵制)");
        }

        [Test]
        public void T5_VariantUniform()
        {
            var sim = NewSim();
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            for (int i = 0; i < 40; i++) sim.Step();

            var soldiers = SoldiersOf(sim, a.Id);
            for (int i = 0; i < 7; i++) sim.RemoveEntity(soldiers[i].Id); // 12 兵制杀 7 剩 5
            a.SecondsSinceCombat = 4f;
            foreach (var s in SoldiersOf(sim, a.Id)) s.SecondsSinceCombat = 4f;

            sim.ReinforcePool[Side.Blue] = 20;
            sim.ReinforceAccum[Side.Blue] = 0f;
            string error;
            Assert.IsTrue(ReinforceSystem.RequestReinforce(sim, Side.Blue, out error));

            var after = SoldiersOf(sim, a.Id);
            Assert.AreEqual(12, after.Count);
            Entity fresh = null;
            foreach (var s in after)
                if (s.FormationSlot == 0) fresh = s;
            Assert.IsNotNull(fresh, "槽 0 为新补士兵");
            Assert.AreEqual(a.MaxHp / 10f, fresh.MaxHp, 1e-3f, "MaxHp=主将/10");
            Assert.AreEqual(a.Attack / 10f, fresh.Attack, 1e-3f, "Attack=主将/10");
            Assert.AreEqual(a.AttackIntervalSec, fresh.AttackIntervalSec, 1e-4f);
            Assert.AreEqual(a.MoveSpeed, fresh.MoveSpeed, 1e-4f);
            Assert.AreEqual(a.Range, fresh.Range);
            Assert.IsTrue(fresh.IsSoldier);
            Assert.AreEqual(0, fresh.TempGarrisonId);
            Assert.AreEqual(a.Hero, fresh.Hero, "变装=主将 Hero");
        }

        [Test]
        public void T6_NoGeneralError()
        {
            var sim = NewSim(); // 空场,无主将
            sim.ReinforcePool[Side.Blue] = 10;
            sim.ReinforceAccum[Side.Blue] = 0f;
            string error;
            bool ok = ReinforceSystem.RequestReinforce(sim, Side.Blue, out error);
            Assert.IsFalse(ok);
            StringAssert.Contains("无主将", error);
        }

        [Test]
        public void T7_MarchOriginNearestBarracks()
        {
            var sim = NewSim();
            var barracks = sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(15, 105));
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 100));
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 110));
            for (int i = 0; i < 40; i++) sim.Step();
            var soldiers = SoldiersOf(sim, a.Id);
            for (int i = 0; i < 7; i++) sim.RemoveEntity(soldiers[i].Id); // 12 兵制杀 7 剩 5
            a.SecondsSinceCombat = 4f; foreach (var s in SoldiersOf(sim, a.Id)) s.SecondsSinceCombat = 4f;
            sim.ReinforcePool[Side.Blue] = 10; sim.ReinforceAccum[Side.Blue] = 0f;
            string error;
            Assert.IsTrue(ReinforceSystem.RequestReinforce(sim, Side.Blue, out error), error);
            bool any = false;
            foreach (var s in SoldiersOf(sim, a.Id))
                if (s.FormationSlot >= 0 && s.MarchOrigin != null) { any = true; break; }
            Assert.IsTrue(any, "新兵应有行军源");
            // 断言:所有带 MarchOrigin 的新兵,源=兵营中心(距主将最近的兵营,非基地)
            foreach (var s in SoldiersOf(sim, a.Id))
                if (s.MarchOrigin != null)
                {
                    Assert.AreEqual(barracks.Pos.Col, s.MarchOrigin.Value.X, 1e-4f);
                    Assert.AreEqual(barracks.Pos.Row, s.MarchOrigin.Value.Y, 1e-4f);
                }
        }

        [Test]
        public void T8_MarchOriginFallsBackToBase()
        {
            var sim = NewSim();
            var baseB = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 100));
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 110));
            for (int i = 0; i < 40; i++) sim.Step();
            var soldiers = SoldiersOf(sim, a.Id);
            for (int i = 0; i < 7; i++) sim.RemoveEntity(soldiers[i].Id); // 12 兵制杀 7 剩 5
            a.SecondsSinceCombat = 4f; foreach (var s in SoldiersOf(sim, a.Id)) s.SecondsSinceCombat = 4f;
            sim.ReinforcePool[Side.Blue] = 10; sim.ReinforceAccum[Side.Blue] = 0f;
            string error;
            Assert.IsTrue(ReinforceSystem.RequestReinforce(sim, Side.Blue, out error), error);
            bool any = false;
            foreach (var s in SoldiersOf(sim, a.Id))
                if (s.MarchOrigin != null)
                {
                    any = true;
                    Assert.AreEqual(baseB.Pos.Col, s.MarchOrigin.Value.X, 1e-4f, "无兵营应回退到基地中心");
                    Assert.AreEqual(baseB.Pos.Row, s.MarchOrigin.Value.Y, 1e-4f, "无兵营应回退到基地中心");
                }
            Assert.IsTrue(any, "新兵应有行军源(回退基地)");
        }

        [Test]
        public void T9_MarchOriginNullWithoutBuildings()
        {
            var sim = NewSim(); // 空场(现状 T2 场景),无任何建筑
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            var b = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhenYue, new GridPos(15, 140));
            for (int i = 0; i < 40; i++) sim.Step();
            var aSoldiers = SoldiersOf(sim, a.Id);
            for (int i = 0; i < 7; i++) sim.RemoveEntity(aSoldiers[i].Id); // 12 兵制
            a.SecondsSinceCombat = 4f; foreach (var s in SoldiersOf(sim, a.Id)) s.SecondsSinceCombat = 4f;
            sim.ReinforcePool[Side.Blue] = 10; sim.ReinforceAccum[Side.Blue] = 0f;
            string error;
            Assert.IsTrue(ReinforceSystem.RequestReinforce(sim, Side.Blue, out error), error);
            Assert.AreEqual(12, SoldiersOf(sim, a.Id).Count, "补满 12 编制");
            foreach (var s in SoldiersOf(sim, a.Id))
                Assert.IsNull(s.MarchOrigin, "无建筑时新兵应即时落位(MarchOrigin=null)");
        }
    }
}
