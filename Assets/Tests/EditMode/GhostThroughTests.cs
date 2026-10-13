using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 制作人口径:"当两个建模互相卡住 2s 左右时,直接放行穿透同阵营"。
    /// 口径:**2 秒触发 / 0.8 秒窗口 / 仅同阵营 / 不放开墙与敌人**。
    ///
    /// 触发口径的实测修正:一开始做成"按位移停滞 2 秒",**测下来根本触发不到** ——
    /// 单位对撞会被互推挤开(双方都在前进,位移不为 0),真正停滞的是被地形/建筑夹住的单位
    /// (那种卡住穿透也救不了,挡住它的是建筑不是单位)。所以改成数"**被同阵营连续推挤的 tick**"。
    /// </summary>
    public sealed class GhostThroughTests
    {
        private static GameConfig _cfg;

        private static SimCore NewSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig(), new SeededRng(91));
            sim.Game = _cfg;
            return sim;
        }

        private static Entity Unit(SimCore sim, Side side, int row, int col)
        {
            var e = sim.SpawnEntity(side, EntityKind.Unit, new GridPos(row, col));
            e.Hero = HeroId.PanShi; e.Hp = e.MaxHp = 500f;
            e.Attack = 10f; e.AttackIntervalSec = 1f; e.MoveSpeed = 3f; e.Range = 2f;
            return e;
        }

        /// <summary>把 b 摆到 a 旁边 0.4 米(小于 SoftCollide 的最小间距 0.9)。</summary>
        private static void Squeeze(Entity a, Entity b)
        {
            b.PosF = new SimVec2(a.PosF.X + 0.4f, a.PosF.Y);
            b.Pos = b.PosF.ToGrid();
        }

        [Test]
        public void 穿透窗口内_同阵营不再互推_但敌我照旧互推()
        {
            var sim = NewSim();
            var ally = Unit(sim, Side.Blue, 80, 100);
            var mate = Unit(sim, Side.Blue, 80, 108);

            // ① 无窗口:同阵营贴太近会被推开(前置,证明推挤确实在工作)
            Squeeze(ally, mate);
            var a0 = ally.PosF; var m0 = mate.PosF;
            sim.Step();
            float movedNoGhost = SimVec2.Distance(ally.PosF, a0) + SimVec2.Distance(mate.PosF, m0);
            Assert.Greater(movedNoGhost, 0.05f, "前置:没有窗口时同阵营贴太近会被推开");

            // ② 开窗口:同阵营**不再**互推
            Squeeze(ally, mate);
            ally.GhostUntilTick = sim.Tick + NavigationAid.GhostWindowTicks;
            var a1 = ally.PosF; var m1 = mate.PosF;
            sim.Step();
            float movedGhost = SimVec2.Distance(ally.PosF, a1) + SimVec2.Distance(mate.PosF, m1);
            Assert.Less(movedGhost, 0.03f, "窗口内同阵营不应再互推(实测 " + movedGhost.ToString("F3") + "m)");

            // ③ 敌我之间:窗口也**不**放行(卡位是战术要素,也不能让近战滑进敌方后排)
            var enemy = Unit(sim, Side.Red, 80, 116);
            Squeeze(ally, enemy);
            ally.GhostUntilTick = sim.Tick + NavigationAid.GhostWindowTicks;
            var a2 = ally.PosF; var e2 = enemy.PosF;
            sim.Step();
            float movedEnemy = SimVec2.Distance(ally.PosF, a2) + SimVec2.Distance(enemy.PosF, e2);
            Assert.Greater(movedEnemy, 0.05f, "敌我之间不得放行穿透(实测 " + movedEnemy.ToString("F3") + "m)");
        }

        [Test]
        public void 两方都在走且贴着两秒_开穿透窗口_且窗口自然过期()
        {
            // 2026-09-14 口径改版:同阵营的"互卡"只发生在**双方都在行军且贴在一起**时
            // (一方走、一方停 → 走得那方直接穿过,见 `_同阵营移动方穿过站定友军_` 用例)。
            // 这里用最典型的"互卡"现场:两队相向而行,谁也推不动谁 → 2 秒后开窗放行。
            var sim = NewSim();
            var a = Unit(sim, Side.Blue, 80, 100);
            var b = Unit(sim, Side.Blue, 80, 104);
            sim.SetMoveTarget(a.Id, new GridPos(80, 108), true);
            sim.SetMoveTarget(b.Id, new GridPos(80, 96), true);

            // 说明:正常行军时同阵营局部绕行(SteerAroundAllies)会让双方各偏一侧错开、根本贴不住,
            // 所以这里**每 tick 强制把它们挤到一起**来复现"两方都在走、谁也过不去"的极端互卡现场。
            bool opened = false; int openedAt = -1;
            for (int i = 0; i < 80; i++)
            {
                Squeeze(a, b);
                sim.Step();
                if (!opened && (a.GhostUntilTick > sim.Tick || b.GhostUntilTick > sim.Tick))
                { opened = true; openedAt = i; }
            }
            Assert.IsTrue(opened, "双方都在走且贴着 2 秒应开穿透窗口(实测未开)");
            Assert.GreaterOrEqual(openedAt, NavigationAid.GhostAfterTicks - 1,
                "窗口不应早于 2 秒(实测第 " + openedAt + " tick)");

            // 不再互推后,窗口自然过期(不会长期穿模)
            for (int i = 0; i < 40; i++) sim.Step();   // 不再强制挤压
            Assert.LessOrEqual(a.GhostUntilTick, sim.Tick);
            Assert.LessOrEqual(b.GhostUntilTick, sim.Tick);
        }

        [Test]
        public void 同阵营移动方穿过站定友军_站定方原地不动()
        {
            // 2026-09-14 制作人「后续支援被友军卡住」的正面口径:一方在走、另一方站定(待命/交战)
            // → **不互推**:走的一方穿过去,站定的一方一格不动(所以战线不会被打散)。
            var sim = NewSim();
            var holder = Unit(sim, Side.Blue, 80, 104);
            holder.State = EntityState.Attacking;          // 交战站定
            holder.HasMoveTarget = false;
            var mover = Unit(sim, Side.Blue, 80, 100);
            sim.SetMoveTarget(mover.Id, new GridPos(80, 112), true);

            var holderAt = holder.PosF;
            for (int i = 0; i < 40; i++) sim.Step();
            Assert.Less(SimVec2.Distance(holder.PosF, holderAt), 0.05f,
                "站定的友军被移动方推动了(" + SimVec2.Distance(holder.PosF, holderAt).ToString("F3") + "m)");
            Assert.Greater(mover.PosF.X, holderAt.X + 0.3f, "移动方应能穿过站定的友军继续前进");
            Assert.AreEqual(0, holder.AllyPushTicks, "站定的一方不该被计成'互卡'");
        }

        [Test]
        public void 被敌方连续推挤两秒_不会开穿透窗口()
        {
            var sim = NewSim();
            var ally = Unit(sim, Side.Blue, 80, 100);
            var enemy = Unit(sim, Side.Red, 80, 108);

            bool opened = false;
            for (int i = 0; i < 60; i++)
            {
                Squeeze(ally, enemy);
                sim.Step();
                if (ally.GhostUntilTick > sim.Tick || enemy.GhostUntilTick > sim.Tick) opened = true;
            }
            Assert.IsFalse(opened, "被**敌方**推挤不得开穿透窗口(卡位是战术要素)");
        }

        [Test]
        public void 穿透不放行地形_顶着屏障的单位仍被挡住()
        {
            var sim = NewSim();
            var center = WallSystem.BaseCenter(Side.Red);
            var u = Unit(sim, Side.Blue, (int)center.Y, (int)(center.X - WallSystem.Radius - 6f));
            u.PosF = new SimVec2(center.X - WallSystem.Radius - 2f, center.Y);
            u.Pos = u.PosF.ToGrid();
            sim.SetMoveTarget(u.Id, new SimVec2(center.X - WallSystem.Radius + 3f, center.Y).ToGrid(), true);
            for (int i = 0; i < 120; i++) sim.Step();
            float r = SimVec2.Distance(center, u.PosF);
            Assert.GreaterOrEqual(r, WallSystem.Radius - 0.01f,
                "穿透只对同阵营单位生效:单位仍被屏障挡住(实测半径 " + r.ToString("F2") + ")");
        }
    }
}
