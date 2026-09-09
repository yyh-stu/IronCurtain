using System.Collections.Generic;
using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class InputAuditTests
    {
        [Test]
        public void FormatIds_UsesStableMachineReadableValues()
        {
            Assert.AreEqual("none", InputAudit.FormatIds(null));
            Assert.AreEqual("none", InputAudit.FormatIds(new List<int>()));
            Assert.AreEqual("9,3,12", InputAudit.FormatIds(new List<int> { 9, 3, 12 }));
        }

        [Test]
        public void GeneralIds_ReturnsSortedLivingGeneralsForRequestedSide()
        {
            var config = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig(), new SeededRng(1)) { Game = config };
            var blue1 = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 24));
            var red = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.DuanFeng, new GridPos(15, 263));
            var blue2 = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhuiRi, new GridPos(15, 36));
            red.State = EntityState.Dead;

            var ids = InputAudit.GeneralIds(sim, Side.Blue);

            CollectionAssert.AreEqual(new[] { blue1.Id, blue2.Id }, ids);
            Assert.AreEqual(blue1.Id + ":PanShi," + blue2.Id + ":ZhuiRi",
                InputAudit.DescribeGenerals(sim, Side.Blue));
        }
    }
}
