using System.Collections.Generic;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 制作人实机三条(收尾批):
    /// ①"两军在中路大桥交战,会边交战边位移…应该原地交战"(实测 蓝 6 军团 vs 红 3 军团,从大桥被推到上草坪)。
    /// ②"多军团交战时,射手尽量在近战后方"(原来射手会一路前压到和镇岳这种强近战贴身)。
    /// ③"射手攻击敌方建筑没有弹道"(根因:`DestroySystem` 扣建筑血时不发 `DamageDealt`)。
    /// </summary>
    public sealed class CombatPositionTests
    {
        private static GameConfig _cfg;

        private static SimCore NewAutoSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig { AutoBehavior = true }, new SeededRng(77));
            sim.Game = _cfg;
            return sim;
        }

        private static SimVec2 Centroid(SimCore sim, Side side)
        {
            float x = 0f, y = 0f; int n = 0;
            foreach (var e in sim.Entities.Values)
            {
                if (e == null || e.Kind != EntityKind.Unit || e.Side != side || e.State == EntityState.Dead) continue;
                x += e.PosF.X; y += e.PosF.Y; n++;
            }
            return n == 0 ? new SimVec2(0f, 0f) : new SimVec2(x / n, y / n);
        }

        [Test]
        public void 两军交战应原地打_不得边打边整体位移()
        {
            var sim = NewAutoSim();
            // 蓝 3 军团 vs 红 2 军团,面对面贴着(中路一带)
            for (int i = 0; i < 3; i++)
                RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(76 + i * 4, 140));
            for (int i = 0; i < 2; i++)
                RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(78 + i * 4, 146));

            for (int i = 0; i < 20 * sim.Config.TickRate; i++) sim.Step();   // 先让他们接战并稳定
            var blue0 = Centroid(sim, Side.Blue);
            var red0 = Centroid(sim, Side.Red);

            for (int i = 0; i < 30 * sim.Config.TickRate; i++) sim.Step();   // 再打 30 秒
            float blueMoved = SimVec2.Distance(blue0, Centroid(sim, Side.Blue));
            float redMoved = SimVec2.Distance(red0, Centroid(sim, Side.Red));

            // 允许小幅站位调整(阵型/推挤),但**不允许整体位移**:旧实现 30 秒会漂出十几米
            Assert.Less(blueMoved, 4f, "蓝方交战期间整体位移应可忽略(实测 " + blueMoved.ToString("F2") + "m)");
            Assert.Less(redMoved, 4f, "红方交战期间整体位移应可忽略(实测 " + redMoved.ToString("F2") + "m)");
        }

        [Test]
        public void 攻城伤害会发DamageDealt事件_这是弹道的来源()
        {
            var sim = NewSim();
            var target = sim.SpawnBuilding(Side.Red, BuildingId.Barracks, new GridPos(80, 120));
            // 蓝方射手站在射程内,带攻城令打这座建筑
            var shooter = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.XingLuo, new GridPos(80, 112));
            // DestroySystem 的前置:该建筑必须有本方下发的"摧毁"意图(游戏里就是点摧毁/右键)
            string ierr;
            Assert.IsTrue(SiegeSystem.SetIntent(sim, target.Id, Side.Blue, SiegeIntent.Destroy, out ierr),
                "前置:设摧毁意图 err=" + ierr);
            shooter.TargetBuildingId = target.Id;
            shooter.Range = 9f; shooter.Attack = 40f; shooter.AttackIntervalSec = 0.5f; shooter.AttackCooldown = 0f;

            int hits = 0, withAttacker = 0;
            sim.AddEventListener(ev =>
            {
                if (ev.Type != SimEventType.DamageDealt) return;
                if (ev.SubjectId != target.Id) return;
                hits++;
                if (ev.AttackerId == shooter.Id) withAttacker++;
            });
            for (int i = 0; i < 5 * sim.Config.TickRate; i++) sim.Step();

            Assert.Greater(hits, 0, "打建筑必须发 DamageDealt —— 否则表现层拿不到,射手打建筑没有弹道");
            Assert.Greater(withAttacker, 0, "事件要带攻击者 id,弹道才能从射手位置发出");
        }

        private static SimCore NewSim()
        {
            var sim = new SimCore(new SimConfig(), new SeededRng(78));
            sim.Game = _cfg ?? GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            return sim;
        }
    }
}
