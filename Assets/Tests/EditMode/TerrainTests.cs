using NUnit.Framework;
using IronCurtain.Sim;

namespace IronCurtain.Tests
{
    public class TerrainTests
    {
        private SimCore sim;

        [SetUp]
        public void SetUp()
        {
            sim = new SimCore(new SimConfig(), new SeededRng(1));
            WallSystem.Reset();
        }

        [Test]
        public void 超椭圆内外判定()
        {
            Assert.IsTrue(TerrainMap.IsInside(new SimVec2(135f, 80f)), "中心在内");
            Assert.IsTrue(TerrainMap.IsInside(new SimVec2(269.9f, 80f)), "长轴右端在内");
            Assert.IsTrue(TerrainMap.IsInside(new SimVec2(0.1f, 80f)), "长轴左端在内");
            Assert.IsTrue(TerrainMap.IsInside(new SimVec2(135f, 159.9f)), "短轴上端在内");
            Assert.IsFalse(TerrainMap.IsInside(new SimVec2(135f, 161f)), "短轴外不在");
            Assert.IsFalse(TerrainMap.IsInside(new SimVec2(270.5f, 80f)), "长轴外不在");
            // 磨圆四角:纯菱形(p=1)的角点区域应在轮廓外
            Assert.IsFalse(TerrainMap.IsInside(new SimVec2(250f, 150f)), "角区(菱形尖)被磨圆排除");
        }

        [Test]
        public void 河与桥()
        {
            Assert.IsTrue(TerrainMap.IsWater(new SimVec2(135f, 45f)), "河心(非桥)是水域");
            Assert.IsFalse(TerrainMap.IsWater(new SimVec2(130f, 80f)), "河外是陆地");
            Assert.IsTrue(TerrainMap.IsBridge(new SimVec2(135f, 80f)), "中路桥");
            Assert.IsTrue(TerrainMap.IsBridge(new SimVec2(135f, 10f)), "上路桥(沿边弧度处)");
            Assert.IsTrue(TerrainMap.IsBridge(new SimVec2(135f, 150f)), "下路桥");
            Assert.IsFalse(TerrainMap.IsBridge(new SimVec2(135f, 30f)), "两桥之间非桥");
            Assert.AreEqual(0.7f, TerrainMap.MoveFactor(new SimVec2(135f, 45f)), "涉水速度 −30%");
            Assert.AreEqual(1f, TerrainMap.MoveFactor(new SimVec2(135f, 80f)), "桥上速度正常");
            Assert.AreEqual(1f, TerrainMap.MoveFactor(new SimVec2(100f, 80f)), "陆地速度正常");
        }

        [Test]
        public void 三路几何()
        {
            Assert.AreEqual(10f, TerrainMap.LaneY(135f, 0), 0.01f, "上路在河心处 y=10");
            Assert.AreEqual(150f, TerrainMap.LaneY(135f, 2), 0.01f, "下路在河心处 y=150");
            Assert.AreEqual(80f, TerrainMap.LaneY(200f, 1), 0.01f, "中路恒 y=80");
            Assert.AreEqual(66f, TerrainMap.LaneY(100f, 1) - TerrainMap.LaneY(100f, 0), 0.5f, "前线间距≈66");
            Assert.AreEqual(43f, TerrainMap.LaneY(40f, 1) - TerrainMap.LaneY(40f, 0), 0.5f, "基地端收窄≈43");
        }

        [Test]
        public void 地表类型()
        {
            Assert.AreEqual(SurfaceKind.Road, TerrainMap.SurfaceOf(new SimVec2(70f, 80f)), "中路车道=石路");
            Assert.AreEqual(SurfaceKind.Water, TerrainMap.SurfaceOf(new SimVec2(135f, 45f)), "河=水");
            Assert.AreEqual(SurfaceKind.Bridge, TerrainMap.SurfaceOf(new SimVec2(135f, 80f)), "桥");
            Assert.AreEqual(SurfaceKind.Sand, TerrainMap.SurfaceOf(new SimVec2(141f, 45f)), "河岸沙地(带内非走廊)");
            Assert.AreEqual(SurfaceKind.Grass, TerrainMap.SurfaceOf(new SimVec2(67.5f, 60f)), "两路之间=草地");
            Assert.AreEqual(SurfaceKind.Road, TerrainMap.SurfaceOf(new SimVec2(30f, 80f)), "主城广场=石路");
        }

        [Test]
        public void 超椭圆边界推出()
        {
            var outside = new SimVec2(135f, 200f);
            var clamped = TerrainMap.ClampToEllipse(outside);
            Assert.IsTrue(TerrainMap.IsInside(clamped), "推出后必在轮廓内");
            Assert.IsFalse(TerrainMap.IsInside(outside), "原点在轮廓外");
        }

        [Test]
        public void 单位移动涉水减速()
        {
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(45, 125));
            u.MoveSpeed = 2f;
            sim.SetMoveTarget(u.Id, new GridPos(45, 150));
            for (int i = 0; i < 10; i++) sim.Step();
            float moved = SimVec2.Distance(u.PosF, SimVec2.FromGrid(new GridPos(45, 125)));
            Assert.Less(moved, 2.0f, "涉水段使 1 秒位移小于 2 米");
            Assert.Greater(moved, 0.5f, "仍在推进");
        }

        [Test]
        public void 轮廓内移动不越界()
        {
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 140));
            u.MoveSpeed = 5f;
            sim.SetMoveTarget(u.Id, new GridPos(120, 268));
            for (int i = 0; i < 100; i++)
            {
                sim.Step();
                Assert.IsTrue(TerrainMap.IsInside(u.PosF), "任意时刻都不得越出轮廓");
            }
        }

        /// <summary>2026-09-11 制作人目视修正:"两方草坪上都有个从上往下走的类似沙地样式的东西,换成草地"。
        /// 根因=SurfaceOf 原"前线石路带"(x∈[100,112] 与 [158,170])把带内非车道处判成 Sand。
        /// 此测试扫描这两条竖带:任何不属车道(Road)的采样点都必须是草地,不允许出现沙地。</summary>
        [Test]
        public void 原前线石路带内非车道处必须是草地而非沙地()
        {
            foreach (float x in new[] { 106f, 164f })
            {
                int road = 0, sand = 0, grass = 0;
                for (int row = 12; row < 148; row++)
                {
                    var v = new SimVec2(x, row + 0.5f);
                    if (!TerrainMap.IsInside(v)) continue;          // 轮廓外不参与判定
                    var k = TerrainMap.SurfaceOf(v);
                    if (k == SurfaceKind.Road) road++;
                    else if (k == SurfaceKind.Sand) sand++;
                    else if (k == SurfaceKind.Grass) grass++;
                }
                Assert.AreEqual(0, sand, "x=" + x + " 竖带内不应再有沙地(实测 " + sand + " 个采样点)");
                Assert.Greater(grass, 0, "x=" + x + " 竖带内应主要为草地");
                Assert.Greater(road, 0, "x=" + x + " 竖带内车道仍应判为石路");
            }
        }

    }
}
