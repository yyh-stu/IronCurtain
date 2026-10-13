using System.Collections.Generic;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 两个 bug 的回归:
    /// **①「点驻军后主将没进去、进去的士兵消失了」** —— 旧 `Rollback` 只把人从驻军名册删掉、
    ///   没清 `GarrisonBuildingId`,那些单位既不在名册(释放/清理都找不到)、又被视图按"城内"隐藏
    ///   = **永久幽灵**。本文件用"登记不变量"把它钉死。
    /// **②「情报获取时间」** —— 敌方建筑的驻军情报切片要能标出快照时刻(界面口径),未侦察过返回 null(显示"？")。
    /// </summary>
    public sealed class GarrisonGhostTests
    {
        private static GameConfig _cfg;

        private static SimCore NewSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig(), new SeededRng(31));
            sim.Game = _cfg;
            return sim;
        }

        private static List<Entity> SoldiersOf(SimCore sim, int generalId)
        {
            var list = new List<Entity>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Unit && e.IsSoldier && e.GeneralId == generalId) list.Add(e);
            return list;
        }

        /// <summary>不变量:任何"登记为城内(正式/临时)"的单位,都必须出现在某座建筑的名册里。
        /// 违反 = 幽灵(不可见、不可释放)。</summary>
        private static void AssertNoGhosts(SimCore sim, string tag)
        {
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Unit) continue;
                if (e.GarrisonBuildingId != 0)
                {
                    bool listed = false;
                    foreach (var b in sim.Entities.Values)
                        if (b.Kind == EntityKind.Building && b.GarrisonFormal.Contains(e.Id)) { listed = true; break; }
                    Assert.IsTrue(listed, tag + ":单位 " + e.Id + " 登记着城内(GarrisonBuildingId="
                        + e.GarrisonBuildingId + ")却不在任何正式名册里 = 幽灵");
                }
                if (e.TempGarrisonId != 0)
                {
                    bool listed = false;
                    foreach (var b in sim.Entities.Values)
                        if (b.Kind == EntityKind.Building && b.GarrisonTemp.Contains(e.Id)) { listed = true; break; }
                    Assert.IsTrue(listed, tag + ":单位 " + e.Id + " 登记着临时驻军却不在任何临时名册里 = 幽灵");
                }
            }
        }

        [Test]
        public void 正常入场_主将与士兵都在名册且无幽灵()
        {
            var sim = NewSim();
            var b = sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(80, 100));
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(80, 104));
            var ids = new List<int> { g.Id };
            foreach (var s in SoldiersOf(sim, g.Id)) ids.Add(s.Id);

            string err;
            Assert.IsTrue(GarrisonSystem.EnterFormal(sim, ids, b.Id, out err), "入场应成功 err=" + err);
            Assert.AreEqual(13, b.GarrisonFormal.Count, "1 主将 + 12 兵");
            foreach (var id in ids)
            {
                var u = sim.GetEntity(id);
                Assert.AreNotEqual(0, u.GarrisonBuildingId, "单位 " + id + " 应登记为城内");
            }
            AssertNoGhosts(sim, "正常入场");
        }

        [Test]
        public void 入场被拒时_已受理的单位不得变成幽灵()
        {
            // 触发拒收:名单里混入一个**敌方**单位(旧实现会把已受理的先登记、再从名册删掉 → 幽灵)
            var sim = NewSim();
            var b = sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(80, 100));
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(80, 104));
            var enemy = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 96));
            enemy.Hero = HeroId.PanShi;

            var ids = new List<int> { g.Id };            // 我方主将排在前(旧实现会先受理它)
            ids.Add(enemy.Id);                            // 敌方位居其后 → 触发拒收 + 回滚
            foreach (var s in SoldiersOf(sim, g.Id)) ids.Add(s.Id);

            string err;
            bool ok = GarrisonSystem.EnterFormal(sim, ids, b.Id, out err);
            Assert.IsFalse(ok, "混入敌方单位应被拒收");
            Assert.AreEqual("建筑须为己方", err);
            Assert.AreEqual(0, b.GarrisonFormal.Count, "拒收后名册应为空");
            var gg = sim.GetEntity(g.Id);
            Assert.AreEqual(0, gg.GarrisonBuildingId,
                "被回滚的单位必须把 GarrisonBuildingId 清回 0 —— 否则它既不在名册、又被视图隐藏 = 消失");
            AssertNoGhosts(sim, "入场被拒");
        }

        [Test]
        public void 占领易主后_城内士兵不留在敌方名册里()
        {
            // 制作人场景:军队攻占敌方建筑期间点驻军 → 易主时必须把旧名册清干净(士兵留场可见)
            var sim = NewSim();
            var b = sim.SpawnBuilding(Side.Red, BuildingId.Barracks, new GridPos(80, 100));
            var g = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 104));
            var ids = new List<int> { g.Id };
            foreach (var s in SoldiersOf(sim, g.Id)) ids.Add(s.Id);
            string err;
            Assert.IsTrue(GarrisonSystem.EnterFormal(sim, ids, b.Id, out err), "入场应成功 err=" + err);

            b.Side = Side.Blue;                          // 易主
            GarrisonSystem.OnCityCaptured(sim, b);
            Assert.AreEqual(0, b.GarrisonFormal.Count + b.GarrisonTemp.Count, "易主后旧驻军名册应清空");
            foreach (var id in ids)
            {
                var u = sim.GetEntity(id);
                if (u == null) continue;
                Assert.AreEqual(0, u.GarrisonBuildingId, "原驻军应解除登记(否则被视图永久隐藏)");
                Assert.AreEqual(0, u.TempGarrisonId);
            }
            AssertNoGhosts(sim, "易主");
        }

        [Test]
        public void 敌方建筑情报_未侦察为null_侦察后有获取时间()
        {
            var sim = NewSim();
            var far = sim.SpawnBuilding(Side.Red, BuildingId.Barracks, new GridPos(80, 220));
            Assert.IsNull(VisionSystem.LastKnownBuilding(sim, Side.Blue, far.Id),
                "从未侦察过的敌建筑应没有快照(界面显示'？')");

            // 派一名蓝方单位到它旁边 → 进入视野后应产生带时间戳的快照
            var scout = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 214));
            scout.Hero = HeroId.PanShi; scout.Hp = scout.MaxHp = 100f;
            for (int i = 0; i < 30; i++) sim.Step();

            var snap = VisionSystem.LastKnownBuilding(sim, Side.Blue, far.Id);
            Assert.IsNotNull(snap, "侦察到之后应产生情报快照");
            Assert.Greater(snap.Tick, 0, "快照必须带**获取时刻**(界面要标'情报时间')");
            Assert.LessOrEqual(snap.Tick, sim.Tick, "获取时刻不得晚于当前时刻");
        }
    }
}
