using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 制作人:"训练场 EP 无限"。
    /// 口径与既有的"训练场将名额无限"(`PopulationCapOverride = 999`)同款:设 `EpCapOverride`
    /// 就把 EP **钉在**该值上(花掉立刻补满),顶栏用 `EpCapEffective` 显示同一个上限。
    /// </summary>
    public sealed class PracticeModeTests
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

        [Test]
        public void T1_训练场_EP恒为上限且花完立刻补满()
        {
            var sim = NewSim();
            sim.EpCapOverride = 99;
            for (int i = 0; i < 3; i++) sim.Step();
            Assert.AreEqual(99f, sim.EpOf(Side.Blue), "训练场 EP 应钉在 99");

            Assert.IsTrue(sim.SpendEp(Side.Blue, 5f), "训练场应能随便花");
            Assert.AreEqual(94f, sim.EpOf(Side.Blue), "花完当 tick 内是 94");
            sim.Step();
            Assert.AreEqual(99f, sim.EpOf(Side.Blue), "下一 tick 应立刻补满(等于无限)");
            Assert.AreEqual(99, sim.EpCapEffective, "顶栏显示用的上限也应读同一个口径");
        }

        [Test]
        public void T2_对局模式_EP仍按资源点累积且有上限5()
        {
            var sim = NewSim();
            var b = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 30));
            b.Prod = new ProductionSet();
            int pts = sim.OwnedResourcePointsOf(Side.Blue);
            if (pts <= 0)   // 本局地图可能没给蓝方资源点 → 造一个
            {
                sim.SpawnBuilding(Side.Blue, BuildingId.ResourcePoint, new GridPos(15, 40));
                pts = sim.OwnedResourcePointsOf(Side.Blue);
            }
            Assert.Greater(pts, 0, "前置:蓝方应有资源点");
            Assert.AreEqual(_cfg.Ep.Cap, sim.EpCapEffective, "对局模式上限就是配置的 5");

            // 资源点给 EP:推进到第一次累积(90 秒 / 点数)
            int ticks = (int)((double)_cfg.Ep.IntervalSeconds / pts * sim.Config.TickRate) + 2;
            for (int i = 0; i < ticks; i++) sim.Step();
            Assert.Greater(sim.EpOf(Side.Blue), 0f, "对局模式应照常按资源点累积 EP");
            Assert.LessOrEqual(sim.EpOf(Side.Blue), _cfg.Ep.Cap, "对局模式 EP 不得超过上限 5");
        }
    }
}
