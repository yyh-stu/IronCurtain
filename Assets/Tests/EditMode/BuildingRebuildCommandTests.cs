using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class BuildingRebuildCommandTests
    {
        private SimCore _sim;
        private BuildingCommandModel _model;
        private Entity _rubble;

        [SetUp]
        public void SetUp()
        {
            VisionSystem.Reset();
            var config = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            _sim = new SimCore(new SimConfig(), new SeededRng(61)) { Game = config };
            _sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 90));
            _rubble = _sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 110));
            _rubble.Rubbled = true;
            _rubble.Side = Side.Neutral;
            _rubble.Hp = 0f;
            _sim.Step();
            _model = new BuildingCommandModel(_sim, Side.Blue);
        }

        [TearDown]
        public void TearDown() => VisionSystem.Reset();

        private void GiveOneEp()
        {
            _sim.SpawnBuilding(Side.Blue, BuildingId.ResourcePoint, new GridPos(0, 24));
            for (int i = 0; i < 905; i++) _sim.Step();
        }

        [Test]
        public void Describe_可见正式建筑废墟无需选将即提供重建()
        {
            var info = _model.Describe(_rubble.Id, new int[0]);
            Assert.IsTrue(info.Exists && info.Fresh);
            Assert.IsTrue(info.CanRebuild);
            Assert.IsFalse(info.CanCapture || info.CanDestroy);
            Assert.AreEqual(_sim.Game.Ep.RebuildCost, info.RebuildCost);
            StringAssert.Contains("废墟", info.Status);
        }

        [Test]
        public void Execute_EP不足拒绝且废墟零副作用()
        {
            var result = _model.Execute(_rubble.Id, null, BuildingCommandAction.Rebuild);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SiegeOrderError.IntentRejected, result.Error);
            StringAssert.Contains("EP 不足", result.Message);
            Assert.IsTrue(_rubble.Rubbled);
            Assert.AreEqual(Side.Neutral, _rubble.Side);
            Assert.AreEqual(0f, _rubble.Hp, 0.001f);
            Assert.AreEqual(0f, _sim.EpOf(Side.Blue), 0.001f);
        }

        [Test]
        public void Execute_一EP成功重建为己方满血且恰扣一次()
        {
            GiveOneEp();
            var result = _model.Execute(_rubble.Id, new int[0], BuildingCommandAction.Rebuild);
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(0, result.SoldierCount);
            Assert.IsFalse(_rubble.Rubbled);
            Assert.AreEqual(Side.Blue, _rubble.Side);
            Assert.AreEqual(_rubble.MaxHp, _rubble.Hp, 0.001f);
            Assert.AreEqual(0f, _sim.EpOf(Side.Blue), 0.001f);

            var repeat = _model.Execute(_rubble.Id, null, BuildingCommandAction.Rebuild);
            Assert.IsFalse(repeat.Success);
            Assert.AreEqual(0f, _sim.EpOf(Side.Blue), 0.001f);
        }

        [Test]
        public void 非废墟_迷雾废墟_临时阵地废墟均拒绝且不抛异常()
        {
            var intact = _sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(15, 100));
            var stale = _sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 250));
            stale.Rubbled = true; stale.Side = Side.Neutral; stale.Hp = 0f;
            var fort = _sim.SpawnEntity(Side.Neutral, EntityKind.Building, new GridPos(15, 105));
            fort.IsFort = true; fort.Rubbled = true; fort.Hp = 0f;

            foreach (var target in new[] { intact, stale, fort })
            {
                BuildingCommandExecution result = default;
                Assert.DoesNotThrow(() => result = _model.Execute(
                    target.Id, null, BuildingCommandAction.Rebuild));
                Assert.IsFalse(result.Success);
                Assert.IsTrue(target == intact ? !target.Rubbled : target.Rubbled);
            }
        }

        [Test]
        public void 非法动作安全拒绝且不按摧毁解释()
        {
            var result = _model.Execute(_rubble.Id, null, (BuildingCommandAction)99);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SiegeOrderError.InvalidIntent, result.Error);
            Assert.IsTrue(_rubble.Rubbled);
            Assert.AreEqual(0f, _sim.EpOf(Side.Blue), 0.001f);
        }
    }
}
