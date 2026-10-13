using System.Linq;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 制作人:「士兵挨打了,意味着敌人出现在视野内了,按道理说整个兵团都会自动上去打」,
    /// 并怀疑存在"射手射程比近战视野大"的边界情况 —— **确认成立**:
    /// 射手(坠日/星落)射程 **9 米**,而近战士兵视野 **3 米**、近战主将视野 **6 米**
    /// (`gameconfig.json` 射程 + `RegimentBehaviorSystem.VisionRadiusOf`,与 `VisionSystem` 同口径)。
    /// 于是射手可以在近战兵团的视野外开火,近战兵团"站着挨打不还手"。
    /// 本文件锁定修复后的口径:**被打 = 看得见打我的那个敌人**(整团自动上去打)。
    /// </summary>
    public sealed class AttackedPerceptionTests
    {
        private static GameConfig _cfg;
        private SimCore sim;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig { AutoBehavior = true }, new SeededRng(11));
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

        /// <summary>把整团(主将 + 其士兵)的射程人为加长,用于构造"在对方视野外开火"。</summary>
        private void Lengthen(Entity g, float range)
        {
            g.Range = range;
            foreach (var s in sim.Entities.Values)
                if (s.IsSoldier && s.GeneralId == g.Id) s.Range = range;
        }

        [Test]
        public void T1_被视野外的射手打_近战兵团必须自动压上去()
        {
            // 磐石(近战,射程 2/视野 6)对 坠日(射手,射程 9)
            // 2026-09-14 起近战视野 8 = 射手射程 8,配置层面的这个边界已经堵上(见 VisionTests);
            // 这里把攻击者射程**人为加到 14**,构造"确实在视野外被打",单测这条规则本身
            // (规则=制作人口径「士兵挨打了就意味着敌人出现在视野内」,与具体数值无关)。
            var melee = Regiment(Side.Blue, HeroId.PanShi, new GridPos(80, 120));
            var archer = Regiment(Side.Red, HeroId.ZhuiRi, new GridPos(80, 132));   // 12 米
            Lengthen(archer, 14f);
            // 前置在**出生瞬间**量:此时射手在近战视野(8 米)之外 —— 它就是在视野外开的火;
            // 之后近战团会因"挨打=看得见"压上去(那正是本用例要验的行为,不能拿它当"前置"量)。
            float dist = SimVec2.Distance(melee.PosF, archer.PosF);
            Assert.Greater(dist, 8f, "前置:出生时射手在近战视野(8 米)之外(实际 " + dist.ToString("F1") + ")");
            for (int i = 0; i < 30; i++) sim.Step();
            Assert.Less(melee.SecondsSinceCombat, 3f, "前置:肉盾应在挨打(射手已开火)");

            var startDist = dist;
            bool came = false;
            for (int i = 0; i < 400 && !came; i++)
            {
                sim.Step();
                if (SimVec2.Distance(melee.PosF, archer.PosF) < startDist - 2f) came = true;
            }
            Assert.IsTrue(came, "被视野外的射手打了之后,近战兵团必须自动压上去打它(实测站着没动,距离 "
                + SimVec2.Distance(melee.PosF, archer.PosF).ToString("F1") + " 米)");
        }

        [Test]
        public void T2_挨打后打我的那个敌人被视为可见()
        {
            var melee = Regiment(Side.Blue, HeroId.PanShi, new GridPos(80, 120));
            var archer = Regiment(Side.Red, HeroId.ZhuiRi, new GridPos(80, 132));   // 12 米(射程人为 14)
            Lengthen(archer, 14f);
            Assert.Greater(SimVec2.Distance(melee.PosF, archer.PosF), 8f, "前置:出生时在被攻击方的视野外");
            for (int i = 0; i < 30; i++) sim.Step();
            Assert.Less(melee.SecondsSinceCombat, 3f, "前置:肉盾应在挨打");

            // 被打了 → 应该认得出手方(哪怕它还在视野外) → 记录在案
            int known = melee.LastAttackerId;
            Assert.AreNotEqual(0, known, "挨打后应记录'谁在打我'(LastAttackerId)");
            // 记的是**最近一次打我**的单位:射手团的士兵同样在 14 米外开火,可能先命中,
            // 所以判据是"打我的人属于射手那个兵团"(射手本人或其士兵),而不是必须恰好是射手主将。
            var attacker = sim.GetEntity(known);
            Assert.IsNotNull(attacker, "攻击者应还在场");
            Assert.IsTrue(attacker.Id == archer.Id || attacker.GeneralId == archer.Id,
                "记的应是射手兵团的单位(实际 id=" + known + " 士兵=" + attacker.IsSoldier + ")");
        }

        [Test]
        public void T3_主动开火的射手仍在近战视野内时照旧索敌()
        {
            // 不引入回归:射手走进近战视野(6 米内)时,原来的索敌逻辑照样生效
            var melee = Regiment(Side.Blue, HeroId.PanShi, new GridPos(80, 120));
            var archer = Regiment(Side.Red, HeroId.ZhuiRi, new GridPos(80, 127));   // 7 米(视野外开火)
            var startDist = SimVec2.Distance(melee.PosF, archer.PosF);
            bool closed = false;
            for (int i = 0; i < 600 && !closed; i++)
            {
                sim.Step();
                if (SimVec2.Distance(melee.PosF, archer.PosF) < startDist - 2f) closed = true;
            }
            Assert.IsTrue(closed, "挨打后应压上去(这条与 T1 同源,留作回归保护)");
            Assert.IsNotNull(sim.GetEntity(archer.Id), "射手应还在场");
        }
    }
}
