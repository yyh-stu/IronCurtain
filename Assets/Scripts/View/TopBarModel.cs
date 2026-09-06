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

        /// <summary>M6.4a 对局倒计时:常规期=剩余常规时长;加时=剩余加时;结束定格 00:00。无配置回退正计时。</summary>
        public static string FormatCountdown(SimCore sim)
        {
            if (sim.Config.TickRate <= 0) return "00:00";
            if (sim.Game == null) return FormatClock(sim.Tick, sim.Config.TickRate);
            float remain = MatchClockSystem.RemainingSeconds(sim);
            long sec = (long)System.Math.Ceiling(remain);
            return (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00");
        }

        /// <summary>M6.4a 阶段副标题。</summary>
        public static string PhaseText(SimCore sim)
        {
            if (sim.Phase == MatchPhase.Ended) return sim.Winner.HasValue ? "战斗结束" : "平局";
            return sim.Phase == MatchPhase.Overtime ? "加时赛" : "对战中";
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
