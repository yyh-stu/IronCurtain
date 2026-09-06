using System;
using System.Text;
using IronCurtain.Sim;
using NUnit.Framework;

namespace IronCurtain.Tests
{
    public sealed class DeterminismTests
    {
        /// <summary>同一脚本化场景跑两遍,事件流必须逐字节一致。</summary>
        [Test]
        public void SameScenario_Twice_IdenticalEventStream()
        {
            string Run()
            {
                var sim = new SimCore(new SimConfig(), new SeededRng(42));
                var sb = new StringBuilder();
                sim.AddEventListener(e => sb.Append((int)e.Type).Append(':').Append(e.Tick).Append(':').Append(e.SubjectId).Append(';'));

                sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(0, 0));
                sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(2, 29));
                sim.SpawnEntity(Side.Blue, EntityKind.Building, new GridPos(1, 14));

                sim.SubmitCommand(new StepCountCommand(50));
                sim.SubmitCommand(new RngProbeCommand(3));
                for (int i = 0; i < 300; i++) sim.Step();
                sim.RemoveEntity(2);
                for (int i = 0; i < 100; i++) sim.Step();
                return sb.ToString();
            }
            Assert.AreEqual(Run(), Run());
        }

        /// <summary>空转步进在两次运行中事件序列一致。</summary>
        [Test]
        public void EmptySteps_Twice_IdenticalTicks()
        {
            string Run()
            {
                var sim = new SimCore(new SimConfig(), new SeededRng(7));
                var sb = new StringBuilder();
                sim.AddEventListener(e => sb.Append(e.Tick).Append(','));
                for (int i = 0; i < 1000; i++) sim.Step();
                return sb.ToString();
            }
            Assert.AreEqual(Run(), Run());
        }

        private sealed class StepCountCommand : ICommand
        {
            private readonly int _n;
            public StepCountCommand(int n) { _n = n; }
            public void Execute(SimCore sim)
            {
                // 占位:证明命令执行路径纳入确定性校验
                for (int i = 0; i < _n; i++) { sim.Rng.NextInt(0, 1000); }
            }
        }

        private sealed class RngProbeCommand : ICommand
        {
            private readonly int _n;
            public RngProbeCommand(int n) { _n = n; }
            public void Execute(SimCore sim)
            {
                for (int i = 0; i < _n; i++) { sim.Rng.NextInt(0, 1000); }
            }
        }
    }
}
