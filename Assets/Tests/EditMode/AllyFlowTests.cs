using System.Collections.Generic;
using System.Linq;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 制作人实机报障:「多兵团交战时**后续支援会被友军卡住**」。
    /// 这里锁的是最根本的一条:**行军单位不能被己方单位永久堵住** ——
    /// 静止的友军、以及正在交战的友军(战线),后续兵团都要能走过去/绕过去。
    /// (旧实现在这里靠"互卡 2 秒开穿透窗口",实测触发口径写错、且换边后还有音源残留一类的半成品;
    ///  本轮改成**同阵营局部绕行**,见 `SimCore.SteerAroundAllies`。)
    /// </summary>
    public sealed class AllyFlowTests
    {
        private static GameConfig _cfg;
        private SimCore sim;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig(), new SeededRng(1));
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

        private Entity SpawnRegiment(Side side, HeroId hero, GridPos pos)
            => RegimentSystem.SpawnGeneral(sim, side, hero, pos);

        private static int AliveSoldiers(SimCore s, Entity g)
            => s.Entities.Values.Count(e => e.IsSoldier && e.GeneralId == g.Id);

        /// <summary>推进直到条件成立,返回用掉的秒数;超时返回 -1。</summary>
        private float RunUntilSeconds(System.Func<bool> cond, float maxSeconds)
        {
            int max = (int)(maxSeconds * sim.Config.TickRate);
            for (int i = 0; i < max; i++)
            {
                if (cond()) return i / (float)sim.Config.TickRate;
                sim.Step();
            }
            return cond() ? maxSeconds : -1f;
        }

        [Test]
        public void T1_穿过静止友军_后续兵团能到达()
        {
            // 友军(静止,不下任何命令)横在路中间
            var blocker = SpawnRegiment(Side.Blue, HeroId.PanShi, new GridPos(15, 104));
            // 后续兵团在它身后 16 格,目标在它前方 16 格(必须"走过去")
            var mover = SpawnRegiment(Side.Blue, HeroId.PanShi, new GridPos(15, 88));
            var goal = new GridPos(15, 120);
            sim.SetMoveTarget(mover.Id, goal, true);

            float sec = RunUntilSeconds(
                () => SimVec2.Distance(mover.PosF, SimVec2.FromGrid(goal)) < 2.5f, 40f);
            Assert.Greater(sec, -1f,
                "行军兵团被己方静止兵团堵住:40 秒内没到目标(实际停在 " + mover.PosF.X.ToString("F1")
                + "," + mover.PosF.Y.ToString("F1") + ")");
            Assert.IsNotNull(sim.GetEntity(blocker.Id), "被穿的友军应完好");
        }

        [Test]
        public void T2_战线上的友军不被打散_后续支援能挤进战线交战()
        {
            // 制作人现场:中路已有交战线(蓝红各一团正面交战、站定),后续支援团从后方赶到增援。
            // 判据:①支援**能挤进战线并和敌军接触**(而不是被自家战线顶在外面干等);
            //       ②自家战线**不该被支援推着位移**(交战原地是既定口径)。
            var blueLine = SpawnRegiment(Side.Blue, HeroId.PanShi, new GridPos(80, 130));
            var redLine = SpawnRegiment(Side.Red, HeroId.PanShi, new GridPos(80, 134));
            for (int i = 0; i < 200; i++) sim.Step();     // 打起来,站定
            var blueLineAt = blueLine.PosF;

            // 后续支援团:下令压到敌方战线位置(即"去增援、去接敌")
            var support = SpawnRegiment(Side.Blue, HeroId.PanShi, new GridPos(80, 116));
            var supportAt = support.PosF;
            sim.SetMoveTarget(support.Id, redLine.Pos, true);

            bool joined = false; float sec = -1f;
            int max = (int)(45f * sim.Config.TickRate);
            for (int i = 0; i < max; i++)
            {
                sim.Step();
                // 支援团里任一单位贴到任一敌军身边 = 挤进战线参战了
                foreach (var s in sim.Entities.Values)
                {
                    if (!(s.IsSoldier && s.GeneralId == support.Id) && s.Id != support.Id) continue;
                    foreach (var o in sim.Entities.Values)
                    {
                        if (o.Kind != EntityKind.Unit || o.Side != Side.Red) continue;
                        // 2026-09-14 新口径下,行军途中若有友军挨打(20 格内),它会**先奔向该友军**再开打,
                        // 所以判据取"进入战线范围"(3.5 米)——旧实现在这里停在 12.85 米外,区分度足够。
                        if (SimVec2.Distance(s.PosF, o.PosF) <= 3.5f) { joined = true; break; }
                    }
                    if (joined) break;
                }
                if (joined) { sec = i / (float)sim.Config.TickRate; break; }
            }
            Assert.IsTrue(joined,
                "支援团被己方战线卡住:45 秒都没能进入战线范围(支援停在 " + support.PosF.X.ToString("F1")
                + "," + support.PosF.Y.ToString("F1") + ",起点 " + supportAt.X.ToString("F1") + ")");

            // 战线本身不该被支援团推着位移
            Assert.Less(SimVec2.Distance(blueLine.PosF, blueLineAt), 4f,
                "战线上的友军被后续支援推着走(位移 " + SimVec2.Distance(blueLine.PosF, blueLineAt).ToString("F1") + " 格)");
            Assert.Greater(AliveSoldiers(sim, blueLine), 0, "自家战线应有存活士兵");
            Assert.IsNotNull(sim.GetEntity(redLine.Id), "敌方战线应仍在");
        }

        [Test]
        public void T4_三团混战中路的后续支援_能挤进战线接敌()
        {
            // **制作人现场的量级复现**(现场是"多兵团交战"):中路 3v3 打成一团(前线站位实测
            // X=128.5,三条横列 Y=76.5/80.5/84.5),支援团从身后 18 格赶到。旧实现在这里**永久卡死**
            // (实测:支援将停在 X=123.6、距最近敌 12.85 米,从第 9 秒到第 60 秒一动不动,状态还是
            //  Moving + 有令 —— 被自家前线堵住;连那条"互卡穿透窗口"也没救回来)。
            // 判据:支援团 25 秒内必须与敌军接触。
            for (int i = 0; i < 3; i++)
            {
                SpawnRegiment(Side.Blue, HeroId.PanShi, new GridPos(76 + i * 4, 128));
                SpawnRegiment(Side.Red, HeroId.PanShi, new GridPos(76 + i * 4, 136));
            }
            for (int i = 0; i < 300; i++) sim.Step();      // 打起来,站定

            var support = SpawnRegiment(Side.Blue, HeroId.PanShi, new GridPos(80, 110));
            var enemy = sim.Entities.Values
                .Where(e => e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == Side.Red)
                .OrderBy(e => System.Math.Abs(e.PosF.Y - support.PosF.Y)).First();
            sim.SetMoveTarget(support.Id, enemy.Pos, true);

            bool joined = false; float joinedAt = -1f;
            int max = (int)(25f * sim.Config.TickRate);
            for (int i = 0; i < max && !joined; i++)
            {
                sim.Step();
                foreach (var s in sim.Entities.Values)
                {
                    if (!(s.Id == support.Id || (s.IsSoldier && s.GeneralId == support.Id))) continue;
                    foreach (var o in sim.Entities.Values)
                    {
                        if (o.Kind != EntityKind.Unit || o.Side != Side.Red) continue;
                        // 2026-09-14 新口径下,行军途中若有友军挨打(20 格内),它会**先奔向该友军**再开打,
                        // 所以判据取"进入战线范围"(3.5 米)——旧实现在这里停在 12.85 米外,区分度足够。
                        if (SimVec2.Distance(s.PosF, o.PosF) <= 3.5f) { joined = true; break; }
                    }
                    if (joined) break;
                }
                if (joined) joinedAt = i / (float)sim.Config.TickRate;
            }
            Assert.IsTrue(joined, "后续支援被己方战线卡住:25 秒没能接敌(停在 "
                + support.PosF.X.ToString("F1") + "," + support.PosF.Y.ToString("F1") + ")");
            Assert.Less(joinedAt, 25f, "接敌太慢:" + joinedAt.ToString("F1") + " 秒");
        }

        [Test]
        public void T3_两团同向行军_后面那团不被堵死()
        {
            // 两团同向行军(前团先出发,后团 8 格跟上),后团必须能在 40 秒内到达同一目标
            var front = SpawnRegiment(Side.Blue, HeroId.PanShi, new GridPos(15, 92));
            var rear = SpawnRegiment(Side.Blue, HeroId.PanShi, new GridPos(15, 86));
            var goal = new GridPos(15, 118);
            sim.SetMoveTarget(front.Id, goal, true);
            sim.SetMoveTarget(rear.Id, goal, true);
            float sec = RunUntilSeconds(() => SimVec2.Distance(rear.PosF, SimVec2.FromGrid(goal)) < 3.5f, 45f);
            Assert.Greater(sec, -1f, "后团被前团堵死:45 秒内没到目标(停在 "
                + rear.PosF.X.ToString("F1") + "," + rear.PosF.Y.ToString("F1") + ")");
        }


        [Test]
        public void T5_互卡计数只算贴上的一对_隔远不算卡()
        {
            // 出厂旧码的触发口径**写错了**:`if (a.Side == b.Side) { AllyPushTicks++ }` 写在
            // **距离判定之前**,于是地图上任意两个同阵营单位(哪怕隔着几十米)每 tick 都在互相"计数" ——
            // 2 秒后整张图上的单位轮流拿到 0.8 秒穿透窗口,表现就是"推挤/穿透**交替闪烁**":
            // 既不是"互卡放行",也没人真被卡住过(卡住被闪烁掩盖了)。这条用例把这个口径钉死:
            // 隔 10 米、各自在行军的一对同阵营单位,**不得**被计成互卡。
            var a = SpawnRegiment(Side.Blue, HeroId.PanShi, new GridPos(15, 90));
            var b = SpawnRegiment(Side.Blue, HeroId.PanShi, new GridPos(15, 100));   // 相距 10 米
            sim.SetMoveTarget(a.Id, new GridPos(15, 118), true);
            sim.SetMoveTarget(b.Id, new GridPos(15, 128), true);
            for (int i = 0; i < 40; i++) sim.Step();
            Assert.AreEqual(0, a.AllyPushTicks, "隔着 10 米不该被计成'互卡'(a.AllyPushTicks=" + a.AllyPushTicks + ")");
            Assert.AreEqual(0, b.AllyPushTicks, "隔着 10 米不该被计成'互卡'(b.AllyPushTicks=" + b.AllyPushTicks + ")");
            Assert.AreEqual(0, a.GhostUntilTick, "没被卡住就不该拿到穿透窗口");
        }
    }
}
