using System.Linq;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>2026-09-17 制作人:"20m 内友军在攻击目标时,如果自身附近没有敌人,就自动去打。"
    /// 场景:蓝射手在打远处红兵(超出蓝近战视野),蓝近战应通过规则 3b 参战(NearestAttackingAlly)。</summary>
    public sealed class AllyCombatSeekTests
    {
        private static GameConfig _cfg;

        private static SimCore NewSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig { AutoBehavior = true }, new SeededRng(11)) { Game = _cfg };
            sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(80, 8));
            sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(80, 262));
            return sim;
        }

        [Test]
        public void T1_射手在打_近战超出视野时自动参战()
        {
            var sim = NewSim();

            // 蓝射手(射程 8)在 (100,80) 打红兵在 (106,80) —— 距射手 6,在射程内
            var archer = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 100));
            archer.Hero = HeroId.ZhuiRi; archer.Attack = 5f; archer.AttackIntervalSec = 1f;
            archer.Range = 8f; archer.MoveSpeed = 0f; archer.Hp = archer.MaxHp = 400f;

            var redTarget = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 106));
            redTarget.Attack = 1f; redTarget.AttackIntervalSec = 1f; redTarget.Range = 2f;
            redTarget.MoveSpeed = 0f; redTarget.Hp = redTarget.MaxHp = 2000f;   // 高血量保证活到近战到位

            // 蓝磐石(近战,视野 8)在 (85,80) —— 距红兵 21 米(远超视野 8),距射手 15 米(≤20 支援范围)
            var melee = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 85));
            melee.Hero = HeroId.PanShi; melee.Attack = 8f; melee.AttackIntervalSec = 1f;
            melee.Range = 2f; melee.MoveSpeed = 1f; melee.Hp = melee.MaxHp = 400f;

            // 跑 15 秒:射手在打(SecondsSinceCombat=0)→ 磐石应被 3b 拉向战场
            float startDist = SimVec2.Distance(melee.PosF, redTarget.PosF);
            for (int t = 0; t < 150; t++) sim.Step();
            float endDist = SimVec2.Distance(melee.PosF, sim.GetEntity(redTarget.Id)?.PosF ?? redTarget.PosF);

            Debug.Log($"起始距红兵 {startDist:F1}m → 15 秒后 {endDist:F1}m(应显著缩短)");
            Assert.Less(endDist, startDist - 3f,
                $"近战应向友军正在打的敌人移动(起始 {startDist:F1} → 终 {endDist:F1},缩短不足 3 米)");
        }

        [Test]
        public void T2_视野内有敌时优先直接索敌_不走3b()
        {
            var sim = NewSim();

            // 蓝射手在 (100,80) 打远敌(106,80);蓝近战在 (98,80)——距近敌(99,80)仅 1 米(视野内)
            var archer = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 100));
            archer.Hero = HeroId.ZhuiRi; archer.Attack = 5f; archer.AttackIntervalSec = 1f;
            archer.Range = 8f; archer.MoveSpeed = 0f; archer.Hp = archer.MaxHp = 400f;

            var farEnemy = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 106));
            farEnemy.Attack = 1f; farEnemy.MoveSpeed = 0f; farEnemy.Hp = farEnemy.MaxHp = 2000f;

            var nearEnemy = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 99));
            nearEnemy.Attack = 1f; nearEnemy.MoveSpeed = 0f; nearEnemy.Hp = nearEnemy.MaxHp = 2000f;

            var melee = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 98));
            melee.Hero = HeroId.PanShi; melee.Attack = 8f; melee.AttackIntervalSec = 1f;
            melee.Range = 2f; melee.MoveSpeed = 1f; melee.Hp = melee.MaxHp = 400f;

            for (int t = 0; t < 30; t++) sim.Step();
            // 近敌在近战旁边(距 1 米),应直接被攻击(规则 3 或 CombatUpdate)
            Assert.Less(nearEnemy.Hp, nearEnemy.MaxHp,
                "视野内有敌(距 1 米)应直接攻击,不被 3b 拉去远处");
        }

        [Test]
        public void T3_友军不在打时_3b不触发_原地待命()
        {
            var sim = NewSim();

            // 蓝射手和蓝近战都无敌可见 → 都不动
            var archer = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 100));
            archer.Hero = HeroId.ZhuiRi; archer.Attack = 5f; archer.MoveSpeed = 0f;
            archer.Hp = archer.MaxHp = 400f;

            var melee = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 85));
            melee.Hero = HeroId.PanShi; melee.Attack = 8f; melee.MoveSpeed = 1f;
            melee.Range = 2f; melee.Hp = melee.MaxHp = 400f;

            var posBefore = melee.PosF;
            for (int t = 0; t < 50; t++) sim.Step();
            float moved = SimVec2.Distance(posBefore, melee.PosF);
            Assert.Less(moved, 1.5f, $"无敌可见且友军不在打 → 不应移动(实际移动 {moved:F1} 米)");
        }
    }
}
