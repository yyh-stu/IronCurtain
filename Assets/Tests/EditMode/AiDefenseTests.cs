using System.Linq;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>2026-09-12 AI 智能化第一步:威胁响应触发器——
    /// ①敌兵压境 → 就近士兵入城驻守;②城池被攻 → 调闲置主将回防。纯规则,确定性种子。</summary>
    public class AiDefenseTests
    {
        private static GameConfig _cfg;
        private SimCore sim;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig(), new SeededRng(5));
            sim.Game = _cfg;
            AiSystem.Reset();
        }

        [TearDown]
        public void TearDown() => AiSystem.Reset();

        private void Run(int ticks) { for (int i = 0; i < ticks; i++) sim.Step(); }

        [Test]
        public void 敌兵压境_AI征就近士兵入城驻守()
        {
            var barracks = sim.SpawnBuilding(Side.Red, BuildingId.Barracks, new GridPos(80, 200));
            var redG = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(78, 200)); // 兵团在城旁
            // 蓝方近战兵团压境(距城约 10m,塔视野 24 内可见;近战射程够不着,不会当场开打)
            RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(80, 193)); // 纵队在主将身后,摆近确保压境计数达标
            AiSystem.Configure(sim, Side.Red, AiTier.Normal);

            Assert.AreEqual(0, barracks.GarrisonTemp.Count + barracks.GarrisonFormal.Count, "开局无人驻守");
            Run(25);   // 决策 tick=20 时触发

            Assert.Greater(barracks.GarrisonTemp.Count + barracks.GarrisonFormal.Count, 0,
                "敌兵压境(≥3 可见敌兵在 10m 内)→ 就近士兵应入城驻守,实测临时+正式="
                + barracks.GarrisonTemp.Count + "+" + barracks.GarrisonFormal.Count);
        }

        [Test]
        public void 城池被攻_AI调闲置主将回防()
        {
            var tower = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 200));
            var idleG = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.DuanFeng, new GridPos(70, 205)); // 闲置主将
            AiSystem.Configure(sim, Side.Red, AiTier.Normal);

            Run(21);                       // 首个决策 tick:记录血快照(无威胁不触发)
            Assert.IsFalse(idleG.HasMoveTarget, "未被威胁时主将不动");

            tower.Hp -= 20f;               // 模拟被攻击(血下降)
            Run(20);                       // 下个决策 tick:underAttack 触发

            Assert.IsTrue(idleG.HasMoveTarget || idleG.State == EntityState.Moving,
                "城池掉血 → 最近的闲置主将应被调往城门回防");
        }
    }
}
