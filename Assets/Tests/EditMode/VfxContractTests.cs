using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>2026-09-17 特效契约落地验证(M0 契约表 §3,异构审核 R1→R2 批准的四条)。
    /// 只验"事件/查询的语义与批准口径一致",不含任何表现。</summary>
    public sealed class VfxContractTests
    {
        private static GameConfig _cfg;

        private static SimCore NewSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            return new SimCore(new SimConfig(), new SeededRng(11)) { Game = _cfg };
        }

        private static void Step(SimCore sim, int n) { for (int i = 0; i < n; i++) sim.Step(); }

        [Test]
        public void 契约1_SettledDamage_盾吸收与死亡标记与无净变化不发()
        {
            var sim = NewSim();
            var events = new List<SimEvent>();
            sim.AddEventListener(events.Add);

            // 受害者:无英雄被动干扰的普通单位,直接给盾字段(契约验证的是结算摘要,不是盾的来源)
            var victim = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 100));
            victim.Hp = victim.MaxHp = 200f; victim.Shield = 20f;
            victim.Attack = 0f; victim.MoveSpeed = 0f;
            var attacker = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 101));
            attacker.Attack = 15f; attacker.AttackIntervalSec = 1f; attacker.Range = 2f; attacker.MoveSpeed = 0f;

            events.Clear();
            Step(sim, 12);   // 约 1 秒:若干次命中,每次盾吸收/掉血都应有摘要

            var settled = events.Where(e => e.Type == SimEventType.SettledDamage).ToList();
            Assert.IsNotEmpty(settled, "盾或血有净变化时应发 SettledDamage");
            foreach (var s in settled)
            {
                Assert.GreaterOrEqual(s.ShieldBefore - s.ShieldAfter, -1e-4f, "盾只减不增");
                Assert.AreEqual(s.ShieldBefore - s.ShieldAfter, s.AbsorbedByShield, 1e-4f, "AbsorbedByShield 派生一致");
                if (!s.Died && s.ShieldBefore - s.ShieldAfter < 1e-4f)
                    Assert.Greater(s.HpBefore - s.HpAfter, 0.0001f, "盾无变化且未死亡时,血必有净下降(否则不该发;盾全额吸收是合法摘要)");
            }
            // 盾被打空的那一条:ShieldBefore>0 且 ShieldAfter==0
            Assert.IsTrue(settled.Any(s => s.ShieldBefore > 0f && s.ShieldAfter <= 0f), "应存在'破盾'那一条摘要");

            // 死亡标记:把受害者压到 1 血,再挨一刀
            victim.Hp = 1f; victim.Shield = 0f;
            events.Clear();
            Step(sim, 15);
            var death = events.FirstOrDefault(e => e.Type == SimEventType.SettledDamage && e.Died);
            Assert.IsNotNull(death, "致死那 tick 的摘要应带 Died=true");
            Assert.LessOrEqual(death.HpAfter, 0f, "死亡摘要的 HpAfter ≤ 0");
            var diedIdx = events.IndexOf(death);
            var entityDiedIdx = events.FindIndex(e => e.Type == SimEventType.EntityDied && e.SubjectId == victim.Id);
            Assert.Greater(entityDiedIdx, -1, "EntityDied 也应发出");
            Assert.Less(diedIdx, entityDiedIdx, "契约口径:SettledDamage 必须先于 EntityDied");

            // 无净变化不发:零攻攻击者打满血新目标
            var dummy = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 98));
            dummy.Hp = dummy.MaxHp = 100f; dummy.Attack = 0f; dummy.MoveSpeed = 0f; dummy.Shield = 0f;
            var zeroAtk = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 97));
            zeroAtk.Attack = 0f; zeroAtk.AttackIntervalSec = 1f; zeroAtk.Range = 2f; zeroAtk.MoveSpeed = 0f;
            events.Clear();
            Step(sim, 12);
            Assert.IsEmpty(events.Where(e => e.Type == SimEventType.SettledDamage && e.SubjectId == dummy.Id),
                "盾血均无净变化不得发摘要(职责分工:交给 DamageDealt)");
        }

        [Test]
        public void 契约2_FortPulsed_每次脉冲带堡垒id且伤害路径不变()
        {
            var sim = NewSim();
            var events = new List<SimEvent>();
            sim.AddEventListener(events.Add);
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(80, 8));
            sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 180));
            sim.SpawnBuilding(Side.Blue, BuildingId.ResourcePoint, new GridPos(80, 1));   // 蓝方在其后,不劫持最前判定
            for (int i = 0; i < 905; i++) sim.Step();   // EP
            Assert.IsTrue(ToolsSystem.UseFort(sim, Side.Blue, new GridPos(80, 30), out _), "摆一个临时阵地(基地视野内)");

            var foe = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 31));
            foe.Hp = foe.MaxHp = 100f; foe.Attack = 0f; foe.MoveSpeed = 0f;
            float hp0 = foe.Hp;

            events.Clear();
            Step(sim, 32);   // 3 秒+:恰好一次脉冲
            var pulses = events.Where(e => e.Type == SimEventType.FortPulsed).ToList();
            Assert.IsNotEmpty(pulses, "脉冲周期到应发 FortPulsed");
            var fort = sim.Entities.Values.First(e => e.IsFort);
            Assert.IsTrue(pulses.All(p => p.SubjectId == fort.Id), "SubjectId=堡垒实体 id");
            Assert.AreEqual(1, pulses.Count, "3.2 秒内一次脉冲一个事件");
            Assert.AreEqual(hp0 - sim.Game.Tools.RamDamage, foe.Hp, 0.01f, "伤害路径不变:仍直接扣血");
            // 互不覆盖口径:脉冲伤害不出现在 SettledDamage 流里
            Assert.IsEmpty(events.Where(e => e.Type == SimEventType.SettledDamage && e.SubjectId == foe.Id),
                "脉冲不经聚合 → SettledDamage 不得覆盖它(契约钉死的分工)");
        }

        [Test]
        public void 契约3_重装资格与激活戳()
        {
            var sim = NewSim();
            var pan = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 10));
            pan.Hero = HeroId.PanShi;
            var duan = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 11));
            duan.Hero = HeroId.DuanFeng;
            var soldier = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 12));
            soldier.IsSoldier = true; soldier.GeneralId = pan.Id;
            Assert.IsTrue(HexSystem.IsHeavyRegimentOf(sim, pan), "磐石=重装");
            Assert.IsTrue(HexSystem.IsHeavyRegimentOf(sim, soldier), "重装主将的士兵随军团(军团口径)");
            Assert.IsFalse(HexSystem.IsHeavyRegimentOf(sim, duan), "断峰非重装(反例)");

            // 激活戳:激活前为 -1(Q08:与"tick0 激活"区分);释放后=当时 tick;换卡再激活会变。
            // 集卡走训练场同款自由抽取(HexFreeDraw),定向抽到 SteelTide+HiddenPath(不碰 internal 状态)。
            Assert.AreEqual(-1, HexSystem.ActivationStampOf(sim, Side.Blue), "未激活为 -1(Q08 修复)");
            sim.HexFreeDraw = true;
            sim.EpCapOverride = 99;
            Step(sim, 2);
            for (int i = 0; i < 60 && !(HexSystem.HandOf(sim, Side.Blue).Contains(HexId.SteelTide)
                && HexSystem.HandOf(sim, Side.Blue).Contains(HexId.HiddenPath)); i++)
            {
                var d = HexSystem.TryDraw(sim, Side.Blue);
                if (!d.Success) break;
                foreach (var want in new[] { HexId.SteelTide, HexId.HiddenPath })
                    if (d.Choices.Contains(want) && !HexSystem.HandOf(sim, Side.Blue).Contains(want))
                    { HexSystem.TryChoose(sim, Side.Blue, want); break; }
                if (HexSystem.PendingChoicesOf(sim, Side.Blue).Count > 0)
                    HexSystem.TryChoose(sim, Side.Blue, HexSystem.PendingChoicesOf(sim, Side.Blue)[0]);
                Step(sim, 1);
            }
            Assert.IsTrue(HexSystem.HandOf(sim, Side.Blue).Contains(HexId.SteelTide)
                && HexSystem.HandOf(sim, Side.Blue).Contains(HexId.HiddenPath), "自由抽取应能集齐两张目标卡");
            Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.SteelTide));
            long stamp1 = HexSystem.ActivationStampOf(sim, Side.Blue);
            Assert.AreEqual(sim.Tick, stamp1, "激活戳=激活发生的 tick");
            Step(sim, 7);
            Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.HiddenPath));
            Assert.AreNotEqual(stamp1, HexSystem.ActivationStampOf(sim, Side.Blue), "再激活刷新戳(非稳定标识,契约已注明)");
        }

        [Test]
        public void 契约4_PlacementBlocked公开与reason常量单一来源()
        {
            var sim = NewSim();
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(80, 8));
            string reason;
            Assert.IsTrue(ToolsSystem.PlacementBlocked(sim, Side.Blue, new GridPos(40, 135), out reason), "水域阻断");
            Assert.AreEqual(ToolsSystem.PlacementReason.Water, reason, "reason 走常量,不是散落字面量");
            Assert.IsTrue(ToolsSystem.PlacementBlocked(sim, Side.Blue, new GridPos(60, 60), out reason), "草坪(非路非广场)阻断");
            Assert.AreEqual(ToolsSystem.PlacementReason.NotLaneOrPlaza, reason);
            Assert.IsFalse(ToolsSystem.PlacementBlocked(sim, Side.Blue, new GridPos(80, 30), out reason), "中路视野内合法");
        }
    }
}
