using System;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>六条克制链的场景级验证:摆位→跑 tick→断言结果。</summary>
    public sealed class CounterChainTests
    {
        private static GameConfig _cfg;

        [SetUp]
        public void Setup()
        {
            if (_cfg == null)
            {
                var ta = Resources.Load<TextAsset>("gameconfig");
                _cfg = GameConfigParser.FromJson(ta.text);
            }
        }

        private static Entity SpawnHero(SimCore sim, HeroId id, Side side, GridPos pos)
        {
            var c = _cfg.Heroes[(int)id];
            var e = sim.SpawnEntity(side, EntityKind.Unit, pos);
            e.Hero = id;
            e.MaxHp = c.MaxHp; e.Hp = c.MaxHp;
            e.Attack = c.Attack;
            e.AttackIntervalSec = (float)c.AttackIntervalSec;
            e.Range = c.Range;
            e.MoveSpeed = (float)c.MoveSpeed;
            if (c.Shield > 0) { e.Shield = c.Shield; e.ShieldMax = c.Shield; }
            return e;
        }

        private static SimCore NewSim()
        {
            var sim = new SimCore(new SimConfig(), new SeededRng(1));
            sim.Game = _cfg;
            return sim;
        }

        [Test]
        public void Chain1_ZhenYueBreaksPanShi_7HitsKill()
        {
            var sim = NewSim();
            var zy = SpawnHero(sim, HeroId.ZhenYue, Side.Blue, new GridPos(1, 1));
            var ps = SpawnHero(sim, HeroId.PanShi, Side.Red, new GridPos(1, 2));
            for (int i = 0; i < 61; i++) sim.Step(); // 4 击破盾(140=4×35)
            Assert.AreEqual(0f, ps.Shield, "第 4 次攻击破盾");
            Assert.AreEqual(80f, ps.Hp, "破盾前不扣血");
            for (int i = 0; i < 61; i++) sim.Step(); // 第 7 击(121)致死
            Assert.AreEqual(EntityState.Dead, ps.State, "镇岳 7 击击杀满盾磐石");
            // 2026-09-14 "同 tick 同时结算"落地后的语义:本 tick 被打死的单位**仍会打出它这一击**
            // (同时挥砍 → 互相致命会同归于尽;顺序不再决定生死)。所以磐石反打是 **7 击**(含与死亡同 tick 的那一下),
            // 原期望 6 击是"立即扣血"时代的产物。
            Assert.AreEqual(200f - 7 * 4.8f, zy.Hp, 0.01f, "磐石反打 7 击(含与本人阵亡同 tick 的一击)共 33.6 伤害,镇岳存活");
        }

        [Test]
        public void Chain2_PanShiShieldReducesNormalDamage()
        {
            var sim = NewSim();
            var df = SpawnHero(sim, HeroId.DuanFeng, Side.Blue, new GridPos(1, 1));
            var ps = SpawnHero(sim, HeroId.PanShi, Side.Red, new GridPos(1, 2));
            sim.Step();
            Assert.AreEqual(140f - 14f * DamageCalculator.PanShiShieldReduction, ps.Shield, 0.01f,
                "非破防攻击仅 70% 伤害:140-9.8=130.2");
        }

        [Test]
        public void Chain3_ZhuiRiPressesZhenYue_ArmorResetAndWound()
        {
            var sim = NewSim();
            var zy = SpawnHero(sim, HeroId.ZhenYue, Side.Blue, new GridPos(1, 1));
            var zr = SpawnHero(sim, HeroId.ZhuiRi, Side.Red, new GridPos(1, 2));
            Assert.AreEqual(7f, DamageCalculator.CalculateDamage(zr, zy, _cfg, false),
                "坠日攻击镇岳无视 20% 免伤:10×1.0×0.7=7");
            sim.Step();
            Assert.IsTrue(zy.Status.Has(DebuffType.Wound), "命中同步施加重伤");
            Assert.AreEqual(193f, zy.Hp, "镇岳 200-7");
        }

        [Test]
        public void Chain4_DuanFengInterceptsCharge_AndSlows()
        {
            var sim = NewSim();
            var zf = SpawnHero(sim, HeroId.ZhuFeng, Side.Blue, new GridPos(1, 1));
            var df = SpawnHero(sim, HeroId.DuanFeng, Side.Red, new GridPos(1, 3)); // 距离 2,拒马范围
            zf.IsCharging = true;
            sim.Step(); // 拒马打断冲锋;断峰射程 2 命中迟滞
            Assert.IsFalse(zf.IsCharging, "拒马打断冲锋");
            Assert.IsTrue(zf.Status.Has(DebuffType.Slow), "断峰攻击附带迟滞");
        }

        [Test]
        public void Chain5_XingLuoSilencesZhuFeng_ChargeNeverHolds()
        {
            var sim = NewSim();
            var xl = SpawnHero(sim, HeroId.XingLuo, Side.Blue, new GridPos(1, 1));
            var zf = SpawnHero(sim, HeroId.ZhuFeng, Side.Red, new GridPos(1, 2));
            for (int i = 0; i < 3; i++)
            {
                zf.IsCharging = true;
                sim.Step();
                Assert.IsFalse(zf.IsCharging, "沉默期间冲锋无法维持");
                Assert.IsTrue(zf.Status.Has(DebuffType.Silence), "星落普攻持续沉默");
            }
        }

        [Test]
        public void Chain6_ZhuFengChargeSlicesRanged_TwoHitsKill()
        {
            var sim = NewSim();
            var zf = SpawnHero(sim, HeroId.ZhuFeng, Side.Blue, new GridPos(1, 1));
            var zr = SpawnHero(sim, HeroId.ZhuiRi, Side.Red, new GridPos(1, 2));
            zf.IsCharging = true;
            Assert.AreEqual(30f, DamageCalculator.CalculateDamage(zf, zr, _cfg, true),
                "冲锋翻倍:15×2=30");
            for (int i = 0; i < 22; i++) sim.Step(); // 2 击(1/21)30×2=60 ≥ 40
            Assert.AreEqual(EntityState.Dead, zr.State, "两击收割远程后排");
            Assert.Greater(zf.Hp, 0f, "逐风存活(坠日两击 25 < 70)");
        }
    }
}
