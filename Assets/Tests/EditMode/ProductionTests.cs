using System.Collections.Generic;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>将生产口径测试(M8.4b):主基地产将、20s 节奏、名额 4、满员等待。</summary>
    public sealed class ProductionTests
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

        private static Entity MakeBase(SimCore sim, Side side, int row, int col)
        {
            var b = sim.SpawnBuilding(side, BuildingId.Base, new GridPos(row, col));
            b.Prod = new ProductionSet();
            return b;
        }

        private static void Step(SimCore sim, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Step();
        }

        private static List<Entity> GeneralsOf(SimCore sim, Side side)
        {
            var list = new List<Entity>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == side
                    && e.Hero != null && e.State != EntityState.Dead)
                    list.Add(e);
            return list;
        }

        private static int SoldiersOf(SimCore sim, Entity general)
        {
            int n = 0;
            foreach (var e in sim.Entities.Values)
                if (e.IsSoldier && e.GeneralId == general.Id && e.State != EntityState.Dead) n++;
            return n;
        }

        [Test]
        public void T1_SingleBase_Cadence()
        {
            var sim = NewSim();
            var b = MakeBase(sim, Side.Blue, 15, 110);
            for (int i = 0; i < 5; i++)
                Assert.IsTrue(ProductionSystem.Enqueue(sim, b.Id, HeroId.PanShi, out _), "入队应成功");

            // 5 项按"最少待产项"分配:槽0×2、槽1×2、槽2×1 → 三槽并行,20s/将
            Step(sim, 199);
            Assert.AreEqual(0, GeneralsOf(sim, Side.Blue).Count, "tick199 不应有将产出");

            Step(sim, 61); // 三槽并行但兵团出生区错峰(实测 #2≈t226/#3≈t250),tick260 前齐出
            var wave1 = GeneralsOf(sim, Side.Blue);
            Assert.AreEqual(3, wave1.Count, "tick200 三槽并行各出 1 将");
            foreach (var g in wave1)
                Assert.AreEqual(12, SoldiersOf(sim, g), "出场自带满编 12 兵");
            Assert.AreEqual(3, sim.PopulationOf(b.Side), "3 将占 3 名额");

            Step(sim, 200);
            var wave2 = GeneralsOf(sim, Side.Blue);
            Assert.AreEqual(4, wave2.Count, "tick460 第 4 将产出,名额满 4");
            foreach (var g in wave2)
                Assert.AreEqual(12, SoldiersOf(sim, g), "每个将均带满编 12 兵");
            Assert.AreEqual(4, sim.PopulationOf(Side.Blue), "名额满 4");

            // 第 5 个待名额:某槽保持进行中(未清空)
            bool waiting = false;
            foreach (var slot in b.Prod.Slots)
                if (slot.Current != null) waiting = true;
            Assert.IsTrue(waiting, "第 5 将应待名额(槽未清空)");
        }

        [Test]
        public void T2_GeneralCap4()
        {
            var sim = NewSim();
            var b = MakeBase(sim, Side.Blue, 15, 110);
            Assert.AreEqual(4, SimCore.GeneralCap, "测试版名额 4");
            Assert.AreEqual(4, sim.PopulationCapOf(Side.Blue));
            Assert.AreEqual(0, sim.PopulationOf(Side.Blue), "0 将起步");

            for (int i = 0; i < 5; i++)
                ProductionSystem.Enqueue(sim, b.Id, HeroId.PanShi, out _);

            Step(sim, 450); // 错峰出生:第 4 将 ~tick401+ε
            Assert.AreEqual(4, sim.PopulationOf(Side.Blue), "产 4 将后 PopulationOf==4==Cap");
            Assert.AreEqual(4, GeneralsOf(sim, Side.Blue).Count);

            // 将死 1 个 → 释放名额 → 第 5 将补位
            GeneralsOf(sim, Side.Blue)[0].State = EntityState.Dead;
            Assert.AreEqual(3, sim.PopulationOf(Side.Blue), "死亡将不占名额");

            Step(sim, 250); // 第 5 将 ~tick601 补位
            var generals = GeneralsOf(sim, Side.Blue);
            Assert.AreEqual(4, generals.Count, "第 5 将应补位产出(存活 4)");
            foreach (var g in generals)
                Assert.AreEqual(12, SoldiersOf(sim, g), "补位将同样满编 12 兵");
            Assert.AreEqual(4, sim.PopulationOf(Side.Blue));
        }

        [Test]
        public void T3_GeneralTime()
        {
            var sim = NewSim();
            var b = MakeBase(sim, Side.Blue, 15, 110);
            Assert.IsTrue(ProductionSystem.Enqueue(sim, b.Id, HeroId.PanShi, out _));

            Step(sim, 199);
            Assert.AreEqual(0, GeneralsOf(sim, Side.Blue).Count, "199 tick 不出");

            Step(sim, 1);
            var generals = GeneralsOf(sim, Side.Blue);
            Assert.AreEqual(1, generals.Count, "200 tick 出(20s/将)");
            Assert.AreEqual(12, SoldiersOf(sim, generals[0]));
        }

        [Test]
        public void T4_EnqueueValidation()
        {
            var sim = NewSim();
            // 兵营不再是产将目标(无生产集)
            var barracks = sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(15, 140));
            Assert.IsFalse(ProductionSystem.Enqueue(sim, barracks.Id, HeroId.PanShi, out var err1), "兵营入队应失败");
            Assert.AreEqual("目标不是主基地", err1);

            var b = MakeBase(sim, Side.Blue, 15, 110);
            Assert.IsTrue(ProductionSystem.Enqueue(sim, b.Id, HeroId.PanShi, out var err2), "基地入队应成功");
            Assert.IsNull(err2);
        }

        [Test]
        public void T5_Cancel()
        {
            var sim = NewSim();
            var b = MakeBase(sim, Side.Blue, 15, 110);
            Assert.IsTrue(ProductionSystem.Enqueue(sim, b.Id, HeroId.PanShi, out _));
            Assert.IsTrue(ProductionSystem.Enqueue(sim, b.Id, HeroId.DuanFeng, out _));
            // 最少待产项分配:磐石→槽0,断峰→槽1
            Assert.AreEqual(1, b.Prod.Slots[0].Pending.Count, "磐石应入槽0");
            Assert.AreEqual(1, b.Prod.Slots[1].Pending.Count, "断峰应入槽1");

            Assert.IsTrue(ProductionSystem.CancelLast(sim, b.Id, 0, out _), "取消待产应成功");
            Assert.AreEqual(0, b.Prod.Slots[0].Pending.Count, "取消后槽0应为空");
            Assert.IsNull(b.Prod.Slots[0].Current);

            Step(sim, 1); // 槽1 开工断峰(进行中)
            Assert.IsNotNull(b.Prod.Slots[1].Current, "断峰应已开工");
            Assert.IsTrue(ProductionSystem.CancelLast(sim, b.Id, 1, out _), "取消进行中应成功");
            Assert.IsNull(b.Prod.Slots[1].Current, "取消后 Current 应为空");

            Step(sim, 200);
            Assert.AreEqual(0, GeneralsOf(sim, Side.Blue).Count, "全部取消后不应有任何产出");
            Assert.AreEqual(0, sim.PopulationOf(Side.Blue), "名额应全部释放");

            Assert.IsFalse(ProductionSystem.CancelLast(sim, b.Id, 0, out var err), "空队列取消应失败");
            Assert.AreEqual("队列为空", err);
        }

        [Test]
        public void T6_Register()
        {
            var sim = NewSim();
            var b = MakeBase(sim, Side.Blue, 15, 110); // 基地 5×5,前缘列 = 110 + ceil(2.5)+1 = 114
            Assert.IsTrue(ProductionSystem.Enqueue(sim, b.Id, HeroId.PanShi, out _));

            Step(sim, 200);
            var generals = GeneralsOf(sim, Side.Blue);
            Assert.AreEqual(1, generals.Count);
            var g = generals[0];

            Assert.IsFalse(g.IsSoldier, "产出的应为非士兵 Unit(主将)");
            Assert.AreEqual(HeroId.PanShi, g.Hero.Value, "Hero 应正确");
            Assert.AreEqual(118, g.Pos.Col, "出生列应为基地前缘 + 阵型纵深(ceil(2.5)+4+1)");
            Assert.LessOrEqual(System.Math.Abs(g.Pos.Row - 15), 2, "出生行应在基地行 ±2 内");
            Assert.Greater(SimVec2.Distance(g.PosF, SimVec2.FromGrid(b.Pos)),
                BuildingFootprint.HalfW(b) + 0.5f, "出生点应距基地中心 > HalfW+0.5");
        }
    }
}
