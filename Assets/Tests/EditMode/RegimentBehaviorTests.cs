using System.Collections.Generic;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class RegimentBehaviorTests
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

        private static SimCore NewAutoSim()
        {
            var sim = NewSim();
            sim.Config.AutoBehavior = true;
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

        private static List<Entity> SoldiersOf(SimCore sim, int generalId)
        {
            var list = new List<Entity>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Unit && e.IsSoldier && e.GeneralId == generalId) list.Add(e);
            return list;
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
        public void T1_EngageOnVision()
        {
            var sim = NewAutoSim();
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.DuanFeng, new GridPos(15, 100));
            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 108)); // 距 8,断峰主将视野 10 内

            bool ok = RunUntil(sim, () => g.AutoKind == 1, 100);
            Assert.IsTrue(ok, "视野内出现敌人应触发自动进攻");
            Assert.IsTrue(g.HasMoveTarget, "自动进攻应下发移动目标");
        }

        [Test]
        public void T2_SupportWithin15()
        {
            var sim = NewAutoSim();
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 102)); // C:与 A 交战,距 B 8 > B 视野 6
            var b = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 114)); // B 距 A 14 ≤ 15;C(102) 距 B 12 > 视野 6

            bool ok = RunUntil(sim, () => b.AutoKind == 2 && b.AutoTargetId == a.Id, 300);
            Assert.IsTrue(ok, "15 格内友军交战应触发自动支援(A 交战中)");
        }

        [Test]
        public void T3_EngageBeatsSupport()
        {
            var sim = NewAutoSim();
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 102)); // A 的交战对象
            var b = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 114)); // B 距 A 10

            var e = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 119)); // 距 B 5 ≤ 视野 6,B 可见
            e.Hero = HeroId.PanShi; e.Hp = e.MaxHp = 5000f; e.Attack = 0f;

            for (int i = 0; i < 20; i++) sim.Step();
            Assert.AreEqual(1, b.AutoKind, "索敌进攻优先于支援");
        }

        [Test]
        public void T4_PlayerInterrupt_NoPullback()
        {
            var sim = NewAutoSim();
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 102));
            var b = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 114));

            Assert.IsTrue(RunUntil(sim, () => b.AutoKind == 2 && b.AutoTargetId == a.Id, 300), "前置:B 进入支援");

            // 玩家手动打断(反方向)
            sim.SetMoveTarget(b.Id, new GridPos(15, 125), true);
            Assert.AreEqual(0, b.AutoKind, "手动命令应清 AutoKind");
            Assert.AreEqual(2, b.SupKind, "手动命令应写 SupKind=2");
            Assert.AreEqual(a.Id, b.SupTargetId);

            float startDist = SimVec2.Distance(b.PosF, a.PosF);
            bool pulled = false;
            for (int i = 0; i < 100; i++)
            {
                sim.Step();
                if (b.AutoKind == 2 && b.AutoTargetId == a.Id) pulled = true;
            }
            Assert.IsFalse(pulled, "同事件支援复读应被禁");
            Assert.Greater(SimVec2.Distance(b.PosF, a.PosF), startDist, "手动目标期间应远离 A");

            // 新事件:敌 E 进入 B 行军路径视野 → 允许索敌进攻
            var e2 = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 128));
            e2.Hero = HeroId.PanShi; e2.Hp = e2.MaxHp = 5000f; e2.Attack = 0f;
            bool engaged = RunUntil(sim, () => b.AutoKind == 1, 200);
            Assert.IsTrue(engaged, "新事件(视野内新敌)可再次触发自动进攻");
        }

        [Test]
        public void T5_FlagOffNoAuto()
        {
            var sim = NewSim(); // 默认 Config,不开 AutoBehavior
            var a = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 102));
            var b = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 114));

            for (int i = 0; i < 200; i++) sim.Step();
            Assert.AreEqual(0, b.AutoKind, "开关关闭时不应有自动决策");
            Assert.IsFalse(b.HasMoveTarget, "开关关闭时不应有自动移动");
        }

        [Test]
        public void T6_AiAutoReinforce()
        {
            var sim = NewAutoSim();
            AiSystem.Reset();
            VisionSystem.Reset();

            var home = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 30));
            home.Prod = new ProductionSet();
            sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 250));
            AiSystem.Configure(sim, Side.Blue, AiTier.Normal);

            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            var soldiers = SoldiersOf(sim, g.Id);
            Assert.AreEqual(12, soldiers.Count);
            for (int i = 0; i < 7; i++) sim.RemoveEntity(soldiers[i].Id); // 杀 7 → 5/12(12 兵制)
            sim.ReinforcePool[Side.Blue] = 10;

            int Count() => SoldiersOf(sim, g.Id).Count;
            bool done = RunUntil(sim, () => Count() > 5 || sim.ReinforcePool[Side.Blue] < 10, 600);
            Assert.IsTrue(done, "AI 应在池≥10 时自动补兵(≤600 tick)");
        }
    }
}
