using System.Linq;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 制作人实机报障:"产兵我快速按下三个将名时,进度条只跑一个,
    /// 似乎每个指令发出都有延迟,但是实际上还是会把这些将产出来 … **上限同时产三个**"。
    /// 根因(读码即复现):`ProductionSystem.Enqueue` 挑槽只比"排队数最少",而三个空槽的排队数都是 0,
    /// 严格小于永不成立 → 连按三下全挤进 0 号槽 → 只有一条进度条在跑。
    /// </summary>
    public sealed class ProductionQueueTests
    {
        private static GameConfig _cfg;
        private SimCore sim;
        private Entity baseB;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig(), new SeededRng(3));
            sim.Game = _cfg;
            baseB = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 30));
            baseB.Prod = new ProductionSet();
        }

        [Test]
        public void T1_连下三单_应占满三个槽并行生产()
        {
            Assert.IsTrue(ProductionSystem.Enqueue(sim, baseB.Id, HeroId.PanShi, out var e1), e1);
            Assert.IsTrue(ProductionSystem.Enqueue(sim, baseB.Id, HeroId.ZhenYue, out var e2), e2);
            Assert.IsTrue(ProductionSystem.Enqueue(sim, baseB.Id, HeroId.DuanFeng, out var e3), e3);

            // 连下三单应**分别落进三个不同的槽**(而不是全挤进 0 号槽)
            int used = 0;
            for (int i = 0; i < baseB.Prod.Slots.Length; i++)
                if (baseB.Prod.Slots[i].Pending.Count == 1) used++;
            Assert.AreEqual(3, used, "三单应分别落进三个槽(实测只用了 " + used + " 个槽)");

            sim.Step();   // 推进一 tick:排队项提升为"在产"
            int busy = 0;
            for (int i = 0; i < baseB.Prod.Slots.Length; i++)
                if (baseB.Prod.Slots[i].Current.HasValue) busy++;
            Assert.AreEqual(3, busy, "三个槽应同时在产(实测 " + busy + " 个)");
        }

        [Test]
        public void T2_三单并行_进度同时推进()
        {
            ProductionSystem.Enqueue(sim, baseB.Id, HeroId.PanShi, out _);
            ProductionSystem.Enqueue(sim, baseB.Id, HeroId.ZhenYue, out _);
            ProductionSystem.Enqueue(sim, baseB.Id, HeroId.DuanFeng, out _);
            for (int i = 0; i < 30; i++) sim.Step();      // 1 秒
            int advancing = 0;
            foreach (var slot in baseB.Prod.Slots)
                if (slot.Progress > 0.05f) advancing++;
            Assert.AreEqual(3, advancing, "三条进度应同时在推进(实测 " + advancing + " 条)");
        }

        [Test]
        public void T3_第四单落在排队最短的槽_不丢单()
        {
            Assert.IsTrue(ProductionSystem.Enqueue(sim, baseB.Id, HeroId.PanShi, out _));
            Assert.IsTrue(ProductionSystem.Enqueue(sim, baseB.Id, HeroId.ZhenYue, out _));
            Assert.IsTrue(ProductionSystem.Enqueue(sim, baseB.Id, HeroId.DuanFeng, out _));
            sim.Step();     // 三槽进入"在产"
            Assert.IsTrue(ProductionSystem.Enqueue(sim, baseB.Id, HeroId.ZhuFeng, out _));
            int queued = baseB.Prod.Slots.Sum(s => s.Pending.Count);
            Assert.AreEqual(1, queued, "第 4 单应排在某槽队列里(不丢单)");
        }
    }
}
