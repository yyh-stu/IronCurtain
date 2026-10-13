using System.Collections.Generic;
using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 制作人:"**玩家刚生产出来的士兵还是会自动寻路**。你不能把 AI 的状态机单独做吗?
    /// 这样老是搞串还得修。"
    ///
    /// 定案:`ProductionSystem` 里"产出后自动行军到集结点"这道令**只给 AI 方下**
    /// (`AiSystem.IsAiControlled`)。玩家方的产出留在出生点听指挥。
    /// 注意区分:兵团自主行为层(视野索敌自动进攻 / 15m 支援 / 射手靠拢)**仍对双方生效** ——
    /// 那是制作人明确拍过板的"简化逻辑,玩家也一样",不要一起改掉(本文件第 3 条用例就是防这个的)。
    /// </summary>
    public sealed class ProductionRallyTests
    {
        private static GameConfig _cfg;

        private static GameConfig Cfg()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            return _cfg;
        }

        /// <summary>推进到"该方出现一名新主将",返回它(出生当 tick 就返回,便于看它拿到的第一道令)。</summary>
        private static Entity RunToSpawn(SimCore sim, Side side, HashSet<int> before, ref int ticks)
        {
            for (int i = 0; i < 60 * sim.Config.TickRate; i++)
            {
                sim.Step();
                ticks++;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == side && !before.Contains(e.Id))
                        return e;
                }
            }
            return null;
        }

        private static HashSet<int> Snapshot(SimCore sim)
        {
            var set = new HashSet<int>();
            foreach (var kv in sim.Entities) set.Add(kv.Key);
            return set;
        }

        private static int BaseOf(SimCore sim, Side side)
        {
            foreach (var kv in sim.Entities)
                if (kv.Value.Kind == EntityKind.Building && kv.Value.Building == BuildingId.Base
                    && kv.Value.Side == side) return kv.Key;
            return 0;
        }

        [Test]
        public void 无AI的一方_新生产的兵团不自动寻路()
        {
            // 练习场口径:两边都没有 AI
            MatchSetup.Reset();
            var sim = MatchSetup.Build(Cfg(), null, null);
            Assert.IsFalse(AiSystem.IsAiControlled(Side.Blue), "前置:蓝方不是 AI 控制");

            int baseId = BaseOf(sim, Side.Blue);
            string err;
            Assert.IsTrue(ProductionSystem.Enqueue(sim, baseId, HeroId.ZhuFeng, out err), "入队 ok,err=" + err);

            var before = Snapshot(sim);
            int ticks = 0;
            var gen = RunToSpawn(sim, Side.Blue, before, ref ticks);
            Assert.IsNotNull(gen, "应产出主将");
            Assert.IsFalse(gen.HasMoveTarget, "玩家的产出不得自动寻路(出生时不应带移动令)");
            Assert.AreEqual(0, gen.AutoKind, "也不应有自动决策");

            // 再跑 5 秒,确认它没有自己走开(制作人看到的"自动寻路")
            float startX = gen.PosF.X, startY = gen.PosF.Y;
            for (int i = 0; i < 5 * sim.Config.TickRate; i++) sim.Step();
            float moved = SimVec2.Distance(gen.PosF, new SimVec2(startX, startY));
            Assert.Less(moved, 1.5f, "玩家产出 5 秒内不得自行离开出生点(实测位移 " + moved.ToString("F2") + "m)");
        }

        [Test]
        public void AI控制的一方_新生产的兵团照旧推进到集结点()
        {
            MatchSetup.Reset();
            var sim = MatchSetup.Build(Cfg(), AiTier.Simple, null);   // 蓝方交给 AI(注意参数序:cfg, blueTier, redTier)
            Assert.IsTrue(AiSystem.IsAiControlled(Side.Blue), "前置:蓝方是 AI 控制");

            int baseId = BaseOf(sim, Side.Blue);
            string err;
            Assert.IsTrue(ProductionSystem.Enqueue(sim, baseId, HeroId.ZhuFeng, out err), "入队 ok,err=" + err);

            var before = Snapshot(sim);
            int ticks = 0;
            var gen = RunToSpawn(sim, Side.Blue, before, ref ticks);
            Assert.IsNotNull(gen, "AI 方应产出主将");
            Assert.IsTrue(gen.HasMoveTarget, "AI 方产出后应自动推进(AI 的调度没有被误伤)");
            Assert.AreEqual(sim.RallyPointOf(Side.Blue), gen.MoveTarget, "目标应为本方集结点");
        }

        [Test]
        public void 连续生产的兵团不堆叠_在基地前沿横向排开()
        {
            // 2026-09-14 制作人:"已产出兵团在出生点堆叠" → 加宽横向停靠面后,
            // 连续生产的多支兵团必须在基地前**横向**排开,而不是叠在一起、也不是朝敌方向排成一列。
            MatchSetup.Reset();
            var sim = MatchSetup.Build(Cfg(), null, null);   // 玩家方:产出不自动推进 → 就是会停下来的那种情形
            int baseId = BaseOf(sim, Side.Blue);
            var heroes = new[] { HeroId.ZhuFeng, HeroId.XingLuo, HeroId.DuanFeng };
            foreach (var h in heroes)
            {
                string err;
                Assert.IsTrue(ProductionSystem.Enqueue(sim, baseId, h, out err), "入队 " + h + " err=" + err);
            }
            var before = Snapshot(sim);
            int ticks = 0;
            var made = new List<Entity>();
            for (int i = 0; i < 60 * sim.Config.TickRate && made.Count < heroes.Length; i++)
            {
                sim.Step();
                ticks++;
                foreach (var kv in sim.Entities)
                {
                    var e = kv.Value;
                    if (e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == Side.Blue
                        && !before.Contains(e.Id) && !made.Contains(e)) made.Add(e);
                }
            }
            Assert.AreEqual(heroes.Length, made.Count, "三将都应产出");

            // ① 落位格两两不同
            for (int i = 0; i < made.Count; i++)
                for (int j = i + 1; j < made.Count; j++)
                    Assert.AreNotEqual(made[i].Pos, made[j].Pos, "两支部队不得落在同一格");

            // ② 不堆叠:同侧单位两两不重叠。
            // 注意口径:出生校验用的是**理想落点** 0.5 米,落位后士兵按阵型微调会吃掉几厘米
            // (实测最近 0.47 米),所以这里按"实体尺寸不重叠"判 —— 兵体宽 0.42/0.24 米,0.35 米以内才算叠。
            float minDist = float.MaxValue;
            var side = new List<Entity>();
            foreach (var kv in sim.Entities.Values)
                if (kv.Kind == EntityKind.Unit && kv.Side == Side.Blue && kv.State != EntityState.Dead) side.Add(kv);
            for (int i = 0; i < side.Count; i++)
                for (int j = i + 1; j < side.Count; j++)
                    minDist = Mathf.Min(minDist, SimVec2.Distance(side[i].PosF, side[j].PosF));
            Assert.Greater(minDist, 0.35f, "同侧单位不得重叠(最近间距 " + minDist.ToString("F2") + "m)");

            // ③ 横向排开:至少两名产出的主将行号不同(证明确实用了横向停靠面,而不是一路朝敌排一列)
            int distinctRows = 0;
            var rows = new HashSet<int>();
            foreach (var g in made) if (rows.Add(g.Pos.Row)) distinctRows++;
            Assert.GreaterOrEqual(distinctRows, 2,
                "产出的兵团应在基地前沿横向排开(实测不同行数 " + distinctRows + "/" + made.Count + ")");
        }

        [Test]
        public void 闸门只认AI_别把双方都生效的行为层也一起关掉()
        {
            // 制作人:"视野内出现敌人自动攻击是为了简化逻辑,玩家也一样" ——
            // 无 AI 的一方,部队仍要有自主索敌(这条不许被"只给 AI"误伤)。
            MatchSetup.Reset();
            var sim = MatchSetup.Build(Cfg(), null, null);
            Assert.IsFalse(AiSystem.IsAiControlled(Side.Blue));
            Assert.IsTrue(sim.Config.AutoBehavior, "自主行为总开关仍开着");

            // 把一名蓝方主将挪到红方单位旁边(视野内),它应当自己进入索敌进攻
            Entity blue = null;
            foreach (var kv in sim.Entities)
                if (kv.Value.Kind == EntityKind.Unit && !kv.Value.IsSoldier && kv.Value.Side == Side.Blue)
                { blue = kv.Value; break; }
            Assert.IsNotNull(blue);
            var red = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi,
                new GridPos(blue.Pos.Row, blue.Pos.Col + 5));   // 距 5 ≤ 近战视野 6

            bool engaged = false;
            for (int i = 0; i < 200 && !engaged; i++)
            {
                sim.Step();
                // 2026-09-14 新口径:20 格内友军挨打时**支援优先于索敌** → 只要自动层接管(AutoKind != 0)即通过
                engaged = blue.AutoKind != 0;
            }
            Assert.IsTrue(engaged, "玩家方(无 AI)的部队仍应自主索敌进攻");
        }
    }
}
