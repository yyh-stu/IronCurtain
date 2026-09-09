using System.Linq;
using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class UpgradeCommandModelTests
    {
        private GameConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        }

        private SimCore NewSim()
        {
            return new SimCore(new SimConfig(), new SeededRng(91)) { Game = _config };
        }

        private static void GiveEp(SimCore sim, Side side, int amount)
        {
            for (int i = 0; i < amount; i++)
                sim.SpawnBuilding(side, BuildingId.ResourcePoint, new GridPos(14 + i, 128));
            for (int i = 0; i < 905; i++) sim.Step();
        }

        [Test]
        public void Describe_六英雄稳定排序并反映可负担状态()
        {
            var sim = NewSim();
            GiveEp(sim, Side.Blue, 2);
            var state = new UpgradeCommandModel(sim, Side.Blue).Describe();

            Assert.AreEqual(6, state.Entries.Count);
            CollectionAssert.AreEqual(
                state.Entries.Select(e => e.Hero).OrderBy(h => (int)h),
                state.Entries.Select(e => e.Hero));
            Assert.IsTrue(state.Entries.All(e => !e.Upgraded && e.Affordable && e.Cost == 2));
        }

        [Test]
        public void Buy_成功扣费一次_重复购买零副作用()
        {
            var sim = NewSim();
            GiveEp(sim, Side.Blue, 2);
            var model = new UpgradeCommandModel(sim, Side.Blue);

            var first = model.Buy(HeroId.PanShi);
            Assert.IsTrue(first.Success, first.Message);
            Assert.AreEqual(0f, sim.EpOf(Side.Blue), 0.001f);
            Assert.IsTrue(sim.IsUpgraded(Side.Blue, HeroId.PanShi));

            var second = model.Buy(HeroId.PanShi);
            Assert.IsFalse(second.Success);
            Assert.AreEqual(UpgradeCommandError.AlreadyUpgraded, second.Error);
            Assert.AreEqual(0f, sim.EpOf(Side.Blue), 0.001f);
        }

        [Test]
        public void Buy_EP不足与无效英雄均拒绝且不扣费()
        {
            var sim = NewSim();
            var model = new UpgradeCommandModel(sim, Side.Blue);

            var poor = model.Buy(HeroId.ZhenYue);
            Assert.AreEqual(UpgradeCommandError.InsufficientEp, poor.Error);
            Assert.IsFalse(sim.IsUpgraded(Side.Blue, HeroId.ZhenYue));

            var invalid = model.Buy((HeroId)99);
            Assert.AreEqual(UpgradeCommandError.InvalidHero, invalid.Error);
            Assert.AreEqual(0f, sim.EpOf(Side.Blue), 0.001f);
        }

        [Test]
        public void InvalidState_返回空状态与错误而非异常()
        {
            var model = new UpgradeCommandModel(null, Side.Blue);
            Assert.AreEqual(0, model.Describe().Entries.Count);
            Assert.AreEqual(UpgradeCommandError.InvalidState, model.Buy(HeroId.PanShi).Error);
        }

        [Test]
        public void InvalidSide_Neutral与未定义值安全拒绝()
        {
            var sim = NewSim();
            foreach (var side in new[] { Side.Neutral, (Side)99 })
            {
                var model = new UpgradeCommandModel(sim, side);
                Assert.AreEqual(0, model.Describe().Entries.Count);
                Assert.AreEqual(UpgradeCommandError.InvalidState, model.Buy(HeroId.PanShi).Error);
            }
            Assert.AreEqual(0f, sim.EpOf(Side.Blue), 0.001f);
            Assert.AreEqual(0f, sim.EpOf(Side.Red), 0.001f);
        }

        [Test]
        public void MalformedConfig_全部安全拒绝且不触发购买()
        {
            AssertMalformed(c => c.Ep = null);
            AssertMalformed(c => c.Ep.UpgradeCost = -2);
            AssertMalformed(c => c.Upgrades = null);
            AssertMalformed(c => c.Upgrades = c.Upgrades.Take(5).ToArray());
            AssertMalformed(c => c.Upgrades[0] = null);
            AssertMalformed(c => c.Upgrades[0].Cost = -2);
            AssertMalformed(c => c.Upgrades[0].Hero = c.Upgrades[1].Hero);
            AssertMalformed(c => c.Upgrades[0].Hero = (HeroId)99);
        }

        private static void AssertMalformed(System.Action<GameConfig> mutate)
        {
            var config = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig(), new SeededRng(77)) { Game = config };
            mutate(config);
            var model = new UpgradeCommandModel(sim, Side.Blue);
            Assert.DoesNotThrow(() => model.Describe());
            Assert.AreEqual(0, model.Describe().Entries.Count);
            Assert.AreEqual(UpgradeCommandError.InvalidState, model.Buy(HeroId.PanShi).Error);
            Assert.AreEqual(0f, sim.EpOf(Side.Blue), 0.001f);
            Assert.IsFalse(sim.IsUpgraded(Side.Blue, HeroId.PanShi));
        }
    }
}
