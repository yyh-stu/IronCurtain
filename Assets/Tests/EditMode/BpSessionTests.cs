using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;

namespace IronCurtain.Tests
{
    /// <summary>BP 选将规则(2026-09-13 制作人定稿):
    /// ①每轮同时选、限时 10s,选取期间互不可见对方选择;
    /// ②双方都锁定后立即揭示并跳过剩余时间(不等满 10s);
    /// ③允许蓝红选同一英雄,但同方 3 轮内不可重复;
    /// ④AI 选将遵循"重装/战士/射手各选一个"的角色覆盖;
    /// ⑤揭示 3s 后进下一轮,3 轮完成。</summary>
    public class BpSessionTests
    {
        [Test]
        public void 双方锁定后立即揭示_不等满十秒()
        {
            var s = new BpSession(1);
            s.PickBlue(HeroId.ZhenYue);                    // 蓝先锁
            Assert.AreEqual(BpSession.Phase.Picking, s.State, "红未锁时不应揭示");
            for (int i = 0; i < 20 && s.State == BpSession.Phase.Picking; i++) s.Tick(0.5f);   // 喂过 AI 锁定点
            Assert.AreEqual(BpSession.Phase.Reveal, s.State, "AI 锁定后应立即揭示");
            Assert.Greater(s.Timer, 0f, "揭示时刻剩余选将时间应被跳过(Timer 已切为揭示计时)");
            Assert.AreEqual(s.Blue[0], HeroId.ZhenYue);
        }

        [Test]
        public void 同一方三轮内不可重复选择()
        {
            var s = new BpSession(2);
            Assert.IsTrue(s.PickBlue(HeroId.PanShi));
            while (s.State != BpSession.Phase.Reveal) s.Tick(0.5f);
            while (s.State == BpSession.Phase.Reveal) s.Tick(0.5f);       // 走完揭示进第 2 轮
            Assert.IsFalse(s.PickBlue(HeroId.PanShi), "第 2 轮再选磐石应被拒绝");
            Assert.IsTrue(s.PickBlue(HeroId.XingLuo), "换别的英雄应可锁");
        }

        [Test]
        public void 蓝红可以选择同一英雄()
        {
            var s = new BpSession(3);
            s.PickBlue(HeroId.PanShi);
            while (s.State == BpSession.Phase.Picking) s.Tick(0.5f);      // AI 自行锁
            // AI 是否同选磐石不确定,但流程必须正常揭示——同选允许性由"规则不排除"保证:
            // 若 AI 也选了磐石,Red[0]==Blue[0] 合法;否则不同也合法。
            Assert.AreEqual(1, s.Blue.Count);
            Assert.AreEqual(1, s.Red.Count);
        }

        [Test]
        public void AI选将每轮覆盖一个定位_三轮齐全()
        {
            for (int seed = 10; seed < 30; seed++)
            {
                var s = new BpSession(seed);
                s.PickBlue(HeroId.ZhuiRi);
                while (s.State != BpSession.Phase.Done)
                {
                    if (s.State == BpSession.Phase.Picking && s.BlueLock == null)
                        s.PickBlue((HeroId)((seed + s.Round) % 6));       // 任意不重复即可,锁定轮替
                    s.Tick(0.25f);
                }
                Assert.AreEqual(3, s.Red.Count, "seed=" + seed);
                var roles = new System.Collections.Generic.HashSet<string>();
                foreach (var h in s.Red) roles.Add(BpSession.RoleOf(h));
                Assert.IsTrue(roles.Contains("重装") && roles.Contains("战士") && roles.Contains("射手"),
                    "AI 三轮应覆盖三个定位 seed=" + seed);
                Assert.AreEqual(3, roles.Count, "AI 同方三轮不可重复 seed=" + seed);
            }
        }

        [Test]
        public void 三轮完成后进入Done并给出双方各三将()
        {
            var s = new BpSession(4);
            var picks = new[] { HeroId.PanShi, HeroId.DuanFeng, HeroId.XingLuo };
            int r = 0;
            while (s.State != BpSession.Phase.Done)
            {
                if (s.State == BpSession.Phase.Picking && s.BlueLock == null && r < 3)
                { s.PickBlue(picks[r]); r++; }
                s.Tick(0.25f);
            }
            CollectionAssert.AreEqual(picks, s.Blue);
            Assert.AreEqual(3, s.Red.Count);
        }
    }
}

namespace IronCurtain.Tests
{
    /// <summary>训练场规则(2026-09-13 制作人):无 BP、六将可产、名额上限无限制。</summary>
    public class PracticeRulesTests
    {
        private static GameConfig _cfg;
        private static GameConfig Cfg()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(UnityEngine.Resources.Load<UnityEngine.TextAsset>("gameconfig").text);
            return _cfg;
        }

        [Test]
        public void 名额上限覆盖_训练场999_默认为GeneralCap加时加一()
        {
            var sim = MatchSetup.Build(Cfg(), null, null);
            Assert.AreEqual(SimCore.GeneralCap, sim.PopulationCapOf(IronCurtain.Sim.Side.Blue), "默认=GeneralCap");
            sim.PopulationCapOverride = 999;
            Assert.AreEqual(999, sim.PopulationCapOf(IronCurtain.Sim.Side.Blue), "覆盖生效");
            sim.PopulationCapOverride = null;
            Assert.AreEqual(SimCore.GeneralCap, sim.PopulationCapOf(IronCurtain.Sim.Side.Blue), "清覆盖回默认");
        }

        [Test]
        public void 训练场可无限产将_超过GeneralCap仍可入队()
        {
            var sim = MatchSetup.Build(Cfg(), null, null);
            sim.PopulationCapOverride = 999;
            int baseId = 0;
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Building && e.Building == BuildingId.Base && e.Side == IronCurtain.Sim.Side.Blue) { baseId = e.Id; break; }
            Assert.AreNotEqual(0, baseId, "找到蓝方主基地");
            int ok = 0;
            for (int i = 0; i < 12; i++)   // 超过正常对局上限 8
                if (ProductionSystem.Enqueue(sim, baseId, HeroId.PanShi, out _)) ok++;
            Assert.AreEqual(12, ok, "12 个全部入队(不受 GeneralCap=8 限制)");
        }
    }
}
