using NUnit.Framework;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>M8.4c 总览面板新口径:区块=主基地、名额、补兵池。</summary>
    public class BarracksOverviewTests
    {
        private static GameConfig _cfg;
        private SimCore sim;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig(), new SeededRng(1));
            sim.Game = _cfg;
        }

        private Entity MakeBase(Side side, int row, int col)
        {
            var b = sim.SpawnBuilding(side, BuildingId.Base, new GridPos(row, col));
            b.Prod = new ProductionSet();
            return b;
        }

        private void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Step();
        }

        private int SoldiersOf(Entity general)
        {
            int n = 0;
            foreach (var e in sim.Entities.Values)
                if (e.IsSoldier && e.GeneralId == general.Id && e.State != EntityState.Dead) n++;
            return n;
        }

        [Test]
        public void T1_BlueView_ListsOwnBaseOnly()
        {
            var blue = MakeBase(Side.Blue, 15, 110);
            MakeBase(Side.Red, 15, 175);
            sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(15, 140)); // 兵营不算区块

            var model = new BarracksOverviewModel(sim, Side.Blue);
            var blocks = model.DescribeBlocks();

            Assert.AreEqual(1, blocks.Count, "蓝方视角只应看到 1 座己方主基地");
            Assert.AreEqual(blue.Id, blocks[0].BarracksId);
            Assert.AreEqual(BarracksOverviewModel.BlockState.Producing, blocks[0].State);
            Assert.AreEqual(3, blocks[0].Slots.Length, "恒 3 槽");
        }

        [Test]
        public void T2_Rubbled_Destroyed_NoProduction()
        {
            var b = MakeBase(Side.Blue, 15, 110);
            b.Rubbled = true;

            var model = new BarracksOverviewModel(sim, Side.Blue);
            var blocks = model.DescribeBlocks();

            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(BarracksOverviewModel.BlockState.Destroyed, blocks[0].State);

            Assert.IsNotNull(model.StartProduction(b.Id, HeroId.PanShi), "废墟入队应失败");
            Assert.IsNotNull(model.Cancel(b.Id, 0), "废墟取消应失败");
        }

        [Test]
        public void T3_Captured_DisappearsFromOwner()
        {
            var b = MakeBase(Side.Blue, 15, 110);
            var blueModel = new BarracksOverviewModel(sim, Side.Blue);
            Assert.AreEqual(1, blueModel.DescribeBlocks().Count, "占领前蓝方可见");

            b.Side = Side.Red; // Sim 层占领翻转
            Assert.AreEqual(0, blueModel.DescribeBlocks().Count, "易主后蓝方视角消失");

            var redModel = new BarracksOverviewModel(sim, Side.Red);
            Assert.AreEqual(1, redModel.DescribeBlocks().Count, "红方视角可见");
            Assert.AreEqual(b.Id, redModel.DescribeBlocks()[0].BarracksId);
        }

        [Test]
        public void T4_StartProduction_Enqueues()
        {
            var b = MakeBase(Side.Blue, 15, 110);
            var model = new BarracksOverviewModel(sim, Side.Blue);

            int before = 0;
            foreach (var s in model.DescribeBlocks()[0].Slots) before += s.PendingCount;

            Assert.IsNull(model.StartProduction(b.Id, HeroId.PanShi), "入队应成功");

            int after = 0;
            foreach (var s in model.DescribeBlocks()[0].Slots) after += s.PendingCount;
            Assert.AreEqual(before + 1, after, "待产总数应 +1");
        }

        [Test]
        public void T5_Cancel_EmptyQueueError()
        {
            var b = MakeBase(Side.Blue, 15, 110);
            var model = new BarracksOverviewModel(sim, Side.Blue);

            Assert.IsNull(model.StartProduction(b.Id, HeroId.PanShi));
            Step(1); // 开工 → 槽0

            Assert.IsNull(model.Cancel(b.Id, 0), "取消进行中槽应成功");
            Assert.IsNull(b.Prod.Slots[0].Current, "取消后槽0应回退为空");

            string err = model.Cancel(b.Id, 1);
            Assert.IsNotNull(err, "空槽取消应失败");
            Assert.IsTrue(err.Contains("队列为空"), "错误文案应含 队列为空,实得: " + err);
        }

        [Test]
        public void T6_MultiBase_TwoBlocks()
        {
            var b1 = MakeBase(Side.Blue, 15, 110);
            var b2 = MakeBase(Side.Blue, 10, 110);
            var model = new BarracksOverviewModel(sim, Side.Blue);

            var blocks = model.DescribeBlocks();
            Assert.AreEqual(2, blocks.Count);
            Assert.AreNotEqual(blocks[0].BarracksId, blocks[1].BarracksId);
        }

        [Test]
        public void T7_TotalSeconds_DisplayMatchesCadence()
        {
            var b = MakeBase(Side.Blue, 15, 110);
            var model = new BarracksOverviewModel(sim, Side.Blue);

            Assert.IsNull(model.StartProduction(b.Id, HeroId.PanShi));
            Step(1); // 开工

            var slot = model.DescribeBlocks()[0].Slots[0];
            Assert.IsTrue(slot.Current.HasValue, "槽0 应已开工");
            Assert.AreEqual(ProductionSystem.GeneralProductionSeconds, slot.TotalSeconds, 1e-3f,
                "展示时长应为 20s/将 恒定值");
        }

        [Test]
        public void T8_Population_MatchesSim()
        {
            MakeBase(Side.Blue, 15, 110);
            var model = new BarracksOverviewModel(sim, Side.Blue);

            Assert.AreEqual(0, model.PopulationOf(), "0 将起步");
            Assert.AreEqual(sim.PopulationOf(Side.Blue), model.PopulationOf());

            RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 130));

            Assert.AreEqual(1, model.PopulationOf(), "产 1 将后名额 ==1");
            Assert.AreEqual(sim.PopulationOf(Side.Blue), model.PopulationOf());
            Assert.AreEqual(SimCore.GeneralCap, model.PopulationCapOf());
            Assert.AreEqual(sim.PopulationCapOf(Side.Blue), model.PopulationCapOf());
        }

        [Test]
        public void T9_ReinforcePool_RequestFillsRegiment()
        {
            MakeBase(Side.Blue, 15, 110);
            var model = new BarracksOverviewModel(sim, Side.Blue);

            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 130));
            Assert.AreEqual(20, SoldiersOf(g), "出场满编 20 兵");

            // 杀掉 15 兵 → 场上该兵团剩 5 兵
            int killed = 0;
            foreach (var e in sim.Entities.Values)
            {
                if (killed >= 15) break;
                if (e.IsSoldier && e.GeneralId == g.Id) { e.State = EntityState.Dead; killed++; }
            }
            Assert.AreEqual(5, SoldiersOf(g), "剩 5 兵");

            sim.ReinforcePool[Side.Blue] = 5;
            Assert.AreEqual(sim.ReinforcePool[Side.Blue], model.ReinforcePoolOf(), "池显示应与 sim 一致");
            Assert.AreEqual(ReinforceSystem.PoolCap, model.ReinforcePoolCap());

            Assert.IsNull(model.RequestReinforce(out var err), "补兵应成功,实得: " + err);
            Assert.AreEqual(0, model.ReinforcePoolOf(), "池 5 兵应全部用于补员");
            Assert.AreEqual(10, SoldiersOf(g), "兵团应 +5 兵(5+5=10),计数与池变化一致");
        }
    }
}
