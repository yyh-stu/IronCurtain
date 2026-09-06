using System;
using System.Collections.Generic;
using IronCurtain.Sim;
using NUnit.Framework;

namespace IronCurtain.Tests
{
    public sealed class SimSmokeTests
    {
        private static SimCore NewSim(ulong seed = 1)
            => new SimCore(new SimConfig(), new SeededRng(seed));

        [Test]
        public void Step_5000Times_TickMatchesAndNoThrow()
        {
            var sim = NewSim();
            for (int i = 0; i < 5000; i++) sim.Step();
            Assert.AreEqual(5000, sim.Tick);
        }

        [Test]
        public void SpawnRemove_CountAndDuplicateCellThrows()
        {
            var sim = NewSim();
            sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(0, 0));
            sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(1, 0));
            sim.SpawnEntity(Side.Blue, EntityKind.Building, new GridPos(2, 0));
            Assert.AreEqual(3, sim.EntityCount);

            Assert.IsTrue(sim.RemoveEntity(2));
            Assert.AreEqual(2, sim.EntityCount);
            Assert.IsNull(sim.GetEntity(2));

            // 移除后原占用释放,可再次生成
            sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(1, 0));
            Assert.AreEqual(3, sim.EntityCount);
            // 仍被占用的格子必须抛异常
            Assert.Throws<InvalidOperationException>(
                () => sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(0, 0)));
        }

        [Test]
        public void SeededRng_DeterministicPerSeed()
        {
            var a = new SeededRng(123);
            var b = new SeededRng(123);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextInt(0, 100), b.NextInt(0, 100));

            var c = new SeededRng(124);
            bool anyDiff = false;
            for (int i = 0; i < 1000; i++)
                if (a.NextInt(0, 100) != c.NextInt(0, 100)) { anyDiff = true; break; }
            Assert.IsTrue(anyDiff);
        }

        [Test]
        public void LaneGrid_BoundsChecks()
        {
            var grid = new LaneGrid();
            Assert.IsTrue(grid.InBounds(new GridPos(1, 15)));
            Assert.IsFalse(grid.InBounds(new GridPos(-1, 0)));
            Assert.IsTrue(grid.InBounds(new GridPos(3, 29)), "2026-09-04 线宽扩至 6 排");
            Assert.IsTrue(grid.InBounds(new GridPos(0, 30)), "2026-09-04 线长扩至 255 列");
            Assert.IsFalse(grid.InBounds(new GridPos(LaneGrid.Rows, 0)));
            Assert.IsFalse(grid.InBounds(new GridPos(0, LaneGrid.Cols)));
        }
    }
}
