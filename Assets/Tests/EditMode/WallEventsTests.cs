using System.Collections.Generic;
using NUnit.Framework;
using IronCurtain.Sim;

namespace IronCurtain.Tests
{
    /// <summary>2026-09-11 屏障特效·Sim 事件:攻墙逐击发 WallDamaged(段/伤害/守方/命中点),击破恰好一条 WallBroken。</summary>
    public class WallEventsTests
    {
        private SimCore sim;
        private readonly List<SimEvent> events = new List<SimEvent>();
        private static GameConfig _cfg;

        [SetUp]
        public void SetUp()
        {
            WallSystem.Reset();
            events.Clear();
            _cfg ??= GameConfigParser.FromJson(UnityEngine.Resources.Load<UnityEngine.TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig(), new SeededRng(7));
            sim.Game = _cfg;
            sim.AddEventListener(events.Add);
        }

        private Entity SpawnWallAttacker()
        {
            // 红方单位顶在蓝中路段上(基地(8,80)+半径20 → 墙脚≈(28,80);GridPos=行,列)
            var u = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 28));
            u.MoveSpeed = 2f; u.Attack = 30f; u.AttackIntervalSec = 1f; u.AttackCooldown = 0f;
            u.Range = 1; u.Hp = u.MaxHp = 100f;
            return u;
        }

        private int Count(SimEventType t)
        {
            int n = 0;
            foreach (var ev in events) if (ev.Type == t) n++;
            return n;
        }

        [Test]
        public void 攻墙逐击发WallDamaged字段正确()
        {
            var u = SpawnWallAttacker();
            for (int i = 0; i < 25; i++) sim.Step(); // 2.5s ≈ 3 刀
            int n = Count(SimEventType.WallDamaged);
            Assert.GreaterOrEqual(n, 2, "攻墙应逐击发 WallDamaged(1s/刀)");
            var c = WallSystem.BaseCenter(Side.Blue);
            foreach (var ev in events)
            {
                if (ev.Type != SimEventType.WallDamaged) continue;
                Assert.AreEqual(2, ev.SubjectId, "命中段=中段");
                Assert.AreEqual(30f, ev.Amount, 0.01f, "Amount=单次伤害");
                Assert.AreEqual((int)Side.Blue, ev.WallSide, "守方=蓝");
                Assert.AreEqual(u.Id, ev.AttackerId, "攻击者=拆墙单位");
                float r = SimVec2.Distance(c, ev.HitPos);
                Assert.AreEqual(WallSystem.Radius, r, WallSystem.WallHalf + 0.1f, "HitPos 应在墙弧带上");
            }
        }

        [Test]
        public void 击破恰好一条WallBroken且此后不再发()
        {
            SpawnWallAttacker();
            for (int i = 0; i < 100; i++) sim.Step(); // 10s × 30/s → 第 7 秒破
            Assert.AreEqual(1, Count(SimEventType.WallBroken), "击破瞬间恰好一条 WallBroken");
            var broken = events.Find(e => e.Type == SimEventType.WallBroken);
            Assert.AreEqual(2, broken.SubjectId, "破段=中段");
            Assert.AreEqual((int)Side.Blue, broken.WallSide, "守方=蓝");
            int dmgBefore = Count(SimEventType.WallDamaged);
            Assert.GreaterOrEqual(dmgBefore, 6, "击破前逐刀 WallDamaged(200/30→7 刀)");
            int stableBroken = Count(SimEventType.WallBroken);
            for (int i = 0; i < 50; i++) sim.Step(); // 破后继续走,不再有墙事件
            Assert.AreEqual(stableBroken, Count(SimEventType.WallBroken), "破后无新 WallBroken");
            Assert.AreEqual(dmgBefore, Count(SimEventType.WallDamaged), "破后无新 WallDamaged(段不再吸收)");
        }

        [Test]
        public void Damage返回值语义()
        {
            Assert.IsFalse(WallSystem.Damage(Side.Blue, 1, 100f), "未归零=false");
            Assert.IsTrue(WallSystem.Damage(Side.Blue, 1, 100f), "归零一击=true");
            Assert.IsFalse(WallSystem.Damage(Side.Blue, 1, 50f), "已破再打=false");
            Assert.IsTrue(WallSystem.IsBroken(Side.Blue, 1));
        }

        [Test]
        public void HitEnemyPanel找段与跳过已破()
        {
            var e = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 28));
            e.Hp = e.MaxHp = 100f;
            var hit = WallSystem.HitEnemyPanel(e, e.PosF);
            Assert.IsNotNull(hit, "顶在蓝中段上应命中");
            Assert.AreEqual(Side.Blue, hit.Value.Owner);
            Assert.AreEqual(2, hit.Value.Panel);
            WallSystem.Damage(Side.Blue, 2, WallSystem.WallHp);
            Assert.IsNull(WallSystem.HitEnemyPanel(e, e.PosF), "段破后不再命中(该角度无其他段)");
            var own = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 31)); // 错开一格(同格出生被拒)
            own.Hp = own.MaxHp = 100f;
            Assert.IsNull(WallSystem.HitEnemyPanel(own, own.PosF), "自家墙不算敌方命中");
        }

        [Test]
        public void PanelCenterPos段中心几何()
        {
            var p = WallSystem.PanelCenterPos(Side.Blue, 2);
            Assert.AreEqual(28f, p.X, 0.01f, "蓝中段中心=基地(8,80)+半径20 正前方");
            Assert.AreEqual(80f, p.Y, 0.01f);
            var c = WallSystem.BaseCenter(Side.Blue);
            for (int panel = 0; panel < WallSystem.Panels; panel++)
            {
                var pc = WallSystem.PanelCenterPos(Side.Blue, panel);
                Assert.AreEqual(WallSystem.Radius, SimVec2.Distance(c, pc), 0.01f, "段" + panel + "中心在弧上");
                Assert.IsTrue(WallSystem.OnWall(Side.Blue, panel, pc), "段" + panel + "中心在本段弧带内");
            }
        }
    }
}
