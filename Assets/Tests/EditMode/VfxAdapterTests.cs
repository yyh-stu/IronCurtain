using NUnit.Framework;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>M1.2/M1.3 适配层与可见性闸的定向测试(纯函数)。</summary>
    public sealed class VfxAdapterTests
    {
        private static GameConfig _cfg;
        private static SimCore NewSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig(), new SeededRng(11)) { Game = _cfg };
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(80, 8));
            return sim;
        }

        [Test]
        public void 来源规则_自伤滤除与无来源与驻军改挂()
        {
            var wallSelf = new SimEvent(1, SimEventType.DamageDealt, 42, 10f, 42);
            Assert.IsTrue(VfxSourceRules.IsWallSelfDamage(wallSelf), "墙体拦截:攻击者==受害者 → 滤除");
            var noSource = new SimEvent(1, SimEventType.DamageDealt, 42, 10f, 0);
            Assert.IsFalse(VfxSourceRules.HasSource(noSource), "AttackerId=0 无来源");
            Assert.IsFalse(VfxSourceRules.IsWallSelfDamage(noSource), "无来源不是自伤");
            var tower = new SimEvent(1, SimEventType.DamageDealt, 42, 10f, 7);
            Assert.IsTrue(VfxSourceRules.HasSource(tower));

            var sim = NewSim();
            var archer = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 10));
            var bldg = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(80, 14));
            archer.GarrisonBuildingId = bldg.Id;
            Assert.AreEqual(bldg.Id, VfxSourceRules.EmitterEntityId(sim, archer.Id), "驻军射手 → 发射点挂建筑");
            var field = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 20));
            Assert.AreEqual(field.Id, VfxSourceRules.EmitterEntityId(sim, field.Id), "野战单位 → 自身");
            Assert.AreEqual(0, VfxSourceRules.EmitterEntityId(sim, 0), "无来源 → 0");
        }

        [Test]
        public void 摘要分类_四种与破盾判定()
        {
            var absorb = new SimEvent(1, SimEventType.SettledDamage, 1) { ShieldBefore = 20, ShieldAfter = 5, HpBefore = 100, HpAfter = 100 };
            Assert.AreEqual(VfxSourceRules.SettledKind.ShieldAbsorb, VfxSourceRules.Classify(absorb));
            var broke = new SimEvent(1, SimEventType.SettledDamage, 1) { ShieldBefore = 5, ShieldAfter = 0, HpBefore = 100, HpAfter = 95 };
            Assert.AreEqual(VfxSourceRules.SettledKind.ShieldBreak, VfxSourceRules.Classify(broke));
            var pure = new SimEvent(1, SimEventType.SettledDamage, 1) { ShieldBefore = 0, ShieldAfter = 0, HpBefore = 100, HpAfter = 90 };
            Assert.AreEqual(VfxSourceRules.SettledKind.Damage, VfxSourceRules.Classify(pure));
            var death = new SimEvent(1, SimEventType.SettledDamage, 1) { ShieldBefore = 0, ShieldAfter = 0, HpBefore = 10, HpAfter = -2, Died = true };
            Assert.AreEqual(VfxSourceRules.SettledKind.Death, VfxSourceRules.Classify(death));
        }

        [Test]
        public void 一击去重_同tick同攻击者只出一次()
        {
            var dedup = new VfxSwingDedup();
            Assert.IsTrue(dedup.FirstOfSwing(7, 100), "首次出招");
            Assert.IsFalse(dedup.FirstOfSwing(7, 100), "同 tick 溅射第二受害者不再出招");
            Assert.IsTrue(dedup.FirstOfSwing(8, 100), "另一攻击者独立");
            Assert.IsTrue(dedup.FirstOfSwing(7, 110), "下一 tick 重新可出");
        }

        [Test]
        public void 可见性闸_己方恒见敌远不可见弹道按段()
        {
            var sim = NewSim();
            for (int i = 0; i < 3; i++) sim.Step();   // 视野需要 VisionSystem 至少跑一拍才标记 Fresh
            var own = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 10));
            var foeFar = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 200));
            Assert.IsTrue(VfxVisibility.ShowAttached(sim, Side.Blue, own), "己方单位恒可见");
            Assert.IsFalse(VfxVisibility.ShowAttached(sim, Side.Blue, foeFar), "远处敌军(基地视野40外)不可见");

            // 弹道:己方基地附近两点全程可见;射向远处雾区应被裁
            float near = VfxVisibility.BeamVisibleFraction(sim, Side.Blue,
                new SimVec2(10.5f, 80.5f), new SimVec2(20.5f, 80.5f));
            Assert.Greater(near, 0.99f, "基地视野内弹道全程可见");
            float intoFog = VfxVisibility.BeamVisibleFraction(sim, Side.Blue,
                new SimVec2(10.5f, 80.5f), new SimVec2(160.5f, 80.5f));
            Assert.Less(intoFog, 0.9f, "射向雾区的弹道应被裁短(分段采样)");
        }
    }
}
