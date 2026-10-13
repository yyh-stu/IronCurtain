using System.Collections.Generic;
using System.Linq;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 制作人实机报障回归:「占领对方箭塔后,将军队派出,但是三个将只剩一个,
    /// 剩的哪一个将也只有几个兵,不是满编」。
    /// 三条根因(均在 SiegeSystem 的交换循环 / Capture):
    /// ① 交换中被消耗的主将只离开 atk 队列、State 仍是 `Exchanging`;该状态被全系统跳过
    ///    (移动/兵团跟随/索敌/被塔攻击/阵型/AI),于是主将永远钉在城下:不能指挥、不能跟随,
    ///    也不能再次受令攻城(IsEligibleGeneral 拒绝 Exchanging)。
    /// ② 占领后不清建筑 Intent,后到的**自家**士兵继续被 EnterCity 拉进城,和自家自动驻军
    ///    按 1:1 互换——兵被自己的城一个一个吃掉。
    /// ③ 自动驻军取了配置的 GarrisonFormal(箭塔 30)=**手动**驻军容量,自动流程把整支军团
    ///    吸进城里并隐藏,而不是 D23 的「≤9,超出者离城待命」。
    /// </summary>
    public sealed class CaptureArmyIntegrityTests
    {
        private static GameConfig _cfg;
        private SimCore sim;
        private int towerId;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig(), new SeededRng(1));
            sim.Game = _cfg;
            SiegeSystem.Reset();
            towerId = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 130)).Id;
        }

        [TearDown]
        public void TearDown()
        {
            AiSystem.Reset();
            VisionSystem.Reset();
            SiegeSystem.Reset();
        }

        private static List<Entity> SoldiersOf(SimCore owner, int generalId)
            => owner.Entities.Values
                .Where(x => x.Kind == EntityKind.Unit && x.IsSoldier && x.GeneralId == generalId)
                .OrderBy(x => x.Id).ToList();

        private static List<Entity> AllUnits(SimCore owner)
            => owner.Entities.Values.Where(e => e.Kind == EntityKind.Unit).ToList();

        /// <summary>给箭塔登记 count 名正式守军(与 SiegeTests.Garrison 同法:士兵不参与兵团跟随)。</summary>
        private void Garrison(int count)
        {
            int added = 0, col = 200;
            while (added < count && col < 300)
            {
                var g = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(5, col));
                col += 25;
                foreach (var s in SoldiersOf(sim, g.Id))
                {
                    s.GarrisonBuildingId = towerId;
                    sim.Entities[towerId].GarrisonFormal.Add(s.Id);
                    if (++added >= count) break;
                }
            }
        }

        private bool RunUntil(System.Func<bool> cond, int maxTicks = 8000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                sim.Step();
                if (cond()) return true;
            }
            return cond();
        }

        /// <summary>全场景不变量:任何单位都不得停在 Exchanging。</summary>
        private void AssertNoExchangingGhost(string where)
        {
            var ghosts = AllUnits(sim).Where(e => e.State == EntityState.Exchanging).ToList();
            if (ghosts.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            foreach (var e in ghosts)
            {
                sb.Append("\n    幽灵 id=").Append(e.Id).Append(" 士兵=").Append(e.IsSoldier)
                  .Append(" 目标建筑=").Append(e.TargetBuildingId).Append(" 将=").Append(e.GeneralId)
                  .Append(" 驻军绑定=").Append(e.GarrisonBuildingId);
                var tb = sim.GetEntity(e.TargetBuildingId);
                if (tb == null) sb.Append(" 目标不存在");
                else sb.Append(" 建筑驻军(正式=").Append(tb.GarrisonFormal.Count)
                           .Append(" 临时=").Append(tb.GarrisonTemp.Count)
                           .Append(" 归属=").Append(tb.Side)
                           .Append(" 意图=").Append(tb.Intent).Append(")");
            }
            Assert.Fail(where + ":不应存在 Exchanging 僵尸单位(该状态被全系统跳过=永久僵死)" + sb);
        }

        [Test]
        public void T1_三将满编攻下箭塔_全员出城_无僵死且城内只留小队()
        {
            Garrison(30); // 满编箭塔:守军多到足以在交换中"吃掉"先到的攻方单位(含主将)
            var g1 = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 100));
            var g2 = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 106));
            var g3 = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 112));
            var gens = new[] { g1, g2, g3 };
            var soldiers = gens.SelectMany(g => SoldiersOf(sim, g.Id)).ToList();
            Assert.AreEqual(36, soldiers.Count, "前置:三将各满编 12 兵");

            var order = SiegeSystem.TryIssueRegimentOrder(
                sim, gens.Select(g => g.Id).ToList(), towerId, Side.Blue, SiegeIntent.Capture);
            Assert.IsTrue(order.Success, "前置:兵团攻城令应下发成功:" + order.Error + "(落点规划失败?)");

            var b = sim.Entities[towerId];
            Assert.IsTrue(RunUntil(() => b.Side == Side.Blue), "超时未能占领箭塔");
            AssertNoExchangingGhost("占领箭塔后");

            // 目标完成:不得再有单位挂在建筑上(旧的旗标残留会让后到的人继续进城)
            Assert.IsEmpty(AllUnits(sim).Where(e => e.TargetBuildingId == towerId).Select(e => e.Id).ToList(),
                "占领后不应再有单位以该建筑为目标");
            Assert.AreEqual(SiegeIntent.None, b.Intent, "占领后意图应清零(目标完成→原地待命)");

            // 自动驻军 = **整团**(2026-09-14 制作人口径「每个军团要么全留,要么全出」)。
            // 本用例是"守军足够吃人"的攻城:三名主将都在交换中出力(各换掉 10 点守军),
            // 按「主将和他的兵团就是一个单位、主将可入城」的口径,主将完成后**留在城内**,
            // 于是占领时城里有一个完整团 → 整团留城(主将一起守城);其余两团整团出城。
            // 整团口径下,本用例的结果取决于"占领那一刻城里有没有完整团":
            // 主将都在交换中留守 → 整团留城;主将都已出城 → 全出。**两种都合规**,不许出现的是半团。
            int keeperSoldiers = AssertNoPartialRegiment(b);

            int aliveGensOutside = 0;
            foreach (var g in gens.Where(x => sim.Entities.ContainsKey(x.Id)))
            {
                var ent = sim.GetEntity(g.Id);
                Assert.AreNotEqual(EntityState.Exchanging, ent.State, "主将不得停在 Exchanging(僵死幽灵)");
                Assert.IsNotNull(ent, "三名主将都应存活(主将不因交换阵亡)");
                if (ent.GarrisonBuildingId == 0) aliveGensOutside++;
            }
            Assert.AreEqual(3 - (b.GarrisonFormal.Count > 0 ? 1 : 0), aliveGensOutside,
                "留城至多 1 团(留城那名主将在城内),其余主将都应随军出城");

            // 军团没被城吃掉:除留城那一整团外,其余士兵随军出城;城外主将谁能动
            var alive = soldiers.Where(s => sim.Entities.ContainsKey(s.Id)).ToList();
            int outside = alive.Count(s => sim.GetEntity(s.Id).GarrisonBuildingId == 0);
            Assert.AreEqual(alive.Count - keeperSoldiers, outside, "其余士兵应整团随军出城");

            foreach (var g in gens.Where(x => sim.Entities.ContainsKey(x.Id)))
            {
                var ent = sim.GetEntity(g.Id);
                if (ent.GarrisonBuildingId != 0) continue;   // 留城守城的那个不参与"随军出城"
                var from = ent.PosF;
                sim.SetMoveTarget(ent.Id, new GridPos(5, 60), true);
                for (int i = 0; i < 240; i++) sim.Step();
                Assert.Greater(SimVec2.Distance(from, ent.PosF), 1f, "主将应能响应移动令(僵死幽灵永不动)");
            }
        }

        /// <summary>
        /// "整团驻军"不变量(任何场景都必须成立):驻军要么为空,要么**恰好是一个完整兵团**——
        /// ①恰好 1 名主将 ②驻军里的每个士兵都属于这名主将 ③该主将的全部存活士兵都在驻军里。
        /// 这条不变量正是制作人「不要主将出去兵还在城里」的机器版本;返回留城士兵数(空则 0)。
        /// </summary>
        private int AssertNoPartialRegiment(Entity b)
        {
            var ids = new List<int>(b.GarrisonFormal);
            if (ids.Count == 0) return 0;
            var generals = ids.Where(id => sim.Entities[id] != null && !sim.Entities[id].IsSoldier).ToList();
            Assert.AreEqual(1, generals.Count, "驻军应是整团:恰好 1 名主将;实际驻军 " + ids.Count + " 人");
            var keeper = sim.Entities[generals[0]];
            var soldierIds = ids.Where(id => sim.Entities[id] != null && sim.Entities[id].IsSoldier).ToList();
            Assert.IsNotEmpty(soldierIds, "整团驻军不该是光杆主将");
            foreach (var id in soldierIds)
                Assert.AreEqual(keeper.Id, sim.Entities[id].GeneralId, "驻军士兵必须属于留城那名主将(不许半团)");
            int living = 0;
            foreach (var kv in sim.Entities)
                if (kv.Value.IsSoldier && kv.Value.GeneralId == keeper.Id) living++;
            Assert.AreEqual(living, soldierIds.Count, "整团留城:该主将的全部存活士兵都应随主将驻城");
            Assert.AreEqual(1 + living, ids.Count, "驻军人数应为主将 + 其全部士兵");
            return living;
        }

        [Test]
        public void T3_空箭塔占领_军队不被自己的城吃掉()
        {
            // 制作人现场的原样复刻:地图上的箭塔(MatchSetup)开局**没有任何驻军**,
            // 所以"占领对方箭塔"走的是「无人建筑直接占领」→ Capture。旧码在这里埋了连环陷阱:
            // ① Capture 不清 Intent → 建筑已归我方,Intent 仍是 Capture;
            // ② 因此**后到的自家士兵**继续被 EnterCity 拉进城(State=Exchanging、离格);
            // ③ 交换循环只要有"攻方城内单位 + 正式驻军"就结算 → 自家兵和自己的驻军 1:1 互换,
            //    每个攻方士兵还顺手带走 1 名自家守军(双倍损失);
            // ④ 主将被消耗后停在 Exchanging → 全系统跳过 → 永远钉在城下。
            // 结果就是制作人看到的:三个将只剩一个,剩的那个也没几个兵。
            var g1 = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 100));
            var g2 = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 106));
            var g3 = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 112));
            var gens = new[] { g1, g2, g3 };
            var soldiers = gens.SelectMany(g => SoldiersOf(sim, g.Id)).ToList();
            Assert.AreEqual(36, soldiers.Count, "前置:三将各满编 12 兵");

            var order = SiegeSystem.TryIssueRegimentOrder(
                sim, gens.Select(g => g.Id).ToList(), towerId, Side.Blue, SiegeIntent.Capture);
            Assert.IsTrue(order.Success, "前置:兵团攻城令应下发成功:" + order.Error);

            var b = sim.Entities[towerId];
            Assert.IsTrue(RunUntil(() => b.Side == Side.Blue), "超时未能占领空箭塔");
            for (int i = 0; i < 200; i++) sim.Step(); // 再跑一段:看有没有"后到的自家兵"继续进城
            AssertNoExchangingGhost("占领空箭塔后");

            Assert.IsEmpty(AllUnits(sim).Where(e => e.TargetBuildingId == towerId).Select(e => e.Id).ToList(),
                "占领后不应再有单位以该建筑为目标");
            Assert.AreEqual(SiegeIntent.None, b.Intent, "占领后意图应清零,否则后到的自家兵会继续被拉进城");
            // 空塔的口径(2026-09-14/15 两条制作人规则叠加后的正确结果):
            // ①「无人建筑接触即占领」→ 是在**第一个兵到场**的瞬间就易主的;
            // ②「驻军/交换只算在场的人」+「整团要么全留要么全出」→ 那一刻整团**没到齐**,所以**全出**,
            //    不许把还在路上的兵"隔空"算成驻军(旧期望"整团留城"依赖"把在路上的人也算在场",已被②推翻)。
            int keeperSoldiers = AssertNoPartialRegiment(b);
            Assert.AreEqual(0, b.GarrisonFormal.Count,
                "空塔在第一名到场者脚下即易主,整团未到齐 → 按在场口径应全出;实际驻军=" + b.GarrisonFormal.Count);

            var alive = soldiers.Where(s => sim.Entities.ContainsKey(s.Id)).ToList();
            Assert.GreaterOrEqual(alive.Count, 34, "空塔无守军,攻方士兵不该在'自家城'里互换阵亡(实际剩 " + alive.Count + ")");
            int outside = alive.Count(s => sim.GetEntity(s.Id).GarrisonBuildingId == 0);
            Assert.AreEqual(alive.Count - keeperSoldiers, outside, "除留城那一整团外,其余士兵随军出城");

            foreach (var g in gens)
            {
                var ent = sim.GetEntity(g.Id);
                Assert.IsNotNull(ent, "主将应全部存活(空塔无战斗)");
                // 占领后保留移动目标 → 主将会继续走到被派的建筑那儿待命(可能仍是 Moving),但绝不许留在 Exchanging
                Assert.AreNotEqual(EntityState.Exchanging, ent.State, "主将不应停在 Exchanging");
                if (ent.GarrisonBuildingId != 0) continue;   // 留城守城的那个不参与"随军出城"
                var from = ent.PosF;
                sim.SetMoveTarget(ent.Id, new GridPos(5, 60), true);
                for (int i = 0; i < 240; i++) sim.Step();
                Assert.Greater(SimVec2.Distance(from, ent.PosF), 1f, "主将应能随军出城");
            }

            // 留城那一整团可一键收回,且**释放也必须是整团**(主将 + 其全部士兵):驻军不拆团
            // (本条场景按在场口径是"全出",所以只在确实留了整团时才验释放)
            if (b.GarrisonFormal.Count > 0)
            {
                var keeper = sim.Entities[b.GarrisonFormal.First(id => !sim.Entities[id].IsSoldier)];
                int released; string relErr;
                Assert.IsTrue(GarrisonSystem.ExitRegiment(sim, towerId, keeper.Hero.Value, out relErr, out released),
                    "整团释放应成功:" + relErr);
                Assert.AreEqual(1 + keeperSoldiers, released, "释放出城应是整团:主将 + 其全部士兵");
                Assert.AreEqual(0, b.GarrisonFormal.Count, "释放后驻军应清空");
                AssertNoExchangingGhost("整团释放后");
            }
        }

        [Test]
        public void T4_团不完整则一个都不留城_有兵驻在别处()
        {
            // 制作人口径的负向用例:「每个军团要么全留,要么全出」。
            // 使某团"不完整"的**可复现**做法:该团有一名士兵已驻在**另一座城**里
            // (攻城放兵时不放已驻军单位,所以这个兵确实不在攻方队列里);
            // 于是这团不能整团留城 → **一个都不留**,主将与到场的士兵一起随军出城。
            // (旧"按人数凑"的自动驻军正是在这里留下半团。)
            var home = sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(60, 60));
            Garrison(1);                                  // 目标塔有守军 → 走进城交换(不被"无人建筑直接占领"抢先)
            Assert.IsTrue(SiegeSystem.SetIntent(sim, towerId, Side.Blue, SiegeIntent.Capture, out var err), err);
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 100));
            var all = SoldiersOf(sim, g.Id);
            Assert.AreEqual(12, all.Count, "前置:满编 12 兵");
            var leftBehind = all[0];
            Assert.IsTrue(GarrisonSystem.EnterFormal(sim, new List<int> { leftBehind.Id }, home.Id, out var gerr), gerr);
            Assert.AreEqual(home.Id, leftBehind.GarrisonBuildingId, "前置:该兵已驻在另一座城");

            var towerPos = sim.Entities[towerId].PosF;
            var bring = all.Skip(1).ToList();             // 其余 11 兵随主将攻城
            foreach (var s in bring)
            {
                s.PosF = new SimVec2(towerPos.X - 4f, towerPos.Y - 3f + s.Id % 6);
                s.State = EntityState.Idle;
                s.HasMoveTarget = false;
            }
            Assert.IsTrue(SiegeSystem.IssueOrder(sim, bring.Select(x => x.Id).ToList(), towerId, out var oerr), oerr);
            foreach (var s in bring) s.HasMoveTarget = false;   // 模拟已抵达
            g.TargetBuildingId = towerId;                       // 主将参战
            g.HasMoveTarget = false;
            g.PosF = new SimVec2(towerPos.X - 5f, towerPos.Y);

            var b = sim.Entities[towerId];
            Assert.IsTrue(RunUntil(() => b.Side == Side.Blue), "超时未能占领箭塔");
            AssertNoExchangingGhost("团不完整时攻城后");
            Assert.IsEmpty(new List<int>(b.GarrisonFormal), "团不完整 → 一个都不留城;实际驻军=" + b.GarrisonFormal.Count);
            foreach (var s in bring)
                if (sim.Entities.ContainsKey(s.Id))
                    Assert.AreEqual(0, sim.GetEntity(s.Id).GarrisonBuildingId, "到场士兵应随军出城");
            var gen = sim.GetEntity(g.Id);
            Assert.IsNotNull(gen, "主将应存活");
            Assert.AreEqual(0, gen.GarrisonBuildingId, "主将也不留城(全出)");
            Assert.AreEqual(home.Id, sim.GetEntity(leftBehind.Id).GarrisonBuildingId, "驻在别处的那个兵不受影响");
        }

        [Test]
        public void T2_攻方被肃清的攻城_主将从交换状态放回城外且可再受令()
        {
            Garrison(30);
            Assert.IsTrue(SiegeSystem.SetIntent(sim, towerId, Side.Blue, SiegeIntent.Capture, out var err), err);
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 122));
            g.TargetBuildingId = towerId;
            g.HasMoveTarget = false;

            var b = sim.Entities[towerId];
            // 主将一轮换 10,本团 12 兵 1:1 换 12 → 攻方全灭(箭塔火力另有可能带走 1~2 兵),
            // 守军不清零 → 攻城失败。跑足 600 tick 让交换彻底停下来。
            for (int i = 0; i < 600; i++) sim.Step();
            Assert.AreEqual(Side.Red, b.Side, "攻方全灭,箭塔应仍属红方(不得被易主)");
            Assert.Greater(b.GarrisonFormal.Count, 0, "守军不应被打空");
            Assert.Less(b.GarrisonFormal.Count, 30, "守军应有损耗(主将 10 + 士兵 1:1)");
            AssertNoExchangingGhost("攻方被肃清后");

            var ent = sim.GetEntity(g.Id);
            Assert.IsNotNull(ent, "主将本体不死");
            Assert.AreEqual(EntityState.Idle, ent.State, "攻城失败后主将应回 Idle,而不是钉死在 Exchanging");
            Assert.AreEqual(0, ent.TargetBuildingId, "攻城失败后主将应摘掉攻城标记");

            var from = ent.PosF;
            sim.SetMoveTarget(ent.Id, new GridPos(5, 60), true);
            for (int i = 0; i < 240; i++) sim.Step();
            Assert.Greater(SimVec2.Distance(from, ent.PosF), 1f, "旧码钉死在 Exchanging,主将永远不动");
        }
    }
}
