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
        }

        [Test]
        public void 椭圆内外判定()
        {
            Assert.IsTrue(TerrainMap.IsInside(new SimVec2(143f, 15f)), "中心在内");
            Assert.IsTrue(TerrainMap.IsInside(new SimVec2(285.9f, 15f)), "长轴右端在内");
            Assert.IsTrue(TerrainMap.IsInside(new SimVec2(0.1f, 15f)), "长轴左端在内");
            Assert.IsTrue(TerrainMap.IsInside(new SimVec2(143f, 29.9f)), "短轴上端在内");
            Assert.IsFalse(TerrainMap.IsInside(new SimVec2(143f, 31f)), "短轴外不在");
            Assert.IsFalse(TerrainMap.IsInside(new SimVec2(286.5f, 15f)), "长轴外不在");
        }

        [Test]
        public void 河与桥与中央岛()
        {
            Assert.IsTrue(TerrainMap.IsWater(new SimVec2(143f, 8f)), "河心(非岛区)是水域");
            Assert.IsFalse(TerrainMap.IsWater(new SimVec2(143f, 15f)), "中央岛不是水域");
            Assert.IsTrue(TerrainMap.IsBridge(new SimVec2(143f, 27f)), "上方桥是桥");
            Assert.IsTrue(TerrainMap.IsBridge(new SimVec2(143f, 2f)), "下方桥是桥");
            Assert.AreEqual(0.5f, TerrainMap.MoveFactor(new SimVec2(143f, 8f)), "涉水速度减半");
            Assert.AreEqual(1f, TerrainMap.MoveFactor(new SimVec2(143f, 27f)), "桥上速度正常");
            Assert.AreEqual(1f, TerrainMap.MoveFactor(new SimVec2(100f, 15f)), "陆地速度正常");
        }

        [Test]
        public void 椭圆边界推出()
        {
            var outside = new SimVec2(143f, 50f);
            var clamped = TerrainMap.ClampToEllipse(outside);
            Assert.IsTrue(TerrainMap.IsInside(clamped), "推出后必在椭圆内");
            Assert.IsFalse(TerrainMap.IsInside(outside), "原点在椭圆外");
        }

        [Test]
        public void 单位移动涉水减速()
        {
            // 直穿河:单位从蓝侧走到红侧,途中经水域,位移应小于 2 米/s 的纯速
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(15, 130));
            u.MoveSpeed = 2f;
            sim.SetMoveTarget(u.Id, new GridPos(15, 156));
            for (int i = 0; i < 10; i++) sim.Step(); // 1 秒:部分在河内 → 位移 < 2
            float moved = SimVec2.Distance(u.PosF, SimVec2.FromGrid(new GridPos(15, 130)));
            Assert.Less(moved, 2.0f, "涉水段使 1 秒位移小于 2 米");
            Assert.Greater(moved, 0.5f, "仍在推进");
        }

        [Test]
        public void 椭圆内移动不越界()
        {
            var u = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(15, 140));
            u.MoveSpeed = 5f;
            sim.SetMoveTarget(u.Id, new GridPos(25, 285));
            for (int i = 0; i < 100; i++)
            {
                sim.Step();
                Assert.IsTrue(TerrainMap.IsInside(u.PosF), "任意时刻都不得越出椭圆");
            }
        }
    }
}
