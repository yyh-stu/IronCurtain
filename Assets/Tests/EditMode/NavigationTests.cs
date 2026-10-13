using System;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class NavigationTests
    {
        private static GameConfig _cfg;
        private static SimCore NewSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig(), new SeededRng(1));
            sim.Game = _cfg;
            return sim;
        }

        private static bool RunUntil(SimCore sim, Func<bool> cond, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                sim.Step();
                if (cond()) return true;
            }
            return false;
        }

        /// <summary>点在建筑占地内部(严格内部,边缘 0.05m 容差不算,兼容推出贴边语义)。</summary>
        private static bool InsideAnyBuilding(SimCore sim, SimVec2 p)
        {
            foreach (var b in sim.Entities.Values)
            {
                if (b.Kind != EntityKind.Building || b.Rubbled) continue;
                var c = SimVec2.FromGrid(b.Pos);
                float hw = BuildingFootprint.HalfW(b) - 0.05f, hh = BuildingFootprint.HalfH(b) - 0.05f;
                if (p.X > c.X - hw && p.X < c.X + hw && p.Y > c.Y - hh && p.Y < c.Y + hh)
                    return true;
            }
            return false;
        }

        [Test]
        public void T1_GeneralDetoursAroundBuilding()
        {
            var sim = NewSim();
            var bar = sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 110)); // 用**大本营**当稳定障碍:永不可占领(兵营会被接触即占领,不是稳定障碍)
            // 2026-09-14:「该兵营必须带驻军」 —— 否则蓝方单位一贴近就被「接触即占领」翻成蓝方,
            // 它就从障碍变成"自家建筑"(自家建筑被卡 1s 可穿透) → 本用例的前提消失(实测踩到过)。
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 105));
            sim.SetMoveTarget(g.Id, new GridPos(15, 116)); // 直线穿建筑正中

            var goal = SimVec2.FromGrid(new GridPos(15, 116));
            bool arrived = false;
            int ticks = 0;
            while (ticks < 1200 && !arrived)
            {
                sim.Step();
                ticks++;
                if (ticks % 5 == 0)
                    Assert.IsFalse(InsideAnyBuilding(sim, g.PosF), "主将不得处于建筑占地内");
                arrived = SimVec2.Distance(g.PosF, goal) < 1.0f;
            }
            Assert.IsTrue(arrived, $"主将应绕行抵达目标,实际位置 {g.PosF},tick={ticks}");
        }

        [Test]
        public void T2_SoldierDetoursToBlockedSlot()
        {
            var sim = NewSim();
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 105));
            g.FacingOverride = new SimVec2(-1f, 0f); // 阵型转向 +x 侧,后排槽位落入兵营占地
            sim.SpawnBuilding(Side.Red, BuildingId.Barracks, new GridPos(15, 110)); // x∈[108,113]

            bool detourSeen = false;
            for (int i = 0; i < 1500; i++)
            {
                sim.Step();
                if (!detourSeen)
                    foreach (var e in sim.Entities.Values)
                        if (e.IsSoldier && e.Detour.HasValue) { detourSeen = true; break; }
            }
            Assert.IsTrue(detourSeen, "被堵槽位的士兵应触发旁路绕行");

            foreach (var e in sim.Entities.Values)
            {
                if (!e.IsSoldier) continue;
                Assert.IsFalse(InsideAnyBuilding(sim, e.PosF), "士兵不得停留在建筑占地内");
            }
            bool anyDocked = false;
            foreach (var e in sim.Entities.Values)
            {
                if (!e.IsSoldier) continue;
                float d = SimVec2.Distance(e.PosF, g.PosF + RegimentSystem.SlotOffset(g, e.FormationSlot));
                if (d > 0f && d <= 4.0f) anyDocked = true;
            }
            Assert.IsTrue(anyDocked, "至少一名士兵绕行后停靠槽位附近(0,4.0m]");
        }

        [Test]
        public void T3_NoFalseDetourOnFreeMarch()
        {
            var sim = NewSim();
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(15, 100));
            u.MoveSpeed = 6f;
            sim.SetMoveTarget(u.Id, new GridPos(15, 140));

            var goal = SimVec2.FromGrid(new GridPos(15, 140));
            int ticks = 0;
            while (ticks < 400 && SimVec2.Distance(u.PosF, goal) > 0.15f)
            {
                sim.Step();
                ticks++;
                if (ticks % 10 == 0)
                    Assert.IsFalse(u.Detour.HasValue, "空地行军不得触发绕行");
                if (ticks >= 200) break;
            }
            Assert.IsTrue(ticks < 200, $"未绕路应在 200 tick 内抵达,实际 {ticks}");
        }

        [Test]
        public void T4_StuckThresholdTiming()
        {
            var sim = NewSim();
            var bar = sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 110));   // 稳定障碍:大本营永不可占领
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 105));
            sim.SetMoveTarget(g.Id, new GridPos(15, 116));

            int stuckAt15 = -1, firstDetour = -1;
            for (int i = 1; i <= 300; i++)
            {
                sim.Step();
                if (stuckAt15 < 0 && (g.StuckTicks >= NavigationAid.StuckThresholdTicks || g.Detour.HasValue)) stuckAt15 = i; // 达阈值同 tick 即清零设 Detour,二者任一都算
                if (firstDetour < 0 && g.Detour.HasValue) firstDetour = i;
                if (stuckAt15 >= 0 && firstDetour >= 0) break;
            }
            Assert.GreaterOrEqual(stuckAt15, 15, "卡死计数不得早于 15 tick 达阈值");
            Assert.GreaterOrEqual(firstDetour, 0, "应触发绕行");
            Assert.LessOrEqual(firstDetour, stuckAt15 + 20, "绕行应在阈值达成后 20 tick 内出现(1.5s+裕量)");
        }
    }
}
