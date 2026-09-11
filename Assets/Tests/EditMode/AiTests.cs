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
            Assert.IsNull(HexSystem.ActiveOf(sim, Side.Blue), "简单 AI 不应抽海克斯");
            Assert.AreEqual(SiegeIntent.None, SiegeSystem.GetIntent(sim, redTower.Id), "简单 AI 不应下达攻城意图");
        }

        [Test]
        public void T3_简单只攻不防_敌压境不驻军()
        {
            // 2026-09-12 新简单档:只攻不防——敌兵压境不做驻军响应(与困难档相反)
            SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(15, 112));
            SpawnHero(Side.Blue, HeroId.DuanFeng, new GridPos(14, 112));
            SpawnHero(Side.Red, HeroId.PanShi, new GridPos(15, 116)); // 蓝塔 8 格内
            AiSystem.Configure(sim, Side.Blue, AiTier.Simple);
            Run(25);
            Assert.AreEqual(0, blueTower.GarrisonTemp.Count + blueTower.GarrisonFormal.Count,
                "简单 AI 只攻不防:敌压境不驻军(威胁响应仅困难档)");
        }

        [Test]
        public void T4_普通补产随机混编()
        {
            // 2026-09-11 制作人拍板"AI 选将随机化,避免全屏磐石":旧的"3 只 → 1 镇岳"固定配比已废弃,
            // 新契约=从六英雄加权池随机(近战×2/远程×1)。本测试断言产出落在合法池内。
            AiSystem.Configure(sim, Side.Blue, AiTier.Hard);
            Run(1000); // 20s/将+错峰出生,出生节奏随场景浮动
            var pool = new HashSet<HeroId> { HeroId.ZhenYue, HeroId.PanShi, HeroId.DuanFeng,
                                             HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
            var produced = sim.Entities.Values
                .Where(e => e.Side == Side.Blue && e.Kind == EntityKind.Unit && !e.IsSoldier && e.Hero.HasValue)
                .Select(e => e.Hero.Value)
                .Concat(blueHQ.Prod.Slots.Where(s => s.Current != null).Select(s => s.Current.Value)
                    .Concat(blueHQ.Prod.Slots.SelectMany(s => s.Pending)))
                .ToList();
            Assert.IsNotEmpty(produced, "普通 AI 应有产出");
            foreach (var h in produced)
                Assert.IsTrue(pool.Contains(h), "产出必须在六英雄池内:" + h);
        }

        [Test]
        public void T5_普通买升级()
        {
            GiveEp(Side.Blue, 2);
            AiSystem.Configure(sim, Side.Blue, AiTier.Hard);
            Run(25);
            Assert.IsTrue(sim.IsUpgraded(Side.Blue, HeroId.ZhenYue), "普通 AI 在 EP 足够时应买镇岳升级");
        }

        [Test]
        public void T6_普通规律抽卡()
        {
            GiveEp(Side.Blue, 4);
            Run(900); // 至 ~1805 tick,进入 3 分钟节点窗口
            AiSystem.Configure(sim, Side.Blue, AiTier.Hard);
            Run(25);
            Assert.IsNotNull(HexSystem.ActiveOf(sim, Side.Blue), "3 分钟节点窗口内普通 AI 应抽一张海克斯");
            Assert.AreEqual(1f, sim.EpOf(Side.Blue), 0.001f, "升级 2EP + 抽取 2EP；释放不得二次扣费");
            Assert.AreEqual(0, HexSystem.PendingChoicesOf(sim, Side.Blue).Count, "AI 应完成三选一");
            Assert.AreEqual(0, HexSystem.HandOf(sim, Side.Blue).Count, "AI 应立即释放所选卡");
        }

        [Test]
        public void T7_集结超时后强制出击()
        {
            // 2026-09-12 新脚本:先立足驻防,集结/阶段 40s 超时后必须出击(防卡死保险①)
            GiveEp(Side.Blue, 2);
            AiSystem.Configure(sim, Side.Blue, AiTier.Hard);
            Run(460);   // 46s:超过 PhaseTimeoutTicks(400)
            bool attacked = SiegeSystem.GetIntent(sim, redTower.Id) != SiegeIntent.None
                || sim.Entities.Values.Any(e => e.Side == Side.Blue && e.Kind == EntityKind.Unit
                    && e.TargetBuildingId != 0);
            Assert.IsTrue(attacked, "超时后(兵力不足以满编驻防)仍应强制发起进攻");
        }

        // T8_普通挑弱侧:2026-09-12 旧 Normal"挑弱侧/中立优先"随脚本化改版退役删除

        // T9_普通占中立优先:2026-09-12 旧 Normal"挑弱侧/中立优先"随脚本化改版退役删除

        [Test]
        public void T10_普通主动防守()
        {
            SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(15, 112));
            SpawnHero(Side.Blue, HeroId.DuanFeng, new GridPos(14, 112));
            SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(16, 112));
            for (int i = 0; i < 8; i++)
                SpawnHero(Side.Red, HeroId.PanShi, new GridPos(14 + i % 3, 118 + i / 3)); // 蓝塔 10 格内且可见
            AiSystem.Configure(sim, Side.Blue, AiTier.Hard);
            Run(25);
            bool defending = sim.Entities.Values.Any(e => e.Side == Side.Blue && e.Kind == EntityKind.Building
                    && e.GarrisonFormal.Count > 0)
                || sim.Entities.Values.Any(e => e.Side == Side.Blue && e.Kind == EntityKind.Unit
                    && e.HasMoveTarget && e.State == EntityState.Moving);
            Assert.IsTrue(defending, "大量敌军逼近时普通 AI 应调兵防守该建筑");
        }
    }
}
