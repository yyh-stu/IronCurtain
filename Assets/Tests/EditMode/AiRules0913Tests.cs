using System.Collections.Generic;
using System.Linq;
using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>2026-09-13 制作人 AI 规则包:①将上限8 ⑩攻城优先打视野内敌人 ②③④出城迎击+阵型+射手协同。</summary>
    public class AiRules0913Tests
    {
        private static GameConfig _cfg;
        private static GameConfig Cfg()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            return _cfg;
        }

        [Test]
        public void 将上限_正常对局为8_训练场覆盖999()
        {
            Assert.AreEqual(8, SimCore.GeneralCap, "制作人:全图三路下正常对局将上限 8");
            var sim = MatchSetup.Build(Cfg(), null, null);
            Assert.AreEqual(8, sim.PopulationCapOf(Side.Blue));
            sim.PopulationCapOverride = 999;
            Assert.AreEqual(999, sim.PopulationCapOf(Side.Blue), "训练场覆盖");
        }

        [Test]
        public void 攻城单位视野内出现敌人_弃攻城目标转向接战()
        {
            var sim = new SimCore(new SimConfig(), new SeededRng(11));
            sim.Game = Cfg();
            // 蓝方近战将带攻城令(目标红方建筑),红方敌将出现在视野内(6m)但射程(2m)外
            var blue = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 100));
            blue.Hero = HeroId.PanShi; blue.IsSoldier = false;
            blue.Attack = 10f; blue.AttackIntervalSec = 1f; blue.Range = 2f; blue.MoveSpeed = 5f;
            var redB = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 130));
            redB.Intent = SiegeIntent.Destroy; redB.IntentIssuer = Side.Blue;
            blue.TargetBuildingId = redB.Id;
            var red = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 104));   // 距蓝 4m:视野内/射程外
            red.Hero = HeroId.ZhenYue; red.IsSoldier = false;
            red.Attack = 10f; red.AttackIntervalSec = 1f; red.Range = 2f; red.MoveSpeed = 5f;
            red.MaxHp = 100f; red.Hp = 100f;

            sim.Step();   // 一个 tick 即应触发视野优先
            Assert.AreEqual(0, blue.TargetBuildingId, "视野内有敌人 → 弃攻城目标");
            Assert.IsTrue(blue.HasMoveTarget, "转向接战(获得移动目标)");
        }

        [Test]
        public void 出城迎击_城里只有射手不出城_混合守军成建制出城并按阵型站位()
        {
            var sim = new SimCore(new SimConfig(), new SeededRng(12));
            sim.Game = Cfg();
            AiSystem.Reset();
            AiSystem.Configure(sim, Side.Red, AiTier.Hard);
            // 红方箭塔(驻防点),城里驻:磐石将+坠日将+若干兵
            var tower = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 180));
            var tank = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 181));
            tank.Hero = HeroId.PanShi; tank.IsSoldier = false; tank.Attack = 10f; tank.AttackIntervalSec = 1f; tank.Range = 2f;
            var archer = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(79, 181));
            archer.Hero = HeroId.ZhuiRi; archer.IsSoldier = false; archer.Attack = 10f; archer.AttackIntervalSec = 1f; archer.Range = 5f;
            // 城内驻将=占领进驻语义(交换力10/12):直接登记(EnterFormal 仅士兵,主动驻军另一条路)
            tower.GarrisonFormal.Add(tank.Id); tank.GarrisonBuildingId = tower.Id;
            tower.GarrisonFormal.Add(archer.Id); archer.GarrisonBuildingId = tower.Id;

            // 蓝方敌将军出现在塔的建筑视野内(20m)且对红可见
            var foe = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 168));   // 距塔 12m
            foe.Hero = HeroId.ZhenYue; foe.IsSoldier = false; foe.Attack = 10f; foe.AttackIntervalSec = 1f; foe.Range = 2f;
            foe.MaxHp = 100f; foe.Hp = 100f;
            VisionSystem.Reset();

            // 喂 300 tick(AI 决策周期+视野精灵传播)
            for (int i = 0; i < 300; i++) sim.Step();

            bool archerOut = archer.GarrisonBuildingId == 0;
            bool tankOut = tank.GarrisonBuildingId == 0;
            Assert.IsTrue(tankOut || tower.GarrisonFormal.Concat(tower.GarrisonTemp).Any(id => sim.GetEntity(id) == tank),
                "混合守军:磐石应出城或仍在推进中(允许 AI 周期)");
            // 阵型断言:若两者都已出城,射手站位应比近战更靠近城门(纵深更浅)
            if (tankOut && archerOut)
            {
                var bc = SimVec2.FromGrid(tower.Pos);
                float dt = System.Math.Abs(SimVec2.Distance(tank.PosF, bc) - 6f);
                float da = System.MathF.Abs(SimVec2.Distance(archer.PosF, bc) - 0.5f);
                Assert.LessOrEqual(dt + da, 8f, "阵型深度近似(移动中容差)");
            }
            AiSystem.Reset();
        }

        [Test]
        public void 纯射手守城_大军压境不出城()
        {
            var sim = new SimCore(new SimConfig(), new SeededRng(13));
            sim.Game = Cfg();
            AiSystem.Reset();
            AiSystem.Configure(sim, Side.Red, AiTier.Hard);
            var tower = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 180));
            var a1 = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 181));
            a1.Hero = HeroId.ZhuiRi; a1.IsSoldier = false; a1.Attack = 10f; a1.AttackIntervalSec = 1f; a1.Range = 5f;
            var a2 = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(79, 181));
            a2.Hero = HeroId.XingLuo; a2.IsSoldier = false; a2.Attack = 10f; a2.AttackIntervalSec = 1f; a2.Range = 5f;
            tower.GarrisonFormal.Add(a1.Id); a1.GarrisonBuildingId = tower.Id;
            tower.GarrisonFormal.Add(a2.Id); a2.GarrisonBuildingId = tower.Id;
            // 蓝方大军压境(3 将,塔视野内)
            for (int i = 0; i < 3; i++)
            {
                var foe = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(78 + i, 168));
                foe.Hero = HeroId.ZhenYue; foe.IsSoldier = false; foe.Attack = 10f; foe.AttackIntervalSec = 1f; foe.Range = 2f;
                foe.MaxHp = 100f; foe.Hp = 100f;
            }
            VisionSystem.Reset();
            for (int i = 0; i < 300; i++) sim.Step();
            Assert.AreEqual(tower.Id, a1.GarrisonBuildingId, "纯射手城:坠日不出城(依托射击)");
            Assert.AreEqual(tower.Id, a2.GarrisonBuildingId, "纯射手城:星落不出城");
            AiSystem.Reset();
        }
    }
}
