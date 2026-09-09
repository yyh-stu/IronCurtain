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
            _enemyTower.Sealed = true;
            var sealedInfo = _model.Describe(_enemyTower.Id, selected);
            Assert.IsFalse(sealedInfo.CanCapture || sealedInfo.CanDestroy);
            StringAssert.Contains("封城", sealedInfo.Status);
        }

        [Test]
        public void Describe_无选中主将时不显示动作()
        {
            var info = _model.Describe(_enemyTower.Id, new int[0]);
            Assert.AreEqual(0, info.SelectedGeneralCount);
            Assert.IsFalse(info.CanCapture || info.CanDestroy);
            StringAssert.Contains("选择", info.Status);
        }

        [Test]
        public void Execute_零兵武将返回具体NoEligible反馈且不写意图()
        {
            var result = _model.Execute(
                _neutral.Id, new[] { _zeroSoldierGeneral.Id }, BuildingCommandAction.Capture);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SiegeOrderError.NoEligibleSoldiers, result.Error);
            StringAssert.Contains("没有可下令士兵", result.Message);
            Assert.AreEqual(SiegeIntent.None, _neutral.Intent);
            Assert.IsFalse(_zeroSoldierGeneral.HasMoveTarget);
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
            Assert.AreEqual(_enemyTower.Id, general.TargetBuildingId, "主将带兵团攻城标记(不参与交换)");
            var st = SimVec2.FromGrid(general.MoveTarget);
            var bp = SimVec2.FromGrid(_enemyTower.Pos);
            float d = SimVec2.Distance(st, bp);
            Assert.IsTrue(d > 3f && d < 12f, "集结点应贴近建筑外缘(碰撞半径+5),实际 " + d);
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
