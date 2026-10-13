
using System.Linq;
using NUnit.Framework;
using IronCurtain.Sim;

namespace IronCurtain.Tests
{
    /// <summary>2026-09-11 制作人三裁决:墙 HP200/点墙即拆令+遇墙自动拆/敌军贴建筑即时占领。</summary>
    public class WallAndCaptureRulingsTests
    {
        private SimCore sim;

        private static GameConfig _cfg;

        [SetUp]
        public void SetUp()
        {
            WallSystem.Reset();
            _cfg ??= GameConfigParser.FromJson(UnityEngine.Resources.Load<UnityEngine.TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig(), new SeededRng(7));
            sim.Game = _cfg;
        }

        [Test]
        public void 建筑旁有归属方单位时_不得被接触即占领_防红蓝闪烁()
        {
            // 2026-09-14 实机教训:有驻军的兵营被"近战出击"放出守军后,同一 tick 被当无人建筑占领,
            // 紧接着旁边归属方的单位又把它翻回去 → **每 tick 红蓝闪烁**,障碍时有时无 → 绕行永远不成立
            // (制作人:"士兵被卡住的时间太久了,似乎要很久才能过去")。
            // 口径:「只有【无人】建筑才能直接占领」= 无驻军 **且归属方半径内没有任何单位**。
            var bar = sim.SpawnBuilding(Side.Red, BuildingId.Barracks, new GridPos(80, 120));
            var keeper = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 122)); // 就在旁边守着(需在碰撞半径内)
            var foe = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 118));                 // 贴上来
            foe.MoveSpeed = 0f; foe.Attack = 0f;
            for (int i = 0; i < 60; i++)
            {
                sim.Step();
                Assert.AreEqual(Side.Red, bar.Side, "归属方有人在旁守着 → 不得被接触即占领(第 " + i + " tick 翻面了)");
            }
            Assert.IsNotNull(sim.GetEntity(keeper.Id));
        }

        [Test]
        public void 射手拆墙_站在射程外沿就能打_不必贴脸()
        {
            // 2026-09-14 制作人:"攻击屏障时坠日居然要走到屏障脸上才能攻击,他应该可以远程的"。
            // 注:红方墙段是镜像的(`LaneAngleOf` 对红方镜像),所以**不写死段号**,只断言"某一段掉血"。
            var side = Side.Blue; var foe = Side.Red;
            var center = WallSystem.BaseCenter(foe);
            float ang = WallSystem.LaneAngleOf(foe, 2);
            var pos = new SimVec2(center.X + System.MathF.Cos(ang) * (WallSystem.Radius + 6f),
                                  center.Y + System.MathF.Sin(ang) * (WallSystem.Radius + 6f));
            var u = sim.SpawnEntity(side, EntityKind.Unit, new GridPos((int)pos.Y, (int)pos.X));
            var c = sim.Game.Heroes[(int)HeroId.ZhuiRi];
            u.Hero = HeroId.ZhuiRi; u.MaxHp = u.Hp = c.MaxHp; u.Attack = c.Attack;
            u.AttackIntervalSec = (float)c.AttackIntervalSec; u.Range = c.Range; u.MoveSpeed = (float)c.MoveSpeed;

            float hp0 = Enumerable.Range(0, WallSystem.Panels).Sum(i => WallSystem.Hp(foe, i));
            var wallPt = WallSystem.NearestWallPoint(side, u.PosF);
            sim.SetMoveTarget(u.Id, new GridPos((int)wallPt.Y, (int)wallPt.X), true);
            for (int i = 0; i < 10 * sim.Config.TickRate; i++) sim.Step();
            float hp1 = Enumerable.Range(0, WallSystem.Panels).Sum(i => WallSystem.Hp(foe, i));
            float shell = System.Math.Abs(SimVec2.Distance(center, u.PosF) - WallSystem.Radius);
            Assert.Less(hp1, hp0, "射手应能打到屏障(墙段总血 " + hp0 + "→" + hp1 + ")");
            Assert.Greater(shell, 2.0f, "射手不该走到屏障脸上(实测离墙皮 " + shell.ToString("F2") + " 米)");
        }

        [Test]
        public void 墙血量为200()
        {
            Assert.AreEqual(200f, WallSystem.WallHp);
            Assert.AreEqual(200f, WallSystem.Hp(Side.Blue, 2));
        }

        [Test]
        public void 推进遇墙自动拆到破()
        {
            // 红方单位顶在蓝中路段上:攻击循环自动打墙
            var u = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 28));   // 贴蓝墙(基地8+半径20=28)
            u.MoveSpeed = 2f; u.Attack = 30f; u.AttackIntervalSec = 1f; u.AttackCooldown = 0f;
            u.Range = 1; u.Hp = u.MaxHp = 100f;
            for (int i = 0; i < 100; i++) sim.Step(); // 10 秒 × 30/s,200 血在第 7 秒破
            Assert.IsTrue(WallSystem.IsBroken(Side.Blue, 2), "持续攻击后中段屏障应碎");
            Assert.AreEqual(0f, WallSystem.Hp(Side.Blue, 2));
        }

        [Test]
        public void 点击拾取敌方墙与吸附点()
        {
            Assert.IsTrue(WallSystem.HitEnemyWall(sim, Side.Red, new SimVec2(28f, 80f)), "红方点蓝中段墙身(基地8+半径20)");
            Assert.IsFalse(WallSystem.HitEnemyWall(sim, Side.Blue, new SimVec2(28f, 80f)), "自家墙不可作为拆墙目标");
            var p = WallSystem.NearestWallPoint(Side.Red, new SimVec2(28f, 80f));
            // 2026-09-14 口径修正:吸附点必须在**命中带内**,否则军队走到目的地只是站着、打不到屏障。
            // 原口径是"墙外 6 米"(34=8+20+6),那正好落在命中带外 —— 制作人"磐石打不到屏障"的一半原因。
            float off = p.X - 28f;   // 28 = 基地 8 + 半径 20
            Assert.Greater(off, 0f, "吸附点应在墙外侧");
            Assert.LessOrEqual(off, WallSystem.WallHalf + WallSystem.WallHitMargin,
                "吸附点到墙皮的距离必须在命中带内(off=" + off + ")");
        }

        [Test]
        public void 走到墙脚的军队能真正打到屏障()
        {
            // 2026-09-14 制作人"磐石打不到屏障,还被挡着过不去"的回归:
            // 单位**走**到敌方墙脚(而不是被摆到墙皮上),必须能真的把墙血打下去。
            // 旧实现在 CanPass 侧加了 1m 容差 → 走到离墙皮 1.6~1.9 米处停住,而命中判定是 ±0.6 →
            // 一刀都打不到,却仍然发 DamageDealt(看着像"打了没效果")。
            var u = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 40));
            u.MoveSpeed = 3f; u.Attack = 30f; u.AttackIntervalSec = 0.5f; u.AttackCooldown = 0f;
            u.Range = 1f; u.Hp = u.MaxHp = 100f;
            // 下令走到蓝方中路屏障(基地 8 + 半径 20 = x28)
            sim.SetMoveTarget(u.Id, WallSystem.NearestWallPoint(Side.Red, new SimVec2(28f, 80f)).ToGrid(), true);
            float hp0 = WallSystem.Hp(Side.Blue, 2);
            for (int i = 0; i < 300; i++) sim.Step();   // 30 秒:足够走到墙脚并砍碎 200 血
            float r = SimVec2.Distance(WallSystem.BaseCenter(Side.Blue), u.PosF);
            Assert.LessOrEqual(r - WallSystem.Radius, WallSystem.WallHalf + WallSystem.WallHitMargin,
                "单位必须走到命中带内(离墙皮=" + (r - WallSystem.Radius).ToString("F2") + "m)");
            Assert.Less(WallSystem.Hp(Side.Blue, 2), hp0, "走到墙脚后必须真的打掉墙血");
        }

        [Test]
        public void 开着自动行为也能把墙拆掉_自主层不抢拆墙令()
        {
            // 2026-09-14 对照实验锁死:自动行为层曾把"贴墙站定"的单位重新派去索敌,
            // 于是屏障只挨一刀。这里开着 AutoBehavior 跑同一条行军令,必须真的把墙拆了。
            var s = new SimCore(new SimConfig { AutoBehavior = true }, new SeededRng(11));
            s.Game = _cfg;
            var u = s.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 40));
            u.MoveSpeed = 3f; u.Attack = 30f; u.AttackIntervalSec = 0.5f; u.AttackCooldown = 0f;
            u.Range = 1f; u.Hp = u.MaxHp = 100f;
            s.SetMoveTarget(u.Id, WallSystem.NearestWallPoint(Side.Red, new SimVec2(28f, 80f)).ToGrid(), true);
            for (int i = 0; i < 400; i++) s.Step();   // 40 秒
            float r = SimVec2.Distance(WallSystem.BaseCenter(Side.Blue), u.PosF);
            Assert.LessOrEqual(r - WallSystem.Radius, WallSystem.WallHalf + WallSystem.WallHitMargin,
                "贴墙站定(离墙皮=" + (r - WallSystem.Radius).ToString("F2") + "m)");
            Assert.Less(WallSystem.Hp(Side.Blue, 2), 60f,
                "开着自动行为层也要持续输出(实测墙血=" + WallSystem.Hp(Side.Blue, 2).ToString("F1") + ")");
        }

        [Test]
        public void 被挤进薄壳的单位能向外走脱()
        {
            // 2026-09-14:SoftCollide/PushOutOfBuildings 的位移不过墙判定,能把单位挤进薄壳;
            // 薄壳宽 1.2 米而单帧步长只有 0.2~0.3 米 → 若只放行"一步跨出壳"就永远出不来。
            // 带起点的 CanPass 必须放行**壳内向外**的每一步。
            var inside = new SimVec2(28f, 80f);                 // 正在薄壳上(蓝基地 8 + 半径 20)
            var stepOut = new SimVec2(28.25f, 80f);             // 仍在壳内,但更远离基地中心
            var stepIn = new SimVec2(27.75f, 80f);
            Assert.IsFalse(WallSystem.CanPass(Side.Red, inside), "壳上不可通行(敌方)");
            Assert.IsTrue(WallSystem.CanPass(Side.Red, inside, stepOut), "壳内向外的一步必须放行(否则永久卡死)");
            Assert.IsFalse(WallSystem.CanPass(Side.Red, inside, stepIn), "壳内向内仍不可穿");
            Assert.IsFalse(WallSystem.CanPass(Side.Red, new SimVec2(30f, 80f), inside),
                "从壳外往里不给放行(不能靠'起点在壳内'钻空子)");
        }

        [Test]
        public void 敌军贴建筑即时占领()
        {
            var b = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(80, 100));
            var u = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 102));
            u.Hp = u.MaxHp = 100f; // 裸单位默认 0 血会被箭塔当场击杀,占领判定按 Dead 跳过
            sim.Step();
            Assert.AreEqual(Side.Red, b.Side, "敌军贴上即换归属");
            Assert.AreEqual(SiegeIntent.None, b.Intent);
        }

        [Test]
        public void 有驻军建筑不可即时占领()
        {
            var b = sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(80, 100));
            var defender = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 101));
            defender.IsSoldier = true;
            b.GarrisonTemp.Add(defender.Id);
            defender.GarrisonBuildingId = b.Id;
            var u = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 102));
            u.Hp = u.MaxHp = 100f;
            sim.Step();
            Assert.AreEqual(Side.Blue, b.Side, "有人防守不被瞬间占领(2026-09-11 制作人澄清)");
        }

        [Test]
        public void 堡垒被摧毁直接消失不留废墟()
        {
            var fort = sim.SpawnEntity(Side.Blue, EntityKind.Building, new GridPos(80, 100));
            fort.IsFort = true; fort.Building = null;
            fort.Hp = fort.MaxHp = 2f;                      // 设低血量:一击即破,聚焦"消失而非留废墟"这条分支
            var u = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 101));
            u.Hp = u.MaxHp = 200f;
            u.Attack = 5f; u.AttackIntervalSec = 1f; u.AttackCooldown = 0f;   // 必填:裸单位缺省 0 会踩冷却死循环
            u.TargetBuildingId = fort.Id;
            fort.Intent = SiegeIntent.Destroy; fort.IntentIssuer = Side.Red;
            for (int i = 0; i < 40; i++) sim.Step();
            Assert.IsFalse(sim.Entities.ContainsKey(fort.Id), "堡垒打掉即消失");
            Assert.AreEqual(0, sim.Entities.Values.Count(e => e.Kind == EntityKind.Building && e.Rubbled),
                "不留下废墟");
        }

        [Test]
        public void 己方不可对自家堡垒下摧毁令()
        {
            var fort = sim.SpawnEntity(Side.Blue, EntityKind.Building, new GridPos(80, 100));
            fort.IsFort = true; fort.Building = null; fort.Hp = fort.MaxHp = 100f;
            string err;
            Assert.IsFalse(SiegeSystem.SetIntent(sim, fort.Id, Side.Blue, SiegeIntent.Destroy, out err),
                "己方堡垒不可下令摧毁");
            Assert.IsNotNull(err);
        }

        [Test]
        public void 堡垒不可驻军且100血()
        {
            // 数值与入口双验证
            Assert.AreEqual(100, sim.Game.Tools.FortHp, "配置 FortHp=100");
        }

        [Test]
        public void 大本营不可即时占领()
        {
            var b = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(80, 200));
            var u = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 202));
            u.Hp = u.MaxHp = 100f;
            sim.Step();
            Assert.AreEqual(Side.Blue, b.Side, "大本营仍走摧毁胜利,不吃即时占领");
        }

        [Test]
        public void 摧毁意图中的建筑不被路过部队顺手占领()
        {
            var b = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(80, 100));
            b.Intent = SiegeIntent.Destroy;
            b.IntentIssuer = Side.Red;
            var u = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 102));
            u.TargetBuildingId = b.Id;
            // 注意:执行摧毁的单位必须带攻击属性——AttackIntervalSec=0 会让 DestroySystem
            // 的冷却 while 循环死转(历史设计:单位总是来自配置,裸单位缺省 0)
            u.Attack = 5f; u.AttackIntervalSec = 1f; u.AttackCooldown = 1f;
            u.Hp = u.MaxHp = 100f;
            sim.Step();
            Assert.AreEqual(Side.Blue, b.Side, "摧毁进行中不换主");
        }
    }
}
