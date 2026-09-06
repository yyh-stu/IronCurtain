using System.Collections.Generic;
using IronCurtain.Sim;

namespace IronCurtain.View
{
    /// <summary>M6.2 顶栏纯逻辑:基地查询 / 计时 / 人口 / 敌方基地快照(战争迷雾口径)。禁 UnityEngine。</summary>
    public sealed class TopBarModel
    {
        public static Entity FindBase(SimCore sim, Side side)
        {
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Building || e.Building != BuildingId.Base) continue;
                if (e.Side == side) return e;
            }
            return null;
        }

        public static string FormatClock(long tick, int tickRate)
        {
            if (tickRate <= 0) return "00:00";
            long sec = tick / tickRate;
            long min = sec / 60;
            sec %= 60;
            return min.ToString("00") + ":" + sec.ToString("00");
        }

        public static string PopText(SimCore sim, Side side)
        {
            return "将 " + sim.PopulationOf(side) + "/" + sim.PopulationCapOf(side);
        }

        public static BuildingSnapshot EnemyBaseSnapshot(SimCore sim, Side viewer, Side target)
        {
            var b = FindBase(sim, target);
            if (b == null) return null;
            return VisionSystem.LastKnownBuilding(sim, viewer, b.Id);
        }

        public static string HpText(float hp, float maxHp)
        {
            return System.Math.Round(hp, System.MidpointRounding.AwayFromZero) + "/" +
                   System.Math.Round(maxHp, System.MidpointRounding.AwayFromZero);
        }
    }
}
