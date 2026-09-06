namespace IronCurtain.Sim
{
    /// <summary>对局阶段(M6.4a)。</summary>
    public enum MatchPhase { Normal, Overtime, Ended }

    /// <summary>
    /// 对局时钟(M6.4a):10 分钟时限 → 2 分钟加时 → 积分判定。
    /// 摧毁大本营胜利(DestroySystem.SetWinner)由本系统统一收口为 Ended + MatchEnded 事件。
    /// 加时 buff 落点:EconomyUpdate(EP 60s)/PopulationCapOf(名额+1)/ReinforceSystem(补兵 ×1.1)。
    /// </summary>
    public static class MatchClockSystem
    {
        public static float ElapsedSeconds(SimCore sim) => sim.Tick / (float)sim.Config.TickRate;

        /// <summary>常规期剩余 / 加时剩余 / 已结束 0。</summary>
        public static float RemainingSeconds(SimCore sim)
        {
            if (sim.Game == null) return 0f;
            var m = sim.Game.Match;
            float total = (m.DurationMinutes + (sim.Phase == MatchPhase.Overtime ? m.OvertimeMinutes : 0)) * 60f;
            if (sim.Phase == MatchPhase.Ended) return 0f;
            return System.Math.Max(0f, total - ElapsedSeconds(sim));
        }

        public static void Tick(SimCore sim, float dt)
        {
            // 留场死扫描(塔击/海克斯置 Dead 不移除):首次发现即计数,与 CombatUpdate 清理块双路径防重
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Unit && !e.IsSoldier && e.State == EntityState.Dead && !e.DeadCounted)
                { e.DeadCounted = true; sim.LostGenerals[e.Side]++; }

            if (sim.Phase == MatchPhase.Ended) return;
            if (sim.Game == null) return; // 无数值配置(纯战斗测试)不启用时钟

            // 摧毁胜利已发生 → 收口
            if (sim.Winner != null)
            {
                sim.Phase = MatchPhase.Ended;
                sim.Emit(new SimEvent(sim.Tick, SimEventType.MatchEnded, (int)sim.Winner.Value));
                return;
            }

            var m = sim.Game.Match;
            float duration = m.DurationMinutes * 60f;
            float elapsed = ElapsedSeconds(sim);

            // 常规时限到(双方大本营均在,否则上方已收口)→ 加时
            if (sim.Phase == MatchPhase.Normal && elapsed >= duration)
            {
                sim.Phase = MatchPhase.Overtime;
                sim.Emit(new SimEvent(sim.Tick, SimEventType.OvertimeStarted, 0));
            }

            // 加时结束 → 积分判定
            if (sim.Phase == MatchPhase.Overtime && elapsed >= duration + m.OvertimeMinutes * 60f)
            {
                sim.Phase = MatchPhase.Ended;
                var winner = JudgeByScore(sim);
                if (winner.HasValue) sim.SetWinner(winner.Value);
                sim.Emit(new SimEvent(sim.Tick, SimEventType.MatchEnded, winner.HasValue ? (int)winner.Value : -1));
            }
        }

        /// <summary>积分明细(GDD 9.1):5×基地血 + 3×将 + 2×EP + 建筑×1。</summary>
        public static void ScoreOf(SimCore sim, Side side,
            out int basePt, out int heroPt, out int epPt, out int bldPt, out int total)
        {
            var m = sim.Game.Match;
            basePt = 0; bldPt = 0;
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Building || e.Side != side || e.Rubbled || e.State == EntityState.Dead) continue;
                if (e.Building == BuildingId.Base) basePt += (int)e.Hp * m.ScoreBaseHpWeight;
                bldPt += m.ScoreBuildingWeight;
            }
            heroPt = sim.PopulationOf(side) * m.ScoreHeroWeight;
            epPt = (int)sim.EpOf(side) * m.ScoreEpWeight;
            total = basePt + heroPt + epPt + bldPt;
        }

        /// <summary>平局链:总分 → 存活将数 → 阵亡兵团数(少者胜)→ 平局。</summary>
        private static Side? JudgeByScore(SimCore sim)
        {
            ScoreOf(sim, Side.Blue, out _, out _, out _, out _, out int b);
            ScoreOf(sim, Side.Red, out _, out _, out _, out _, out int r);
            if (b != r) return b > r ? Side.Blue : Side.Red;
            int bp = sim.PopulationOf(Side.Blue), rp = sim.PopulationOf(Side.Red);
            if (bp != rp) return bp > rp ? Side.Blue : Side.Red;
            int bl = sim.LostGenerals[Side.Blue], rl = sim.LostGenerals[Side.Red];
            if (bl != rl) return bl < rl ? Side.Blue : Side.Red;
            return null;
        }
    }
}
