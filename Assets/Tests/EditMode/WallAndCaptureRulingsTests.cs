
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
        public void 墙血量为200()
        {
            Assert.AreEqual(200f, WallSystem.WallHp);
            Assert.AreEqual(200f, WallSystem.Hp(Side.Blue, 2));
        }

        [Test]
        public void 推进遇墙自动拆到破()
        {
            // 红方单位顶在蓝中路段上:攻击循环自动打墙
            var u = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 30));   // 贴蓝墙(基地8+半径20=28)
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
            Assert.AreEqual(34f, p.X, 1f, "吸附点在墙外 6 米(34=8+20+6)");
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
