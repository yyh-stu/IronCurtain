using System.Collections.Generic;
using System.Linq;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public class AiTests
    {
        private static GameConfig _cfg;
        private SimCore sim;
        private Entity blueHQ, redHQ, blueBarracks, blueTower, redTower, redBarracks, neutral;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig(), new SeededRng(1));
            sim.Game = _cfg;
            AiSystem.Reset();
            blueHQ = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
            blueHQ.Prod = new ProductionSet();
            blueBarracks = sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(15, 110));
            blueBarracks.Prod = new ProductionSet();
            blueTower = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 114));
            redHQ = sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 136));
            redHQ.Prod = new ProductionSet();
            redBarracks = sim.SpawnBuilding(Side.Red, BuildingId.Barracks, new GridPos(15, 134));
            redBarracks.Prod = new ProductionSet();
            redTower = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 130));
            neutral = sim.SpawnBuilding(Side.Neutral, BuildingId.ResourcePoint, new GridPos(15, 122));
        }

        [TearDown]
        public void TearDown() => AiSystem.Reset();

        private void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Step();
        }

        private Entity SpawnHero(Side side, HeroId heroId, GridPos pos)
        {
            var c = sim.Game.Heroes[(int)heroId];
            var u = sim.SpawnEntity(side, EntityKind.Unit, pos);
            u.Hero = heroId; u.MaxHp = c.MaxHp; u.Hp = c.MaxHp; u.Attack = c.Attack;
            u.AttackIntervalSec = (float)c.AttackIntervalSec; u.Range = c.Range; u.MoveSpeed = (float)c.MoveSpeed;
            if (c.Shield > 0) { u.Shield = c.Shield; u.ShieldMax = c.Shield; }
            return u;
        }

        private void GiveEp(Side side, int amount)
        {
            for (int i = 0; i < amount; i++)
                sim.SpawnBuilding(side, BuildingId.ResourcePoint, new GridPos(14 + i % 3, 128 + i / 3));
            Run(905);
        }

        /// <summary>让某方"见过"一座建筑:放侦察单位在旁一格,步进后移除(快照冻结保留)。</summary>
        private void RevealBuilding(Side viewer, Entity b)
        {
            int col = b.Pos.Col > 0 ? b.Pos.Col - 1 : b.Pos.Col + 1;
            var scout = SpawnHero(viewer, HeroId.PanShi, new GridPos((b.Pos.Row + 1) % 3, col));
            sim.Step();
            sim.RemoveEntity(scout.Id);
        }

        [Test]
        public void T1_简单满产()
        {
            AiSystem.Configure(sim, Side.Blue, AiTier.Simple);
            Run(25);
            bool hasWork = blueHQ.Prod.Slots.Any(s => s.Current != null || s.Pending.Count > 0);
            Assert.IsTrue(hasWork, "简单 AI 应持续补满生产队列");
        }

        [Test]
        public void T2_简单不越权()
        {
            GiveEp(Side.Blue, 2);
            AiSystem.Configure(sim, Side.Blue, AiTier.Simple);
            Run(60);
            Assert.IsFalse(sim.IsUpgraded(Side.Blue, HeroId.ZhenYue), "简单 AI 不应买升级");
            Assert.IsNull(HexSystem.ActiveOf(Side.Blue), "简单 AI 不应抽海克斯");
            Assert.AreEqual(SiegeIntent.None, SiegeSystem.GetIntent(sim, redTower.Id), "简单 AI 不应下达攻城意图");
        }

        [Test]
        public void T3_简单被动回防()
        {
            SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(15, 112));
            SpawnHero(Side.Blue, HeroId.DuanFeng, new GridPos(14, 112));
            SpawnHero(Side.Red, HeroId.PanShi, new GridPos(15, 116)); // 蓝塔 8 格内
            AiSystem.Configure(sim, Side.Blue, AiTier.Simple);
            Run(25);
            bool anyMove = sim.Entities.Values.Any(e => e.Side == Side.Blue && e.Kind == EntityKind.Unit
                && e.HasMoveTarget && e.State == EntityState.Moving);
            Assert.IsTrue(anyMove, "敌单位逼近己方建筑时,简单 AI 应调兵回防");
        }

        [Test]
        public void T4_普通补产镇岳()
        {
            AiSystem.Configure(sim, Side.Blue, AiTier.Normal);
            Run(1000); // 配比"3 只 → 1 镇岳";20s/将+错峰出生,出生节奏随场景浮动,放宽并兜底查场上
            bool hasZhenYue = sim.Entities.Values.Any(e => e.Side == Side.Blue && e.Kind == EntityKind.Unit
                && !e.IsSoldier && e.Hero == HeroId.ZhenYue)
                || blueHQ.Prod.Slots.Any(s =>
                s.Current == HeroId.ZhenYue || s.Pending.Contains(HeroId.ZhenYue));
            Assert.IsTrue(hasZhenYue, "普通 AI 兵种配比应含镇岳");
        }

        [Test]
        public void T5_普通买升级()
        {
            GiveEp(Side.Blue, 2);
            AiSystem.Configure(sim, Side.Blue, AiTier.Normal);
            Run(25);
            Assert.IsTrue(sim.IsUpgraded(Side.Blue, HeroId.ZhenYue), "普通 AI 在 EP 足够时应买镇岳升级");
        }

        [Test]
        public void T6_普通规律抽卡()
        {
            GiveEp(Side.Blue, 4);
            Run(900); // 至 ~1805 tick,进入 3 分钟节点窗口
            AiSystem.Configure(sim, Side.Blue, AiTier.Normal);
            Run(25);
            Assert.IsNotNull(HexSystem.ActiveOf(Side.Blue), "3 分钟节点窗口内普通 AI 应抽一张海克斯");
        }

        [Test]
        public void T7_普通集结阈值()
        {
            RevealBuilding(Side.Blue, redTower);
            // 将兵制(M8.4b 名额 4):波次门槛按兵团计=2(旧 8 只单兵口径作废)
            SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(0, 3));
            AiSystem.Configure(sim, Side.Blue, AiTier.Normal);
            Run(25);
            Assert.AreEqual(SiegeIntent.None, SiegeSystem.GetIntent(sim, neutral.Id), "1 兵团不应触发集结");

            SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(1, 3)); // 第 2 兵团
            Run(25);
            // 蓝塔视野自动揭示中立点 → 波次首选中立(占中立优先)
            Assert.AreEqual(SiegeIntent.Capture, SiegeSystem.GetIntent(sim, neutral.Id), "2 兵团应触发对中立点的攻城意图");
        }

        [Test]
        public void T8_普通挑弱侧()
        {
            sim.RemoveEntity(neutral.Id); // 去掉中立,直测"弱侧"
            var g1 = SpawnHero(Side.Red, HeroId.PanShi, new GridPos(15, 129));
            g1.GarrisonBuildingId = redTower.Id; redTower.GarrisonFormal.Add(g1.Id);
            for (int i = 0; i < 5; i++)
            {
                var g = SpawnHero(Side.Red, HeroId.DuanFeng, new GridPos(i % 3, 10 + i));
                g.GarrisonBuildingId = redBarracks.Id; redBarracks.GarrisonFormal.Add(g.Id);
            }
            RevealBuilding(Side.Blue, redTower);
            RevealBuilding(Side.Blue, redBarracks);
            for (int i = 0; i < 8; i++)
                SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(i % 3, 3 + i / 3));
            AiSystem.Configure(sim, Side.Blue, AiTier.Normal);
            Run(25);
            Assert.AreNotEqual(SiegeIntent.None, SiegeSystem.GetIntent(sim, redTower.Id), "应选择驻军更少的红塔(弱侧)");
        }

        [Test]
        public void T9_普通占中立优先()
        {
            RevealBuilding(Side.Blue, neutral);
            RevealBuilding(Side.Blue, redTower);
            for (int i = 0; i < 8; i++)
                SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(i % 3, 3 + i / 3));
            AiSystem.Configure(sim, Side.Blue, AiTier.Normal);
            Run(25);
            Assert.AreEqual(SiegeIntent.Capture, SiegeSystem.GetIntent(sim, neutral.Id), "优先占领中立资源点");
        }

        [Test]
        public void T10_普通主动防守()
        {
            SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(15, 112));
            SpawnHero(Side.Blue, HeroId.DuanFeng, new GridPos(14, 112));
            SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(16, 112));
            for (int i = 0; i < 8; i++)
                SpawnHero(Side.Red, HeroId.PanShi, new GridPos(14 + i % 3, 118 + i / 3)); // 蓝塔 10 格内且可见
            AiSystem.Configure(sim, Side.Blue, AiTier.Normal);
            Run(25);
            bool defending = sim.Entities.Values.Any(e => e.Side == Side.Blue && e.Kind == EntityKind.Building
                    && e.GarrisonFormal.Count > 0)
                || sim.Entities.Values.Any(e => e.Side == Side.Blue && e.Kind == EntityKind.Unit
                    && e.HasMoveTarget && e.State == EntityState.Moving);
            Assert.IsTrue(defending, "大量敌军逼近时普通 AI 应调兵防守该建筑");
        }
    }
}
