using System.Collections.Generic;
using System.Linq;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 制作人实机报两条 + 一条口径确认:
    /// ①「附近友军在打架,离得很近的队友不正常支援」→ 新口径:**20 格以内队友挨打,除非玩家有调度安排,
    ///   必须支援,写进最优先**(旧实现:15 格、只认"主将"在打架、且排在索敌进攻之后)。
    /// ②「小兵宁可不攻击,也要保持圆形阵型围住主将」→ 新口径:**警戒时围住主将,战斗时废除阵型,
    ///   优先保证可以攻击到敌人**。
    /// ③「视野内出现敌人、非战斗状态自动上去攻击」是游戏机制(保留),玩家要避免交战只能撤退 —— 由
    ///   `RetreatHold` 保证(玩家授意撤退后自动层让位)。
    /// </summary>
    public sealed class SupportAndFormationTests
    {
        private static GameConfig _cfg;
        private SimCore sim;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig { AutoBehavior = true }, new SeededRng(7));   // 实机开局即开自动层
            sim.Game = _cfg;
            SiegeSystem.Reset();
            AiSystem.Reset();
            VisionSystem.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SiegeSystem.Reset();
            AiSystem.Reset();
        }

        private Entity Regiment(Side side, HeroId hero, GridPos pos)
            => RegimentSystem.SpawnGeneral(sim, side, hero, pos);

        private static List<Entity> SoldiersOf(SimCore s, int generalId)
            => s.Entities.Values.Where(e => e.IsSoldier && e.GeneralId == generalId).ToList();

        private void Run(int ticks) { for (int i = 0; i < ticks; i++) sim.Step(); }

        [Test]
        public void T1_十八格外友军挨打_必须前来支援()
        {
            // 旧口径是 15 格 → 18 格外站着不动的兵团对"友军挨打"毫无反应
            var victim = Regiment(Side.Blue, HeroId.PanShi, new GridPos(80, 130));
            var enemy = Regiment(Side.Red, HeroId.PanShi, new GridPos(80, 134));   // 贴身开打
            Run(200);                                                             // 打起来(victim 进入交战)
            Assert.Less(victim.SecondsSinceCombat, 3f, "前置:victim 应在交战中");

            var helper = Regiment(Side.Blue, HeroId.PanShi, new GridPos(80, 112)); // 距 victim 18 格
            float dist = SimVec2.Distance(helper.PosF, victim.PosF);
            Assert.Greater(dist, 15f, "前置:距离要在旧口径 15 格之外(实际 " + dist.ToString("F1") + ")");
            Assert.LessOrEqual(dist, 20f, "前置:距离要在新口径 20 格之内(实际 " + dist.ToString("F1") + ")");

            var startDist = dist;
            bool came = false;
            for (int i = 0; i < 120 && !came; i++)
            {
                sim.Step();
                if (SimVec2.Distance(helper.PosF, victim.PosF) < startDist - 3f) came = true;
            }
            Assert.IsTrue(came, "20 格内友军挨打,近处队友必须前来支援(实测没动)");
        }

        [Test]
        public void T2_挨打的是孤兵也要支援()
        {
            // 旧口径只认"**主将**在打架"(`NearestFightingAlly` 直接跳过 IsSoldier)——
            // 现场里挨打的常常是后排散兵;这条兵离本方主将很远,但它在 20 格内挨打就必须有人来救。
            var lone = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 130));
            lone.Hero = HeroId.PanShi; lone.Hp = lone.MaxHp = 300f; lone.Attack = 8f;
            lone.AttackIntervalSec = 1f; lone.Range = 2f; lone.MoveSpeed = 3f; lone.IsSoldier = true;
            var enemy = Regiment(Side.Red, HeroId.PanShi, new GridPos(80, 133));   // 贴上来打它
            Run(120);
            Assert.Less(lone.SecondsSinceCombat, 3f, "前置:这名孤兵应在挨打");

            var helper = Regiment(Side.Blue, HeroId.PanShi, new GridPos(80, 116));  // 其主将不在此处,距离约 14 格
            var startDist = SimVec2.Distance(helper.PosF, lone.PosF);
            Assert.LessOrEqual(startDist, 20f, "前置:距离在 20 格内");
            bool came = false;
            for (int i = 0; i < 150 && !came; i++)
            {
                sim.Step();
                if (SimVec2.Distance(helper.PosF, lone.PosF) < startDist - 3f) came = true;
            }
            Assert.IsTrue(came, "挨打的是孤兵(附近没有它的主将)时也必须来支援(实测没动)");
        }

        [Test]
        public void T3_玩家有调度安排时不自动支援()
        {
            var victim = Regiment(Side.Blue, HeroId.PanShi, new GridPos(80, 130));
            Regiment(Side.Red, HeroId.PanShi, new GridPos(80, 134));
            Run(200);
            Assert.Less(victim.SecondsSinceCombat, 3f, "前置:victim 应在交战中");

            var helper = Regiment(Side.Blue, HeroId.PanShi, new GridPos(80, 114));  // 在 20 格内
            var away = new GridPos(80, 150);                                        // 玩家把它调到反方向
            sim.SetMoveTarget(helper.Id, away, true);
            Run(60);
            Assert.AreEqual(0, helper.AutoKind, "玩家已调度这个兵团 → 自动层不得再给它派支援令");
            Assert.AreEqual(away, helper.MoveTarget, "玩家的移动目标应保持不动");
            Assert.Greater(helper.PosF.X, SimVec2.FromGrid(new GridPos(80, 114)).X,
                "它应该继续朝玩家指定的方向走(而不是被拉去支援)");
        }

        [Test]
        public void T4_交战时小兵离开圆阵去攻击_警戒时才围主将()
        {
            // 制作人:"小兵宁可不攻击,也要保持圆形阵型围住主将" → 战斗时废除阵型,优先打到人。
            // 构造:把本方**主将攻击设为 0**、只留士兵能输出 → "敌人掉没掉血"就是**纯士兵信号**。
            // 敌人放在我方主将身边(圆阵半径 2.6 米 → 旧实现在圆上的士兵离它约 2.6 米、够不到 2 米近战射程,
            // 于是一圈人围着不打;新实现应离开圆阵贴上去打)。
            var blue = Regiment(Side.Blue, HeroId.PanShi, new GridPos(80, 128));
            foreach (var s in SoldiersOf(sim, blue.Id)) s.Attack = 10f;   // 只让士兵能打
            blue.Attack = 0f;
            // 靶子只打我方主将(让我方进入"交战中"状态),它的血只可能被我方**士兵**打掉(我方主将攻击=0)
            var dummy = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 130));
            dummy.Hero = HeroId.PanShi; dummy.Hp = dummy.MaxHp = 4000f;
            dummy.Attack = 5f; dummy.AttackIntervalSec = 1f; dummy.MoveSpeed = 0f; dummy.Range = 3f;
            float hp0 = dummy.Hp;
            Run(180);                                                     // 6 秒(我方自动索敌会压上去)
            Assert.Less(dummy.Hp, hp0 - 1f,
                "交战中士兵必须能打到敌人(旧实现在圆阵上够不到,实测伤害 " + (hp0 - dummy.Hp).ToString("F1") + ")");

            // 警戒(未交战)时仍应围住主将
            var idle = Regiment(Side.Blue, HeroId.PanShi, new GridPos(20, 200));
            Run(120);
            var idleSoldiers = SoldiersOf(sim, idle.Id);
            int near = 0;
            foreach (var s in idleSoldiers)
                if (SimVec2.Distance(s.PosF, idle.PosF) <= 4.5f) near++;
            Assert.GreaterOrEqual(near, idleSoldiers.Count - 1,
                "警戒/非战斗时士兵应继续围在主将身边(圆阵保留),实测 " + near + "/" + idleSoldiers.Count);
        }

        [Test]
        public void T5_玩家授意撤退后不再自动接战()
        {
            // 制作人:"视野内出现敌人、非战斗状态自动上去攻击"是机制;**玩家要避免这种战斗只能撤退**。
            var g = Regiment(Side.Blue, HeroId.PanShi, new GridPos(80, 120));
            Regiment(Side.Red, HeroId.PanShi, new GridPos(80, 126));   // 视野内
            Run(30);
            Assert.IsTrue(g.AutoKind == 1 || g.State == EntityState.Attacking,
                "前置:视野内出现敌人应自动上去打(AutoKind=" + g.AutoKind + " 态=" + g.State + ")");

            var atEngage = g.PosF.X;
            sim.SetMoveTarget(g.Id, new GridPos(80, 100), true);       // 玩家授意撤退(反方向)
            for (int i = 0; i < 10; i++) { g.RetreatHold = true; sim.Step(); }   // 撤退压制(与 RetreatSystem 同源)
            Assert.AreEqual(0, g.AutoKind, "玩家授意撤退后自动层应让位");
            Assert.Less(g.PosF.X, atEngage, "撤退后应往反方向走,而不是自己压上去");
        }
    }
}
