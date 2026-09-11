using System.Linq;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>2026-09-12 AI 智能化第一步:威胁响应触发器——
    /// ①敌兵压境 → 就近士兵入城驻守;②城池被攻 → 调闲置主将回防。纯规则,确定性种子。</summary>
    public class AiDefenseTests
    {
        private static GameConfig _cfg;
        private SimCore sim;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig(), new SeededRng(5));
            sim.Game = _cfg;
            AiSystem.Reset();
        }

        [TearDown]
        public void TearDown() => AiSystem.Reset();

        private void Run(int ticks) { for (int i = 0; i < ticks; i++) sim.Step(); }

        [Test]
        public void 敌兵压境_AI征就近士兵入城驻守()
        {
            var barracks = sim.SpawnBuilding(Side.Red, BuildingId.Barracks, new GridPos(80, 200));
            var redG = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(78, 200)); // 兵团在城旁
            // 蓝方近战兵团压境(距城约 10m,塔视野 24 内可见;近战射程够不着,不会当场开打)
            RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(80, 193)); // 纵队在主将身后,摆近确保压境计数达标
            AiSystem.Configure(sim, Side.Red, AiTier.Hard);

            Assert.AreEqual(0, barracks.GarrisonTemp.Count + barracks.GarrisonFormal.Count, "开局无人驻守");
            Run(25);   // 决策 tick=20 时触发

            Assert.Greater(barracks.GarrisonTemp.Count + barracks.GarrisonFormal.Count, 0,
                "敌兵压境(≥3 可见敌兵在 10m 内)→ 就近士兵应入城驻守,实测临时+正式="
                + barracks.GarrisonTemp.Count + "+" + barracks.GarrisonFormal.Count);
        }

        [Test]
        public void 简单档前线失守_草坪塔驻军参战回防()
        {
            // 制作人补例(2026-09-12):简单档只攻不防,但前线失守时草坪箭塔驻军要回防(收复)
            var towerA = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 205));   // 中路前线点
            var lawn = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(45, 60));     // 草坪塔(不在路带)
            var g1 = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.DuanFeng, new GridPos(78, 205));
            var g2 = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(45, 58));
            AiSystem.Configure(sim, Side.Red, AiTier.Simple);

            Run(80);   // 立足:g1 驻前线塔、g2 驻草坪塔;OwnedSet 快照记录

            // 真实失守:撤走塔旁全部红军(否则贴身即时占领会立刻翻回,掩盖行为),蓝兵进城驻守
            foreach (var e in sim.Entities.Values.ToList())
                if (e != null && e.Side == Side.Red && e.Kind == EntityKind.Unit
                    && SimVec2.Distance(e.PosF, new SimVec2(205f, 80f)) < 14f)
                    sim.RemoveEntity(e.Id);
            var defender = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(79, 204));
            defender.IsSoldier = true; defender.Hp = defender.MaxHp = 100f; defender.GeneralId = 0;
            defender.GarrisonBuildingId = towerA.Id;
            towerA.GarrisonFormal.Add(defender.Id);   // 有守军 → 红方无法即时翻回
            towerA.Side = Side.Blue;
            Run(40);   // 决策 tick:失地 → Reclaim → 草坪塔驻将 g2 参战

            var moving = sim.Entities.Values.Count(e => e != null && e.Side == Side.Red
                && e.Kind == EntityKind.Unit && (e.HasMoveTarget || e.TargetBuildingId != 0));
            Assert.IsTrue(g2.HasMoveTarget || g2.State == EntityState.Moving || g2.TargetBuildingId != 0,
                "前线失守 → 草坪塔驻军(g2)应参与收复回防(g2: HasMoveTarget=" + g2.HasMoveTarget
                + " Target=" + g2.TargetBuildingId + " State=" + g2.State + " Pos=" + g2.PosF
                + "; 全红方动员数=" + moving + "; " + AiSystem.DebugStateOf(Side.Red) + ")");
        }

        [Test]
        public void 二线城被攻_AI调闲置主将回防()
        {
            // 用资源点(二线,非驻防点)验回防令:驻防点的将移动属立足行为,会干扰旧断言
            // 塔在一线做驻防点(吸走立足期的移动令),res 在二线被攻 → 回防令=新目标可分辨
            sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 205));
            var res = sim.SpawnBuilding(Side.Red, BuildingId.ResourcePoint, new GridPos(80, 215));
            // g1 距塔最近 → 立足期被派驻一线(吃掉驻防令);g2 驻 res 旁保持真闲置 → 回防令的断言对象
            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.DuanFeng, new GridPos(70, 210));
            var g2 = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(78, 220));
            AiSystem.Configure(sim, Side.Red, AiTier.Hard);

            Run(21);
            res.Hp -= 20f;                 // 模拟被攻(血下降)
            Run(20);                       // 下个决策 tick:underAttack → ThreatRespond 回防令

            bool gotDefendOrder = g2.HasMoveTarget;   // 立足期只派驻 g1,g2 的任何移动令都来自回防
            Assert.IsTrue(gotDefendOrder,
                "二线城掉血 → 闲置主将应收到回防移动令(g2 HasMoveTarget=" + g2.HasMoveTarget + ")");
        }
    }
}
