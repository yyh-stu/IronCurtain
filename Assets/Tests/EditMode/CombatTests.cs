using System;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;
using IronCurtain.Tests;

namespace IronCurtain.Tests
{
    public sealed class CombatTests
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

        private static HeroCfg Hero(HeroId id)
        {
            foreach (var h in _cfg.Heroes) if (h.Id == id) return h;
            throw new InvalidOperationException("hero not found: " + id);
        }

        /// <summary>按配置生成英雄单位并赋值战斗属性。</summary>
        private static Entity SpawnHero(SimCore sim, HeroId id, Side side, GridPos pos)
        {
            var c = Hero(id);
            var e = sim.SpawnEntity(side, EntityKind.Unit, pos);
            e.Hero = id;
            e.MaxHp = c.MaxHp;
            e.Hp = c.MaxHp;
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

        // ===== GDD 7.1.1 五例演算 =====

        [Test]
        public void GddExample1_ZhenYueVsPanShi_ShieldBreaksAt4th_Overflow35()
        {
            var sim = NewSim();
            var zy = SpawnHero(sim, HeroId.ZhenYue, Side.Blue, new GridPos(15, 109));
            var ps = SpawnHero(sim, HeroId.PanShi, Side.Red, new GridPos(15, 110));
            // 攻击间隔 2s = 20 tick;第 1 tick 即出手,出手时刻 1/21/41/61/81
            for (int i = 0; i < 62; i++) sim.Step();
            Assert.AreEqual(0f, ps.Shield, "第 4 次攻击破盾(140 = 4×35)");
            Assert.AreEqual(80f, ps.Hp, "破盾前不扣血");
            for (int i = 0; i < 20; i++) sim.Step();
            Assert.AreEqual(45f, ps.Hp, "第 5 次溢出 35 扣血,80-35=45");
        }

        [Test]
        public void GddExample2_ZhuiRiVsZhenYue_7PerHit()
        {
            var sim = NewSim();
            var zr = SpawnHero(sim, HeroId.ZhuiRi, Side.Blue, new GridPos(15, 109));
            var zy = SpawnHero(sim, HeroId.ZhenYue, Side.Red, new GridPos(15, 110));
            Assert.AreEqual(7f, DamageCalculator.CalculateDamage(zr, zy, _cfg, false), "10×1.0×0.7=7");
            zy.Attack = 0f; // 关闭镇岳还手,隔离被测变量
            for (int i = 0; i < 20; i++) sim.Step(); // 第 1 tick 出手,第 2 击在第 21 tick
            Assert.AreEqual(193f, zy.Hp, "200 - 7");
        }

        [Test]
        public void GddExample3_ZhuFengChargeVsDuanFeng_30PerHit_ThreeHitsKill()
        {
            var sim = NewSim();
            var zf = SpawnHero(sim, HeroId.ZhuFeng, Side.Blue, new GridPos(15, 109));
            var df = SpawnHero(sim, HeroId.DuanFeng, Side.Red, new GridPos(15, 128)); // 远离,拒马不触发
            Assert.AreEqual(30f, DamageCalculator.CalculateDamage(zf, df, _cfg, true), "15×2.0=30(冲锋,公式层)");
            Assert.AreEqual(15f, DamageCalculator.CalculateDamage(zf, df, _cfg, false), "非冲锋 15");

            // 场景:无克制中性靶(断峰有拒马,真实引擎中冲锋接触前即被打断,故用靶验证击杀节奏)
            var dummy = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 110));
            dummy.MaxHp = 80f; dummy.Hp = 80f;
            dummy.Attack = 0f; dummy.AttackIntervalSec = 999f; dummy.Range = 1f;
            zf.IsCharging = true;
            for (int i = 0; i < 45; i++) sim.Step();
            Assert.AreEqual(EntityState.Dead, dummy.State, "3 次冲锋击杀(90 ≥ 80)");
        }

        [Test]
        public void GddExample4_XingLuoVsPanShiShield_2_5()
        {
            var sim = NewSim();
            var xl = SpawnHero(sim, HeroId.XingLuo, Side.Blue, new GridPos(15, 109));
            var ps = SpawnHero(sim, HeroId.PanShi, Side.Red, new GridPos(15, 110));
            Assert.AreEqual(2.5f, DamageCalculator.CalculateDamage(xl, ps, _cfg, false), "5×0.5=2.5");
        }

        [Test]
        public void GddExample5_ZhuiRiVsZhuFeng_12_5_SixHitsKill()
        {
            var sim = NewSim();
            var zr = SpawnHero(sim, HeroId.ZhuiRi, Side.Blue, new GridPos(15, 109));
            var zf = SpawnHero(sim, HeroId.ZhuFeng, Side.Red, new GridPos(15, 112)); // 逐风射程 2(有效 2.3),距 3 打不到坠日;坠日射程 9 覆盖
            Assert.AreEqual(12.5f, DamageCalculator.CalculateDamage(zr, zf, _cfg, false), "10×1.25=12.5");
            // 攻击间隔 2s:5 次攻击 62.5 < 70 存活;第 6 次(第 101 tick)致死
            for (int i = 0; i < 100; i++) sim.Step();
            Assert.AreEqual(EntityState.Attacking, zr.State, "坠日(攻击方)应处于攻击状态");
            Assert.Greater(zf.Hp, 0f, "5 次后仍存活");
            Assert.IsTrue(TestUtil.RunUntil(sim, () => zf.State == EntityState.Dead, 10), "第 6 次击杀");
        }

        // ===== 战斗框架 =====

        [Test]
        public void Cooldown_AttacksAtInterval()
        {
            var sim = NewSim();
            var a = SpawnHero(sim, HeroId.ZhenYue, Side.Blue, new GridPos(15, 109));
            var d = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 110));
            d.Hero = HeroId.PanShi; d.MaxHp = 10000f; d.Hp = 10000f;
            d.Attack = 0f; d.AttackIntervalSec = 999f; d.Range = 1f;
            int hits = 0;
            sim.AddEventListener(e => { if (e.Type == SimEventType.DamageDealt) hits++; });
            for (int i = 0; i < 100; i++) sim.Step();
            Assert.AreEqual(5, hits, "间隔 2s=20tick,100tick 内 5 次出手");
        }

        [Test]
        public void Death_RemovesEntity_AndEmitsDied()
        {
            var sim = NewSim();
            var a = SpawnHero(sim, HeroId.ZhenYue, Side.Blue, new GridPos(15, 109));
            var d = SpawnHero(sim, HeroId.DuanFeng, Side.Red, new GridPos(15, 110));
            int died = 0;
            sim.AddEventListener(e => { if (e.Type == SimEventType.EntityDied) died++; });
            // 镇岳 35/击(破防...断峰非磐石:14/击),断峰 80 血 → 6 击死(84)
            for (int i = 0; i < 130; i++) sim.Step();
            Assert.AreEqual(1, died);
            Assert.AreEqual(1, sim.EntityCount, "死者已移除");
            Assert.IsNull(sim.GetEntity(d.Id));
        }

        [Test]
        public void NearestTarget_ChosenByDistance_ThenId()
        {
            var sim = NewSim();
            var a = SpawnHero(sim, HeroId.ZhuiRi, Side.Blue, new GridPos(15, 109)); // 射程 9
            var far = SpawnHero(sim, HeroId.DuanFeng, Side.Red, new GridPos(15, 113)); // 距离 4
            var near = SpawnHero(sim, HeroId.DuanFeng, Side.Red, new GridPos(15, 111)); // 距离 2
            int nearHits = 0, farHits = 0;
            sim.AddEventListener(e =>
            {
                if (e.Type != SimEventType.DamageDealt) return;
                if (e.SubjectId == near.Id) nearHits++;
                if (e.SubjectId == far.Id) farHits++;
            });
            for (int i = 0; i < 60; i++) sim.Step();
            Assert.Greater(nearHits, 0);
            Assert.AreEqual(0, farHits, "优先打最近目标");
        }

        [Test]
        public void MovingUnit_StopsWhenEnemyInRange()
        {
            var sim = NewSim();
            var a = SpawnHero(sim, HeroId.ZhuiRi, Side.Blue, new GridPos(15, 109)); // 射程 9,移速 2
            var e = SpawnHero(sim, HeroId.DuanFeng, Side.Red, new GridPos(15, 138));
            e.Attack = 0f; e.AttackIntervalSec = 999f;
            sim.SetMoveTarget(a.Id, new GridPos(15, 138));
            for (int i = 0; i < 60; i++) sim.Step(); // 2格/s×6s=12格 → 到 (15,121),与敌距离 17 > 9
            Assert.AreEqual(EntityState.Moving, a.State);
            Assert.IsTrue(TestUtil.RunUntil(sim, () => a.State == EntityState.Attacking, 200),
                "进入射程(9)即接敌");
            Assert.IsFalse(a.HasMoveTarget, "接敌后清除移动目标");
        }
    }
}
