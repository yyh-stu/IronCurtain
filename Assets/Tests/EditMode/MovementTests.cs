using System;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class MovementTests
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

        [Test]
        public void UniformAdvance_2CellsPerSecond()
        {
            var sim = NewSim();
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 108));
            u.MoveSpeed = 2f;
            sim.SetMoveTarget(u.Id, new GridPos(14, 118));
            for (int i = 0; i < 50; i++) sim.Step(); // 2格/s × 5s × 10tick/s
            Assert.AreEqual(new GridPos(14, 118), u.Pos);
            Assert.AreEqual(EntityState.Idle, u.State);
            Assert.IsFalse(u.HasMoveTarget);
        }

        [Test]
        public void StraightLine_ContinuousMove()
        {
            // 连续坐标:直线移动,不锁列(替代旧 ColumnFirst_ThenRow 格点语义)
            var sim = NewSim();
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 108));
            u.MoveSpeed = 1f;
            sim.SetMoveTarget(u.Id, new GridPos(16, 111));
            for (int i = 0; i < 10; i++) sim.Step(); // 1 米/s × 1s = 1 米(对角线直线)
            Assert.AreEqual(1f, SimVec2.Distance(u.PosF, SimVec2.FromGrid(new GridPos(14, 108))), 0.15f, "10 tick 应走 1 米");
            for (int i = 0; i < 60; i++) sim.Step(); // 总 7s → 到 (2,3)
            Assert.AreEqual(new GridPos(16, 111), u.Pos);
            Assert.AreEqual(EntityState.Idle, u.State);
        }

        [Test]
        public void SoftCollide_PushPastBlockingUnit()
        {
            // 连续坐标软碰撞:无占格排队,单位互相推开,阻挡者被挤动,行路者最终到达
            var sim = NewSim();
            var a = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 113)); // 无目标,原地不动
            var b = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 112));
            b.MoveSpeed = 2f;
            sim.SetMoveTarget(b.Id, new GridPos(14, 117));
            float minDist = 10f;
            for (int i = 0; i < 120; i++)
            {
                sim.Step();
                minDist = System.Math.Min(minDist, SimVec2.Distance(a.PosF, b.PosF));
            }
            Assert.AreEqual(new GridPos(14, 117), b.Pos, "B 应到达目标");
            Assert.AreEqual(EntityState.Idle, b.State);
            Assert.GreaterOrEqual(minDist, 0.7f, "推挤过程中单位间距不得塌陷(软碰撞生效)");
        }

        [Test]
        public void Arrival_StopsAndClearsTarget()
        {
            var sim = NewSim();
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(15, 109));
            u.MoveSpeed = 3f;
            sim.SetMoveTarget(u.Id, new GridPos(15, 112));
            for (int i = 0; i < 30; i++) sim.Step();
            Assert.IsFalse(u.HasMoveTarget);
            Assert.AreEqual(EntityState.Idle, u.State);
            Assert.AreEqual(new GridPos(15, 112), u.Pos);
        }

        [Test]
        public void InvalidTargets_Throw()
        {
            var sim = NewSim();
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 108));
            var bd = sim.SpawnEntity(Side.Red, EntityKind.Building, new GridPos(16, 110));
            Assert.Throws<InvalidOperationException>(() => sim.SetMoveTarget(u.Id, new GridPos(0, LaneGrid.Cols)));
            Assert.Throws<InvalidOperationException>(() => sim.SetMoveTarget(u.Id, new GridPos(LaneGrid.Rows, 0)));
            Assert.Throws<InvalidOperationException>(() => sim.SetMoveTarget(bd.Id, new GridPos(16, 111)));
            Assert.Throws<InvalidOperationException>(() => sim.SetMoveTarget(999, new GridPos(14, 108)));
        }

        [Test]
        public void AdvanceEvents_MatchCellsCrossed()
        {
            var sim = NewSim();
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 108));
            u.MoveSpeed = 2f;
            sim.SetMoveTarget(u.Id, new GridPos(14, 114));
            int advanced = 0;
            sim.AddEventListener(e => { if (e.Type == SimEventType.UnitAdvanced) advanced++; });
            for (int i = 0; i < 40; i++) sim.Step();
            Assert.AreEqual(6, advanced);
        }

        // ===== M8.1a 占地矩形 =====

        [Test]
        public void Footprint_TableValues_AndCollisionRadius()
        {
            var sim = NewSim();
            var baseB = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 100));
            Assert.AreEqual(2.5f, BuildingFootprint.HalfW(baseB), "基地半宽");
            Assert.AreEqual(2.5f, BuildingFootprint.HalfH(baseB), "基地半高");
            var tow = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 108));
            Assert.AreEqual(2.0f, BuildingFootprint.HalfW(tow), "箭塔半宽");
            Assert.AreEqual(2.0f, BuildingFootprint.HalfH(tow), "箭塔半高");
            var bar = sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(15, 116));
            Assert.AreEqual(2.5f, BuildingFootprint.HalfW(bar), "兵营半宽");
            Assert.AreEqual(2.0f, BuildingFootprint.HalfH(bar), "兵营半高");
            var rp = sim.SpawnBuilding(Side.Blue, BuildingId.ResourcePoint, new GridPos(15, 124));
            Assert.AreEqual(2.0f, BuildingFootprint.HalfW(rp), "资源点半宽");
            Assert.AreEqual(2.0f, BuildingFootprint.HalfH(rp), "资源点半高");
            var fort = sim.SpawnEntity(Side.Blue, EntityKind.Building, new GridPos(15, 132));
            fort.IsFort = true;
            Assert.AreEqual(1.5f, BuildingFootprint.HalfW(fort), "堡垒半宽");
            Assert.AreEqual(1.5f, BuildingFootprint.HalfH(fort), "堡垒半高");
            Assert.AreEqual(2.9f, SimCore.BuildingCollisionRadius(bar), "外接半径=Max(2.5,2.0)+0.4");
        }

        [Test]
        public void Footprint_CellsOccupied_Counts()
        {
            var sim = NewSim();
            Assert.AreEqual(25, BuildingFootprint.CellsOccupied(sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 60))).Count, "基地 5×5");
            Assert.AreEqual(16, BuildingFootprint.CellsOccupied(sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 66))).Count, "箭塔 4×4");
            Assert.AreEqual(20, BuildingFootprint.CellsOccupied(sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, new GridPos(15, 71))).Count, "兵营 5×4");
            Assert.AreEqual(16, BuildingFootprint.CellsOccupied(sim.SpawnBuilding(Side.Blue, BuildingId.ResourcePoint, new GridPos(15, 76))).Count, "资源点 4×4");
            var fort = sim.SpawnEntity(Side.Blue, EntityKind.Building, new GridPos(15, 81));
            fort.IsFort = true;
            Assert.AreEqual(9, BuildingFootprint.CellsOccupied(fort).Count, "堡垒 3×3");
        }

        [Test]
        public void Footprint_Contains_And_ClosestPoint()
        {
            var sim = NewSim();
            var b = sim.SpawnBuilding(Side.Red, BuildingId.Barracks, new GridPos(15, 120)); // 中心(120.5,15.5) 矩形 x[118,123] y[13.5,17.5]
            Assert.IsTrue(BuildingFootprint.Contains(b, new SimVec2(120.5f, 15.5f)), "中心在矩形内");
            Assert.IsTrue(BuildingFootprint.Contains(b, new SimVec2(118f, 13.5f)), "边界角在矩形内(含边界)");
            Assert.IsFalse(BuildingFootprint.Contains(b, new SimVec2(117.9f, 15.5f)), "矩形外左侧");
            // 点在矩形外 → 最近点
            var near = BuildingFootprint.ClosestPoint(b, new SimVec2(115f, 15.5f));
            Assert.AreEqual(118f, near.X, 1e-4f, "外部点沿 X 贴到矩形左缘");
            Assert.AreEqual(15.5f, near.Y, 1e-4f);
            near = BuildingFootprint.ClosestPoint(b, new SimVec2(115f, 10f));
            Assert.AreEqual(118f, near.X, 1e-4f, "外部斜向点贴到角");
            Assert.AreEqual(13.5f, near.Y, 1e-4f);
            // 点在矩形内 → 最小穿透轴推出
            var pushed = BuildingFootprint.ClosestPoint(b, new SimVec2(118.3f, 15.5f));
            Assert.AreEqual(118f, pushed.X, 1e-4f, "浅穿 X 轴,推出到左缘");
            Assert.AreEqual(15.5f, pushed.Y, 1e-4f);
            pushed = BuildingFootprint.ClosestPoint(b, new SimVec2(120.5f, 17.4f));
            Assert.AreEqual(120.5f, pushed.X, 1e-4f);
            Assert.AreEqual(17.5f, pushed.Y, 1e-4f, "浅穿 Y 轴,推出到上缘");
        }

        [Test]
        public void BuildingRectPush_UnitStaysOutsideFootprint()
        {
            // 推出有效/不穿墙:单位撞向兵营占地,被矩形推出且不得停在矩形内部
            var sim = NewSim();
            var bar = sim.SpawnBuilding(Side.Red, BuildingId.Barracks, new GridPos(15, 120)); // x∈[118,123]
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(15, 116));
            u.MoveSpeed = 2f;
            sim.SetMoveTarget(u.Id, new GridPos(15, 130));
            for (int i = 0; i < 80; i++)
            {
                sim.Step();
                var cp = BuildingFootprint.ClosestPoint(bar, u.PosF);
                Assert.IsFalse(BuildingFootprint.Contains(bar, u.PosF) && SimVec2.Distance(u.PosF, cp) > 0.05f,
                    "单位不得深入占地矩形(贴边/绕行合法,穿透>0.05 违规)");
            }
            Assert.IsTrue(u.State == EntityState.Moving || u.State == EntityState.Idle, "绕行后仍在行军或已抵达");
        }
    }
}
