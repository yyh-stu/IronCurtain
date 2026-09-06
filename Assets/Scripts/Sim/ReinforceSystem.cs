using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 补兵池(M8.4a 第一批):全局 2s/兵、上限 60;玩家点补兵 → SJF(最缺人兵团先补、补齐为止);
    /// 交战兵团不可补、脱战 3s 重新入列。行军表现归 View(M8.6b),本类纯 Sim。
    /// </summary>
    public static class ReinforceSystem
    {
        public const int PoolCap = 60;              // 池上限(平拍)
        public const float SecondsPerSoldier = 2f;  // 2s/兵
        public const float OutOfCombatSeconds = 3f; // 脱战 3s 重新入列

        public static int PoolOf(SimCore sim, Side side) => sim.ReinforcePool[side];

        public static void Tick(SimCore sim, float dt)
        {
            sim.ReinforceAccum[Side.Blue] += dt / SecondsPerSoldier;
            sim.ReinforceAccum[Side.Red] += dt / SecondsPerSoldier;
            foreach (Side side in new[] { Side.Blue, Side.Red })
            {
                while (sim.ReinforceAccum[side] >= 1f && sim.ReinforcePool[side] < PoolCap)
                {
                    sim.ReinforcePool[side]++;
                    sim.ReinforceAccum[side] -= 1f;
                }
            }
        }

        public static bool RequestReinforce(SimCore sim, Side side, out string error)
        {
            error = null;

            // 1) 收集主将
            var generals = new List<Entity>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == side && e.State != EntityState.Dead)
                    generals.Add(e);
            if (generals.Count == 0) { error = "无主将在场"; return false; }
            if (sim.ReinforcePool[side] <= 0) { error = "补兵池为空"; return false; }

            // 2) 交战判定 + 存活士兵计数
            var regiments = new List<System.Tuple<int, Entity>>(); // (cnt, general)
            bool anyEligible = false;
            foreach (var g in generals)
            {
                int cnt = 0;
                bool inCombat = g.SecondsSinceCombat < OutOfCombatSeconds;
                foreach (var e in sim.Entities.Values)
                {
                    if (!e.IsSoldier || e.GeneralId != g.Id || e.State == EntityState.Dead) continue;
                    cnt++;
                    if (e.SecondsSinceCombat < OutOfCombatSeconds) inCombat = true;
                }
                if (inCombat) continue;
                anyEligible = true;
                if (cnt < RegimentSystem.SoldiersPerGeneral)
                    regiments.Add(System.Tuple.Create(cnt, g));
            }
            if (!anyEligible) { error = "全部兵团交战中,不可补兵"; return false; }

            // 3) SJF:缺员数升序(同缺员按 Id 稳定序),逐团补齐为止
            regiments.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.Id.CompareTo(b.Item2.Id));
            bool anyGiven = false;
            foreach (var reg in regiments)
            {
                if (sim.ReinforcePool[side] <= 0) break;
                var g = reg.Item2;
                var used = new HashSet<int>();
                foreach (var e in sim.Entities.Values)
                    if (e.IsSoldier && e.GeneralId == g.Id && e.State != EntityState.Dead)
                        used.Add(e.FormationSlot);
                int need = RegimentSystem.SoldiersPerGeneral - reg.Item1;
                int give = System.Math.Min(need, sim.ReinforcePool[side]);
                for (int slot = 0; slot < RegimentSystem.SoldiersPerGeneral && give > 0; slot++)
                {
                    if (used.Contains(slot)) continue;
                    RegimentSystem.SpawnSoldier(sim, g, slot);
                    sim.ReinforcePool[side]--;
                    give--;
                    anyGiven = true;
                }
            }
            if (!anyGiven) { error = "补兵池为空"; return false; }
            return true;
        }
    }
}
