using System;
using IronCurtain.Sim;

namespace IronCurtain.Tests
{
    public static class TestUtil
    {
        /// <summary>步进 sim 直到条件成立;超时返回 false。替代固定 tick 断言(节奏账根治)。</summary>
        public static bool RunUntil(SimCore sim, Func<bool> condition, int maxTicks = 3000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (condition()) return true;
                sim.Step();
            }
            return condition();
        }

        public static bool RunUntil(SimCore sim, Func<bool> condition, out int ticksUsed, int maxTicks = 3000)
        {
            int i = 0;
            for (; i < maxTicks; i++)
            {
                if (condition()) { ticksUsed = i; return true; }
                sim.Step();
            }
            ticksUsed = i;
            return condition();
        }
    }
}
