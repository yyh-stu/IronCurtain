using System.Collections.Generic;
using System.Linq;
using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class HexCommandModelTests
    {
        private GameConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        }

        private SimCore NewSim(ulong seed)
        {
            var sim = new SimCore(new SimConfig(), new SeededRng(seed)) { Game = _config };
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(1, 0));
            sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(1, 22));
            return sim;
        }

        private static void GiveEpAndReachFirstNode(SimCore sim, Side side, int ep)
        {
            for (int i = 0; i < ep; i++)
                sim.SpawnBuilding(side, BuildingId.ResourcePoint, new GridPos(i % 3, 24 + i / 3));
            for (int i = 0; i < 1800; i++) sim.Step();
        }

        [Test]
        public void DrawChooseUse_完整原子链路与状态反馈()
        {
            var sim = NewSim(12345);
            GiveEpAndReachFirstNode(sim, Side.Blue, 2);
            var model = new HexCommandModel(sim, Side.Blue);
            Assert.IsTrue(model.Describe().Valid);
            float before = sim.EpOf(Side.Blue);

            var draw = model.Draw();
            Assert.IsTrue(draw.Success, draw.Message);
            Assert.AreEqual(3, draw.Choices.Count);
            Assert.AreEqual(before - sim.Game.Ep.HexDrawCost, sim.EpOf(Side.Blue), 0.001f);
            CollectionAssert.AreEqual(draw.Choices, model.Describe().PendingChoices);

            var card = draw.Choices.First(c => c != HexId.Sandbag);
            var choose = model.Choose(card);
            Assert.IsTrue(choose.Success, choose.Message);
            CollectionAssert.Contains(model.Describe().Hand, card);

            var use = model.Use(card, new int[0]);
            Assert.IsTrue(use.Success, use.Message);
            Assert.AreEqual(card, model.Describe().Active);
            CollectionAssert.DoesNotContain(model.Describe().Hand, card);
        }

        [Test]
        public void Failures_给出具体原因且不产生副作用()
        {
            var sim = NewSim(5);
            var model = new HexCommandModel(sim, Side.Blue);

            var draw = model.Draw();
            Assert.IsFalse(draw.Success);
            Assert.AreEqual(HexDrawError.NotInDrawWindow.ToString(), draw.Code);
            StringAssert.Contains("窗口", draw.Message);
            Assert.AreEqual(0, model.Describe().PendingChoices.Count);

            var choose = model.Choose(HexId.SteelTide);
            Assert.IsFalse(choose.Success);
            Assert.AreEqual(HexChooseError.NoPendingChoice.ToString(), choose.Code);
            Assert.AreEqual(0, model.Describe().Hand.Count);

            var use = model.Use(HexId.SteelTide, null);
            Assert.IsFalse(use.Success);
            Assert.AreEqual(HexUseError.CardNotHeld.ToString(), use.Code);
            Assert.IsNull(model.Describe().Active);
        }

        [Test]
        public void Sandbag_多选在适配层拒绝且保留手牌()
        {
            var sim = NewSim(7);
            GiveEpAndReachFirstNode(sim, Side.Red, 2);
            var model = new HexCommandModel(sim, Side.Red);
            var draw = model.Draw();
            CollectionAssert.Contains(draw.Choices, HexId.Sandbag);
            Assert.IsTrue(model.Choose(HexId.Sandbag).Success);

            var result = model.Use(HexId.Sandbag, new[] { 1, 2 });
            Assert.IsFalse(result.Success);
            Assert.AreEqual(HexUseError.InvalidTarget.ToString(), result.Code);
            StringAssert.Contains("单选", result.Message);
            CollectionAssert.Contains(model.Describe().Hand, HexId.Sandbag);
        }

        [Test]
        public void Sandbag_目标边界失败零副作用_有效己方单选成功()
        {
            var sim = NewSim(7);
            GiveEpAndReachFirstNode(sim, Side.Red, 2);
            var model = new HexCommandModel(sim, Side.Red);
            var draw = model.Draw();
            Assert.IsTrue(model.Choose(HexId.Sandbag).Success);
            var enemy = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(0, 10));
            var dead = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(1, 10));
            dead.State = EntityState.Dead;
            var own = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(2, 10));

            foreach (var ids in new IReadOnlyList<int>[] { null, new int[0], new[] { enemy.Id },
                         new[] { dead.Id }, new[] { 999999 }, new[] { own.Id, enemy.Id } })
            {
                var failed = model.Use(HexId.Sandbag, ids);
                Assert.IsFalse(failed.Success);
                Assert.AreEqual(HexUseError.InvalidTarget.ToString(), failed.Code);
                CollectionAssert.Contains(model.Describe().Hand, HexId.Sandbag);
                Assert.IsNull(model.Describe().Active);
            }

            var success = model.Use(HexId.Sandbag, new[] { own.Id });
            Assert.IsTrue(success.Success, success.Message);
            CollectionAssert.DoesNotContain(model.Describe().Hand, HexId.Sandbag);
            Assert.AreEqual(HexId.Sandbag, model.Describe().Active);
        }

        [Test]
        public void InvalidSide_所有入口拒绝且不读取或改写Red状态()
        {
            var sim = NewSim(7);
            GiveEpAndReachFirstNode(sim, Side.Red, 2);
            var red = new HexCommandModel(sim, Side.Red);
            var choices = red.Draw().Choices.ToArray();
            var invalid = new HexCommandModel(sim, (Side)99);

            Assert.AreEqual(0, invalid.Describe().PendingChoices.Count);
            Assert.AreEqual(HexDrawError.InvalidSide.ToString(), invalid.Draw().Code);
            Assert.AreEqual(HexChooseError.InvalidSide.ToString(), invalid.Choose(choices[0]).Code);
            Assert.AreEqual(HexUseError.InvalidSide.ToString(), invalid.Use(choices[0], null).Code);
            CollectionAssert.AreEqual(choices, HexSystem.PendingChoicesOf(sim, Side.Red));
            Assert.AreEqual(0, HexSystem.HandOf(sim, Side.Red).Count);
        }

        [Test]
        public void IncompleteState_所有入口安全返回InvalidState()
        {
            var noConfig = new SimCore(null, new SeededRng(1)) { Game = _config };
            AssertInvalidState(noConfig);
            var noEp = NewSim(2);
            noEp.Game.Ep = null;
            AssertInvalidState(noEp);
            _config = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var noHexes = NewSim(3);
            noHexes.Game.Hexes = null;
            AssertInvalidState(noHexes);
        }

        [Test]
        public void Describe_返回快照不随后续选择改变()
        {
            var sim = NewSim(7);
            GiveEpAndReachFirstNode(sim, Side.Red, 2);
            var model = new HexCommandModel(sim, Side.Red);
            model.Draw();
            var before = model.Describe();
            model.Choose(before.PendingChoices[0]);
            Assert.AreEqual(3, before.PendingChoices.Count);
            Assert.AreEqual(0, before.Hand.Count);
            Assert.AreEqual(0, model.Describe().PendingChoices.Count);
        }

        private static void AssertInvalidState(SimCore sim)
        {
            var model = new HexCommandModel(sim, Side.Blue);
            Assert.DoesNotThrow(() => model.Describe());
            Assert.IsFalse(model.Describe().Valid);
            Assert.AreEqual(0, model.Describe().PendingChoices.Count);
            Assert.AreEqual(HexDrawError.InvalidState.ToString(), model.Draw().Code);
            Assert.AreEqual(HexChooseError.InvalidState.ToString(), model.Choose(HexId.SteelTide).Code);
            Assert.AreEqual(HexUseError.InvalidState.ToString(), model.Use(HexId.SteelTide, null).Code);
        }
    }
}
