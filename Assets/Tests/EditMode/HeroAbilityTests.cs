using System;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class HeroAbilityTests
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

        [Test]
        public void PanShi_ShieldRegen_After5sOutOfCombat()
        {
            var sim = NewSim();
            var zy = SpawnHero(sim, HeroId.ZhenYue, Side.Blue, new GridPos(1, 1));
            var ps = SpawnHero(sim, HeroId.PanShi, Side.Red, new GridPos(1, 2));
            sim.Step(); // 镇岳 35 全扣护盾:140-35=105
            Assert.AreEqual(105f, ps.Shield, "首击后护盾 105");
            sim.RemoveEntity(zy.Id);

            for (int i = 0; i < 48; i++) sim.Step(); // 脱战 4.8s
            Assert.AreEqual(105f, ps.Shield, "脱战未满 5 秒不回盾");

            for (int i = 0; i < 50; i++) sim.Step(); // 脱战满 5s 后 20/s 回盾
            Assert.AreEqual(140f, ps.Shield, "回盾至上限 140 封顶");
        }

        [Test]
        public void PanShi_BlocksCharge_Within1Cell()
        {
            var sim = NewSim();
            var zf = SpawnHero(sim, HeroId.ZhuFeng, Side.Blue, new GridPos(1, 1));
            var ps = SpawnHero(sim, HeroId.PanShi, Side.Red, new GridPos(1, 2)); // 距离 1
            zf.IsCharging = true;
            sim.Step();
            Assert.IsFalse(zf.IsCharging, "磐石 1 格内禁冲锋");
        }

        [Test]
        public void DuanFeng_BreaksCharge_Within2Cells()
        {
            var sim = NewSim();
            var zf = SpawnHero(sim, HeroId.ZhuFeng, Side.Blue, new GridPos(1, 1));
            var df = SpawnHero(sim, HeroId.DuanFeng, Side.Red, new GridPos(1, 3)); // 距离 2
            zf.IsCharging = true;
            sim.Step();
            Assert.IsFalse(zf.IsCharging, "断峰 2 格内拒马打断冲锋");

            var sim2 = NewSim();
            var zf2 = SpawnHero(sim2, HeroId.ZhuFeng, Side.Blue, new GridPos(1, 1));
            SpawnHero(sim2, HeroId.DuanFeng, Side.Red, new GridPos(1, 4)); // 距离 3
            zf2.IsCharging = true;
            sim2.Step();
            Assert.IsTrue(zf2.IsCharging, "3 格外不受拒马影响");
        }

        [Test]
        public void XingLuo_SilenceZhuFeng_AndClearsCharge()
        {
            var sim = NewSim();
            var xl = SpawnHero(sim, HeroId.XingLuo, Side.Blue, new GridPos(1, 1));
            var zf = SpawnHero(sim, HeroId.ZhuFeng, Side.Red, new GridPos(1, 2));
            sim.Step(); // 星落命中,沉默 2s(当 tick 即被逐风自身衰减 0.1)
            Assert.IsTrue(zf.Status.Has(DebuffType.Silence), "星落普攻施加沉默");
            sim.RemoveEntity(xl.Id); // 移除攻击方,观察沉默自然衰减

            for (int i = 0; i < 17; i++) sim.Step(); // 至 tick 18:剩余 2.0-1.8=0.2
            Assert.IsTrue(zf.Status.Has(DebuffType.Silence), "2 秒内沉默持续");
            zf.IsCharging = true;
            sim.Step(); // tick 19:衰减后仍剩 0.1,沉默仍在 → 冲锋被清除
            Assert.IsFalse(zf.IsCharging, "沉默期间冲锋被清除");

            for (int i = 0; i < 2; i++) sim.Step(); // 至 tick 21:2.0-2.0≤0
            Assert.IsFalse(zf.Status.Has(DebuffType.Silence), "沉默 2 秒后自然消失");
        }

        [Test]
        public void XingLuo_DoesNotSilenceZhenYue()
        {
            var sim = NewSim();
            var xl = SpawnHero(sim, HeroId.XingLuo, Side.Blue, new GridPos(1, 1));
            var zy = SpawnHero(sim, HeroId.ZhenYue, Side.Red, new GridPos(1, 2));
            sim.Step();
            Assert.IsFalse(zy.Status.Has(DebuffType.Silence), "星落不沉默镇岳");
        }

        [Test]
        public void DuanFeng_AppliesSlow_ExpiresIn3s()
        {
            var sim = NewSim();
            var df = SpawnHero(sim, HeroId.DuanFeng, Side.Blue, new GridPos(1, 1));
            var zy = SpawnHero(sim, HeroId.ZhenYue, Side.Red, new GridPos(1, 2));
            sim.Step(); // 断峰命中,迟滞 3s
            Assert.IsTrue(zy.Status.Has(DebuffType.Slow), "断峰攻击附带迟滞");
            Assert.AreEqual(0.7f, zy.Status.MoveMultiplier, "移速 -30%");
            Assert.AreEqual(1.1f, zy.Status.AttackIntervalMultiplier, "攻速 -10%");
            sim.RemoveEntity(df.Id);

            for (int i = 0; i < 28; i++) sim.Step(); // 共 29 tick:3.0-2.8=0.2
            Assert.IsTrue(zy.Status.Has(DebuffType.Slow), "3 秒内迟滞持续");
            for (int i = 0; i < 3; i++) sim.Step(); // 共 32 tick
            Assert.IsFalse(zy.Status.Has(DebuffType.Slow), "迟滞 3 秒后消失");
        }

        [Test]
        public void ZhuiRi_WoundZhenYue_ReducesOutput_ExpiresIn3s()
        {
            var sim = NewSim();
            var zy = SpawnHero(sim, HeroId.ZhenYue, Side.Blue, new GridPos(1, 1)); // id 1,先行动
            var zr = SpawnHero(sim, HeroId.ZhuiRi, Side.Red, new GridPos(1, 2));   // id 2
            float hit1 = 0f, hit21 = 0f, hit41 = 0f;
            sim.AddEventListener(e =>
            {
                if (e.Type != SimEventType.DamageDealt || e.SubjectId != zr.Id) return;
                if (e.Tick == 1) hit1 = e.Amount;
                if (e.Tick == 21) hit21 = e.Amount;
                if (e.Tick == 41) hit41 = e.Amount;
            });
            sim.Step(); // 镇岳先手(重伤尚未施加→14),坠日命中施加重伤(7)
            Assert.IsTrue(zy.Status.Has(DebuffType.Wound), "坠日攻击镇岳附加重伤");
            Assert.AreEqual(14f, hit1, 0.01f, "镇岳同 tick 先手,重伤尚未生效");

            zr.Attack = 0f; // 停止刷新重伤,但保留坠日作为镇岳的攻击目标
            for (int i = 0; i < 20; i++) sim.Step(); // 至 tick 21
            Assert.AreEqual(11.9f, hit21, 0.01f, "重伤期间输出 -15%:14×0.85");
            for (int i = 0; i < 11; i++) sim.Step(); // 至 tick 32:重伤 3s 已过期
            Assert.IsFalse(zy.Status.Has(DebuffType.Wound), "重伤 3 秒后消失");
            for (int i = 0; i < 9; i++) sim.Step(); // 至 tick 41
            Assert.AreEqual(14f, hit41, 0.01f, "重伤消失后伤害恢复");
        }

        [Test]
        public void Slow_ActuallyLengthensAttackInterval()
        {
            var sim = NewSim();
            var zr = SpawnHero(sim, HeroId.ZhuiRi, Side.Blue, new GridPos(1, 1)); // id 1,先出手,间隔 2s
            var df = SpawnHero(sim, HeroId.DuanFeng, Side.Red, new GridPos(1, 2)); // id 2,间隔 2.5s
            int thirdTick = 0, count = 0;
            sim.AddEventListener(e =>
            {
                if (e.Type != SimEventType.DamageDealt) return;
                if (e.SubjectId == df.Id) // 坠日打断峰
                {
                    count++;
                    if (count == 3) thirdTick = (int)e.Tick;
                }
            });
            for (int i = 0; i < 60; i++) sim.Step();
            Assert.AreEqual(43, thirdTick, "被迟滞后第 3 击延后至 43(2.2s 间隔:1/21/43)");
        }
    }
}
