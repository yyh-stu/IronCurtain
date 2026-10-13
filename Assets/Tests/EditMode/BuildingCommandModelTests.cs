using System.Linq;
using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class BuildingCommandModelTests
    {
        private SimCore _sim;
        private BuildingCommandModel _model;
        private Entity _ownTower;
        private Entity _neutral;
        private Entity _enemyTower;
        private Entity _enemyBase;
        private Entity _zeroSoldierGeneral;

        [SetUp]
        public void SetUp()
        {
            VisionSystem.Reset();
            var config = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            _sim = new SimCore(new SimConfig(), new SeededRng(33)) { Game = config };
            _ownTower = _sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 114));
            _neutral = _sim.SpawnBuilding(Side.Neutral, BuildingId.ResourcePoint, new GridPos(15, 122));
            _enemyTower = _sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 130));
            _enemyBase = _sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 136));
            _zeroSoldierGeneral = SpawnBareGeneral(Side.Blue, new GridPos(5, 100));
            _sim.Step(); // 己方塔提供 24m 视野，刷新三个目标建筑。
            _model = new BuildingCommandModel(_sim, Side.Blue);
        }

        private Entity SpawnBareGeneral(Side side, GridPos pos)
        {
            var cfg = _sim.Game.Heroes[(int)HeroId.PanShi];
            var g = _sim.SpawnEntity(side, EntityKind.Unit, pos);
            g.Hero = HeroId.PanShi;
            g.Hp = g.MaxHp = cfg.MaxHp;
            g.MoveSpeed = (float)cfg.MoveSpeed;
            return g;
        }

        [Test]
        public void Describe_严格匹配建筑归属与状态动作矩阵()
        {
            var selected = new[] { _zeroSoldierGeneral.Id };
            var own = _model.Describe(_ownTower.Id, selected);
            Assert.IsFalse(own.CanCapture || own.CanDestroy);

            var neutral = _model.Describe(_neutral.Id, selected);
            Assert.IsTrue(neutral.CanCapture);
            Assert.IsFalse(neutral.CanDestroy);

            var tower = _model.Describe(_enemyTower.Id, selected);
            Assert.IsTrue(tower.CanCapture && tower.CanDestroy);

            var enemyBase = _model.Describe(_enemyBase.Id, selected);
            Assert.IsFalse(enemyBase.CanCapture);
            Assert.IsTrue(enemyBase.CanDestroy);

            _neutral.Building = BuildingId.Base;
            var neutralBase = _model.Describe(_neutral.Id, selected);
            Assert.IsFalse(neutralBase.CanCapture || neutralBase.CanDestroy);
            _neutral.Building = BuildingId.ResourcePoint;

            _enemyTower.Rubbled = true;
            var rubble = _model.Describe(_enemyTower.Id, selected);
            Assert.IsFalse(rubble.CanCapture || rubble.CanDestroy);
            StringAssert.Contains("废墟", rubble.Status);
            _enemyTower.Rubbled = false;
            var back = _model.Describe(_enemyTower.Id, selected);
            Assert.IsTrue(back.CanCapture && back.CanDestroy);   // 废墟复原后恢复可操作(旧"封城"拦截已随字段删除)
        }

        [Test]
        public void Describe_无选中主将时命令仍可见_只是不点兵()
        {
            // 2026-09-14 制作人:"**占领/摧毁是建筑的属性**,或者简单点说,**就是不用先框选**"
            // → 旧口径("没框选就不给命令")已作废。现在:命令照样可用(可见可点),
            //   区别只在"有没有把框选的兵团一起派出去"(SelectedGeneralCount),状态行如实写明。
            var info = _model.Describe(_enemyTower.Id, new int[0]);
            Assert.AreEqual(0, info.SelectedGeneralCount);
            Assert.IsTrue(info.CanCapture, "不用先框选也能下占领令(建筑属性)");
            Assert.IsTrue(info.CanDestroy, "不用先框选也能下摧毁令(建筑属性)");
            StringAssert.Contains("未框选", info.Status);
        }

        [Test]
        public void Describe_有选中主将时命令可用且状态报兵团数()
        {
            var sim = _sim;
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi,
                new GridPos(_enemyTower.Pos.Row, _enemyTower.Pos.Col - 6));
            var info = _model.Describe(_enemyTower.Id, new[] { g.Id });
            Assert.AreEqual(1, info.SelectedGeneralCount);
            Assert.IsTrue(info.CanCapture && info.CanDestroy);
            StringAssert.Contains("已选", info.Status);
        }

        [Test]
        public void Execute_零兵武将可单独受令_写下意图并去集结点()
        {
            // 2026-09-14 制作人口径「主将和他的兵团就是一个单位」:士兵打光的**孤将**照样能受令
            // (它自己进城,按交换力 10/12 结算)。旧用例断言"零兵武将必须被拒",与这条口径冲突,已改写。
            var result = _model.Execute(
                _neutral.Id, new[] { _zeroSoldierGeneral.Id }, BuildingCommandAction.Capture);

            Assert.IsTrue(result.Success, "孤将应能受令:" + result.Message);
            Assert.AreEqual(SiegeIntent.Capture, _neutral.Intent, "应写下占领意图");
            Assert.IsTrue(_zeroSoldierGeneral.HasMoveTarget, "孤将应获行军令(去城外集结点)");
            Assert.AreEqual(_neutral.Id, _zeroSoldierGeneral.TargetBuildingId, "孤将应带攻城标记");
        }

        [Test]
        public void Execute_成功回报士兵数且主将获城外集结点()
        {
            var general = RegimentSystem.SpawnGeneral(
                _sim, Side.Blue, HeroId.PanShi, new GridPos(25, 100));
            var soldiers = _sim.Entities.Values.Where(e => e.IsSoldier && e.GeneralId == general.Id).ToList();

            var result = _model.Execute(
                _enemyTower.Id, new[] { general.Id }, BuildingCommandAction.Destroy);

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(RegimentSystem.SoldiersPerGeneral, result.SoldierCount);
            StringAssert.Contains(result.SoldierCount.ToString(), result.Message);
            // 2026-09-09 制作人二次裁决:兵随主将——令下主将(标记+行军),士兵不受令随行,到位放兵
            Assert.IsTrue(general.HasMoveTarget, "主将应受令行军");
            Assert.AreEqual(_enemyTower.Id, general.TargetBuildingId, "主将带兵团攻城标记(主将与兵团同一单位)");
            var st = SimVec2.FromGrid(general.MoveTarget);
            var bp = SimVec2.FromGrid(_enemyTower.Pos);
            float d = SimVec2.Distance(st, bp);
            // 2026-09-15 口径修正(制作人"**还是不攻击建筑,全在发呆**"):**摧毁令**下主将也是打手,
            // 集结点必须落在**它的攻击距离内**。旧口径一律用"碰撞半径+5"(箭塔=7.4 米),而近战要贴到
            // 占地矩形边(≈2.5 米)→ 到了 7.4 米就停住、永远打不到 = 站桩发呆。
            // 旧断言 `d > 3f && d < 12f`(语义是"停在城外 5 米")正是这个 bug 的编码,按新口径改写。
            Assert.IsTrue(SimCore.InMeleeReachOfBuilding(_enemyTower, st),
                "摧毁令的集结点必须落在主将打得到的位置:距塔心 " + d.ToString("F2")
                + ",距占地矩形 " + SimVec2.Distance(st, BuildingFootprint.ClosestPoint(_enemyTower, st)).ToString("F2"));
            Assert.IsTrue(soldiers.All(s => !s.HasMoveTarget && s.TargetBuildingId == 0), "下令时士兵不受令(编队随行)");
            // 模拟主将抵达城外 → 放兵入交换
            general.HasMoveTarget = false;
            general.PosF = new SimVec2(bp.X + 3f, bp.Y + 3f);
            _sim.Step();
            Assert.IsTrue(soldiers.All(s => s.HasMoveTarget && s.TargetBuildingId == _enemyTower.Id), "到位后士兵入场");
        }

        [Test, Repeat(5)]
        public void PickVisibleBuilding_命中占地且拒绝迷雾目标()
        {
            Assert.AreEqual(_ownTower.Id, BuildingCommandModel.PickVisibleBuilding(
                _sim, Side.Blue, SimVec2.FromGrid(_ownTower.Pos)));
            Assert.AreEqual(_enemyTower.Id, BuildingCommandModel.PickVisibleBuilding(
                _sim, Side.Blue, SimVec2.FromGrid(_enemyTower.Pos)));

            var unknown = _sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 250));
            Assert.AreEqual(0, BuildingCommandModel.PickVisibleBuilding(
                _sim, Side.Blue, SimVec2.FromGrid(unknown.Pos)));
        }

        [Test]
        public void DecideWorldClick_空地关闭目标但保留选择语义()
        {
            Assert.AreEqual(BuildingWorldClickDecision.SelectGeneral,
                BuildingCommandModel.DecideWorldClick(true, 7, 9));
            Assert.AreEqual(BuildingWorldClickDecision.TargetBuilding,
                BuildingCommandModel.DecideWorldClick(true, 0, 9));
            Assert.AreEqual(BuildingWorldClickDecision.CloseTargetOnly,
                BuildingCommandModel.DecideWorldClick(true, 0, 0));
            Assert.AreEqual(BuildingWorldClickDecision.ClearSelection,
                BuildingCommandModel.DecideWorldClick(false, 0, 0));
        }
    }
}
