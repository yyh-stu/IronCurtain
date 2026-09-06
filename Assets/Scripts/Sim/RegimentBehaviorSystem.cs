using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 兵团自主行为层(M8.5b):视野索敌自动进攻(最高优先)> 15 格内友军交战自动支援;
    /// 玩家手动命令打断后该次自动决策永久失效(Sup 字段防复读)。纯 tick,无 UnityEngine。
    /// </summary>
    public static class RegimentBehaviorSystem
    {
        public const float SupportRange = 15f;          // 支援判定半径(格/米)
        public const float SupportArriveOffset = 2f;    // 支援落点:友军前方约 2 格

        public static void Tick(SimCore sim, float dt)
        {
            var snapshot = new List<Entity>(sim.Entities.Values);
            foreach (var g in snapshot)
            {
                if (g.Kind != EntityKind.Unit || g.IsSoldier || g.State == EntityState.Dead) continue;
                if (g.RetreatHold) continue; // M8.7 防拉回:玩家授意撤退后自动层让位,直到玩家再下令(被迫接战由 CombatUpdate 兜底)
                if (g.GarrisonBuildingId != 0) continue;

                // 1) 手动/自动命令执行保护(防拉回):有目标在身一律不动,到位后自然重评估
                if (g.HasMoveTarget) continue;

                // 2) 到达即清:本次自动决策自然完成,后续按新事件重新评估
                if (g.AutoKind != 0) { g.AutoKind = 0; g.AutoTargetId = 0; }

                // 3) 索敌进攻(优先级最高)
                var enemy = NearestVisibleEnemy(sim, snapshot, g);
                if (enemy != null)
                {
                    if (g.SupKind == 1 && g.SupTargetId == enemy.Id) continue; // 该敌已被玩家打断过
                    sim.SetMoveTarget(g.Id, enemy.PosF.ToGrid(), false);
                    g.AutoKind = 1;
                    g.AutoTargetId = enemy.Id;
                    continue; // 本 tick 不再评估支援
                }

                // 4) 支援:15 格内交战友军
                var ally = NearestFightingAlly(sim, snapshot, g);
                if (ally != null)
                {
                    if (g.SupKind == 2 && g.SupTargetId == ally.Id) continue; // 该支援已被玩家打断过
                    var dir = (g.PosF - ally.PosF).Normalized();
                    var dest = TerrainMap.ClampToEllipse(ally.PosF + dir * SupportArriveOffset);
                    sim.SetMoveTarget(g.Id, dest.ToGrid(), false);
                    g.AutoKind = 2;
                    g.AutoTargetId = ally.Id;
                }
                // 5) "被迫接战"无需新逻辑:敌方进射程后 CombatUpdate 自动还击
            }
        }

        /// <summary>兵团视野内最近可见敌(成员视野口径:士兵 远程6/近战3;主将 近战6/射手9/断峰10)。</summary>
        static Entity NearestVisibleEnemy(SimCore sim, List<Entity> snapshot, Entity g)
        {
            var members = MembersOf(snapshot, g);
            Entity best = null;
            float bestDist = float.MaxValue;
            foreach (var o in snapshot)
            {
                if (o.Kind != EntityKind.Unit || o.Side == g.Side || o.State == EntityState.Dead) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging) continue;
                if (!VisibleByAny(members, o)) continue;
                float d = SimVec2.Distance(g.PosF, o.PosF);
                if (d < bestDist - 1e-4f || (System.Math.Abs(d - bestDist) <= 1e-4f && best != null && o.Id < best.Id))
                {
                    best = o;
                    bestDist = d;
                }
            }
            return best;
        }

        static Entity NearestFightingAlly(SimCore sim, List<Entity> snapshot, Entity g)
        {
            Entity best = null;
            float bestDist = float.MaxValue;
            foreach (var f in snapshot)
            {
                if (f.Id == g.Id || f.Kind != EntityKind.Unit || f.IsSoldier || f.Side != g.Side) continue;
                if (f.State == EntityState.Dead || f.GarrisonBuildingId != 0) continue;
                if (f.SecondsSinceCombat >= 3f) continue; // 非交战中
                float d = SimVec2.Distance(g.PosF, f.PosF);
                if (d > SupportRange) continue;
                if (d < bestDist - 1e-4f || (System.Math.Abs(d - bestDist) <= 1e-4f && best != null && f.Id < best.Id))
                {
                    best = f;
                    bestDist = d;
                }
            }
            return best;
        }

        static List<Entity> MembersOf(List<Entity> snapshot, Entity g)
        {
            var list = new List<Entity> { g };
            foreach (var e in snapshot)
            {
                if (!e.IsSoldier || e.GeneralId != g.Id || e.State == EntityState.Dead) continue;
                if (e.GarrisonBuildingId != 0) continue;
                list.Add(e);
            }
            return list;
        }

        static bool VisibleByAny(List<Entity> members, Entity enemy)
        {
            foreach (var m in members)
                if (SimVec2.Distance(m.PosF, enemy.PosF) <= VisionRadiusOf(m)) return true;
            return false;
        }

        /// <summary>视野半径:士兵 远程(坠日/星落)6/近战 3;主将 近战 6/坠日星落 9/断峰 10(与 VisionSystem 同口径)。</summary>
        static float VisionRadiusOf(Entity e)
        {
            bool ranged = e.Hero == HeroId.ZhuiRi || e.Hero == HeroId.XingLuo;
            if (e.IsSoldier) return ranged ? 6f : 3f;
            if (e.Hero == HeroId.DuanFeng) return 10f;
            return ranged ? 9f : 6f;
        }
    }
}
