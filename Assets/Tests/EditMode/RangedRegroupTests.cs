using System.Collections.Generic;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 制作人口径(选项 A):**射手单位视野内出现"敌方近战单位" → 优先向附近友军靠拢**。
    /// 制作人 2026-09-05/06 拍板的原始规则里还有"直到近战交战才开始攻击"(hold-fire),
    /// 本轮明确**只做靠拢、不拦开火** —— 所以这里只断言"站位往后收",不断言"不开火"。
    /// </summary>
    public sealed class RangedRegroupTests
    {
        private static GameConfig _cfg;

        private static SimCore NewAutoSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig { AutoBehavior = true }, new SeededRng(21));
            sim.Game = _cfg;
            return sim;
        }

        /// <summary>放一个"光杆"主将(不带随行士兵,免得士兵挤位搅浑距离测量)。</summary>
        private static Entity LoneGeneral(SimCore sim, Side side, HeroId hero, GridPos pos)
        {
            var g = RegimentSystem.SpawnGeneral(sim, side, hero, pos);
            var kids = new List<int>();
            foreach (var kv in sim.Entities)
                if (kv.Value.IsSoldier && kv.Value.GeneralId == g.Id) kids.Add(kv.Key);
            foreach (var k in kids) sim.RemoveEntity(k);
            return g;
        }

        [TearDown]
        public void TearDown()
        {
            AiSystem.Reset();
            VisionSystem.Reset();
            HexSystem.Reset();
            SiegeSystem.Reset();
        }

        [Test]
        public void 射手见敌方近战_优先向友军靠拢而不是迎上去()
        {
            var sim = NewAutoSim();
            // 我方:星落(射手,视野 9)在 (80,100);磐石(近战队友)在 (80,92) —— 相距 8 ≤ 15
            var archer = LoneGeneral(sim, Side.Blue, HeroId.XingLuo, new GridPos(80, 100));
            var ally = LoneGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(80, 92));
            // 敌方近战在 (80,107):距射手 7 ≤ 视野 9 → "视野内出现敌方近战"
            var enemy = LoneGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 107));

            float enemyDist0 = SimVec2.Distance(archer.PosF, enemy.PosF);
            float allyDist0 = SimVec2.Distance(archer.PosF, ally.PosF);

            bool regrouped = false;
            for (int i = 0; i < 300; i++)
            {
                sim.Step();
                if (archer.AutoKind == 3) { regrouped = true; break; }
            }
            Assert.IsTrue(regrouped, "射手视野内出现敌方近战 → 应触发靠拢(AutoKind=3)");
            Assert.AreEqual(enemy.Id, archer.AutoTargetId, "靠拢的成因记录为该近战威胁(供防复读)");

            float x0 = archer.PosF.X;
            for (int i = 0; i < 120; i++) sim.Step();
            float allyDist1 = SimVec2.Distance(archer.PosF, ally.PosF);
            Assert.Less(allyDist1, allyDist0, "应向友军靠拢(与友军距离变小)");
            // 2026-09-14:第二条断言原写"与敌人的距离必须变大" —— 但敌方近战视野已是 8,它现在**会主动追**,
            // 那个量会被追击掩盖(实测 7→5.05 并非射手迎上去)。改成直接看射手**自己的位移方向**:
            // 蓝方朝东(敌在动),射手不得往东走。
            Assert.Less(archer.PosF.X, x0 + 0.5f, "射手不得朝敌人方向移动(应向西退向友军)");
            Assert.Less(archer.PosF.X, enemy.PosF.X, "射手应保持在敌人西侧");
        }

        [Test]
        public void 射手照常输出_近战交战不是开火门槛()
        {
            // 制作人明确:"射手有输出条件依旧输出,近战交战直接开火只是**附加**条件"。
            // 这里锁死"没有 hold-fire":己方没有任何近战交战时,射手在射程内照样造成伤害。
            var sim = NewAutoSim();
            var archer = LoneGeneral(sim, Side.Blue, HeroId.XingLuo, new GridPos(80, 100));
            var enemy = LoneGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 105));  // 距 5 ≤ 射手射程
            enemy.Hp = enemy.MaxHp = 100000f;
            archer.Range = 9f; archer.Attack = 50f; archer.AttackIntervalSec = 0.5f; archer.AttackCooldown = 0f;
            Assert.Greater(archer.SecondsSinceCombat, 3f, "前置:己方无近战交战");

            float hp0 = enemy.Hp;
            for (int i = 0; i < 40; i++) sim.Step();
            Assert.Less(enemy.Hp, hp0, "射手照常输出,不得因'近战未交战'而不开火");
        }

        [Test]
        public void 射手见敌方远程_不触发靠拢_就地开火()
        {
            var sim = NewAutoSim();
            var archer = LoneGeneral(sim, Side.Blue, HeroId.XingLuo, new GridPos(80, 100));
            LoneGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(80, 92));           // 有友军在旁
            var ranged = LoneGeneral(sim, Side.Red, HeroId.ZhuiRi, new GridPos(80, 107)); // 敌方是**远程**
            ranged.Hp = ranged.MaxHp = 100000f;
            archer.Range = 9f; archer.Attack = 50f; archer.AttackIntervalSec = 0.5f; archer.AttackCooldown = 0f;

            // 注:AutoKind 会在"到达即清"里归零,所以要记"曾经出现过什么",不能事后读一次
            bool everRegroup = false;
            float hp0 = ranged.Hp;
            for (int i = 0; i < 200; i++)
            {
                sim.Step();
                if (archer.AutoKind == 3) everRegroup = true;
            }
            Assert.IsFalse(everRegroup, "敌方只有远程时不走靠拢规则");
            // 2026-09-14 起"已在射程内就不再前压"(修边打边位移),故不再断言"拿到移动令",
            // 改为断言**真的在开火** —— 这才是该有的行为。
            Assert.Less(ranged.Hp, hp0, "射手应就地开火(不必再向前压)");
        }

        [Test]
        public void 近战主将见敌方近战_不走靠拢规则()
        {
            var sim = NewAutoSim();
            var melee = LoneGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(80, 100));
            LoneGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(80, 92));
            LoneGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 105));   // 距 5 ≤ 近战主将视野 6(必须可见才会索敌)

            bool everRegroup = false, everEngage = false;
            for (int i = 0; i < 200; i++)
            {
                sim.Step();
                if (melee.AutoKind == 3) everRegroup = true;
                if (melee.AutoKind == 1) everEngage = true;
            }
            Assert.IsFalse(everRegroup, "靠拢只对射手生效");
            Assert.IsTrue(everEngage, "近战仍是索敌进攻");
        }

        [Test]
        public void 玩家打断后_对同一近战威胁不再自动后撤()
        {
            var sim = NewAutoSim();
            var archer = LoneGeneral(sim, Side.Blue, HeroId.XingLuo, new GridPos(80, 100));
            LoneGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(80, 92));
            var enemy = LoneGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 107));

            bool ok = false;
            for (int i = 0; i < 300 && !ok; i++) { sim.Step(); ok = archer.AutoKind == 3; }
            Assert.IsTrue(ok, "前置:先触发一次靠拢");

            sim.SetMoveTarget(archer.Id, new GridPos(80, 112), true);   // 玩家手动下令(朝敌方向)
            Assert.AreEqual(3, archer.SupKind, "手动命令应把 AutoKind=3 记成 SupKind=3");
            Assert.AreEqual(enemy.Id, archer.SupTargetId);

            bool regroupAgain = false;
            for (int i = 0; i < 200; i++)
            {
                sim.Step();
                if (archer.AutoKind == 3) { regroupAgain = true; break; }
            }
            Assert.IsFalse(regroupAgain, "同一个近战威胁不该把玩家已接管的射手再拉回去靠拢");
        }
    }
}
