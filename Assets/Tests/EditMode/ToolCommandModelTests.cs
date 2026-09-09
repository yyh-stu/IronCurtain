using System.Linq;
using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class ToolCommandModelTests
    {
        private static GameConfig Config() =>
            GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);

        private static SimCore NewSim(ulong seed = 41) =>
            new SimCore(new SimConfig(), new SeededRng(seed)) { Game = Config() };

        private static void GiveOneEp(SimCore sim, Side side)
        {
            sim.SpawnBuilding(side, BuildingId.ResourcePoint, new GridPos(0, 24));
            for (int i = 0; i < 905; i++) sim.Step();
        }

        [Test]
        public void Describe_返回成本与可负担状态()
        {
            var sim = NewSim();
            var model = new ToolCommandModel(sim, Side.Blue);
            var empty = model.Describe();
            Assert.IsTrue(empty.Valid);
            Assert.AreEqual(1, empty.RamCost);
            Assert.AreEqual(1, empty.FortCost);
            Assert.IsFalse(empty.CanAffordRam || empty.CanAffordFort);

            GiveOneEp(sim, Side.Blue);
            var funded = model.Describe();
            Assert.AreEqual(1f, funded.Ep, 0.001f);
            Assert.IsTrue(funded.CanAffordRam && funded.CanAffordFort);
        }

        [Test]
        public void UseRam_成功恰扣一次并造成配置伤害()
        {
            var sim = NewSim();
            var tower = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 120));
            tower.Hp = 50f;
            GiveOneEp(sim, Side.Blue);
            var model = new ToolCommandModel(sim, Side.Blue);

            var success = model.UseRam(tower.Id);
            Assert.IsTrue(success.Success, success.Message);
            Assert.AreEqual(0f, sim.EpOf(Side.Blue), 0.001f);
            Assert.AreEqual(50f - (float)sim.Game.Tools.RamDamage, tower.Hp, 0.001f);

            float hp = tower.Hp;
            var repeat = model.UseRam(tower.Id);
            Assert.AreEqual(ToolCommandError.InsufficientEp, repeat.Error);
            Assert.AreEqual(hp, tower.Hp, 0.001f);
        }

        [Test]
        public void UseRam_非法目标全部拒绝且零副作用()
        {
            var sim = NewSim();
            var own = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 100));
            var neutral = sim.SpawnBuilding(Side.Neutral, BuildingId.ResourcePoint, new GridPos(15, 110));
            var rubble = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 120));
            rubble.Rubbled = true;
            var dead = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 130));
            dead.State = EntityState.Dead;
            GiveOneEp(sim, Side.Blue);
            var model = new ToolCommandModel(sim, Side.Blue);

            foreach (int id in new[] { 999999, own.Id, neutral.Id, rubble.Id, dead.Id })
            {
                var result = model.UseRam(id);
                Assert.AreEqual(ToolCommandError.InvalidTarget, result.Error);
                Assert.AreEqual(1f, sim.EpOf(Side.Blue), 0.001f);
            }
            Assert.AreEqual(own.MaxHp, own.Hp, 0.001f);
            Assert.AreEqual(rubble.MaxHp, rubble.Hp, 0.001f);
        }

        [Test]
        public void UseFort_合法空格成功_占用与越界失败不扣费()
        {
            var sim = NewSim();
            GiveOneEp(sim, Side.Blue);
            var model = new ToolCommandModel(sim, Side.Blue);
            int before = sim.EntityCount;
            var cell = new GridPos(2, 100);

            var success = model.UseFort(cell);
            Assert.IsTrue(success.Success, success.Message);
            Assert.AreEqual(0f, sim.EpOf(Side.Blue), 0.001f);
            Assert.AreEqual(before + 1, sim.EntityCount);
            Assert.IsTrue(sim.Entities.Values.Any(e => e.IsFort && e.Side == Side.Blue && e.Pos.Equals(cell)));

            int count = sim.EntityCount;
            Assert.AreEqual(ToolCommandError.InvalidTarget, model.UseFort(cell).Error);
            Assert.AreEqual(ToolCommandError.InvalidTarget, model.UseFort(new GridPos(-1, 2)).Error);
            Assert.AreEqual(count, sim.EntityCount);
            Assert.AreEqual(0f, sim.EpOf(Side.Blue), 0.001f);
        }

        [Test]
        public void InvalidSide与MalformedState_安全拒绝且零副作用()
        {
            foreach (var side in new[] { Side.Neutral, (Side)99 })
            {
                var invalidSide = new ToolCommandModel(NewSim(), side);
                Assert.IsFalse(invalidSide.Describe().Valid);
                Assert.AreEqual(ToolCommandError.InvalidSide, invalidSide.UseRam(1).Error);
                Assert.AreEqual(ToolCommandError.InvalidSide,
                    invalidSide.UseFort(new GridPos(1, 1)).Error);
            }
            AssertInvalid(new ToolCommandModel(null, Side.Blue));
            AssertInvalid(new ToolCommandModel(
                new SimCore(null, new SeededRng(1)) { Game = Config() }, Side.Blue));
            var noGame = NewSim();
            noGame.Game = null;
            AssertInvalid(new ToolCommandModel(noGame, Side.Blue));
            var noEp = NewSim();
            noEp.Game.Ep = null;
            AssertInvalid(new ToolCommandModel(noEp, Side.Blue));
            var noTools = NewSim();
            noTools.Game.Tools = null;
            AssertInvalid(new ToolCommandModel(noTools, Side.Blue));
            var negativeCost = NewSim();
            negativeCost.Game.Ep.RamCost = -1;
            AssertInvalid(new ToolCommandModel(negativeCost, Side.Blue));
            var invalidTool = NewSim();
            invalidTool.Game.Tools.RamDamage = 0;
            AssertInvalid(new ToolCommandModel(invalidTool, Side.Blue));
        }

        [Test]
        public void UseFort_两局相同输入产生相同结果()
        {
            var a = NewSim(88);
            var b = NewSim(88);
            GiveOneEp(a, Side.Red);
            GiveOneEp(b, Side.Red);
            var cell = new GridPos(1, 180);
            Assert.IsTrue(new ToolCommandModel(a, Side.Red).UseFort(cell).Success);
            Assert.IsTrue(new ToolCommandModel(b, Side.Red).UseFort(cell).Success);
            var fa = a.Entities.Values.Single(e => e.IsFort);
            var fb = b.Entities.Values.Single(e => e.IsFort);
            Assert.AreEqual(fa.Id, fb.Id);
            Assert.AreEqual(fa.Hp, fb.Hp, 0.001f);
            Assert.AreEqual(a.EpOf(Side.Red), b.EpOf(Side.Red), 0.001f);
        }

        private static void AssertInvalid(ToolCommandModel model)
        {
            Assert.DoesNotThrow(() => model.Describe());
            Assert.IsFalse(model.Describe().Valid);
            Assert.AreEqual(ToolCommandError.InvalidState, model.UseRam(1).Error);
            Assert.AreEqual(ToolCommandError.InvalidState,
                model.UseFort(new GridPos(1, 1)).Error);
        }
    }
}
