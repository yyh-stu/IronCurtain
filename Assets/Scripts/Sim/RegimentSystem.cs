using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 兵团底座(M8.2a 第一批):1 主将 + 20 兵,兵随将走(4×5 阵型跟随);
    /// 主将死亡士兵永不溃逃、无将状态就近逐杀至死。纯 tick,无 UnityEngine 依赖。
    /// </summary>
    public static class RegimentSystem
    {
        public const int SoldiersPerGeneral = 12; // 2026-09-10 制作人拍板:20→12(画面过乱)
        public const int FormationRows = 4;   // 4 行 × 5 列,行列距 1.0 米
        public const int FormationCols = 3; // 行军纵队 3×4(2026-09-10 制作人拍板)

        public static SimVec2 FacingOf(Entity g)
        {
            if (g.FacingOverride.HasValue) return g.FacingOverride.Value;
            SimVec2 enemyDir = g.Side == Side.Blue ? new SimVec2(1f, 0f) : new SimVec2(-1f, 0f);
            if (g.HasMoveTarget)
            {
                var dir = (SimVec2.FromGrid(g.MoveTarget) - g.PosF).Normalized();
                bool toward = (g.Side == Side.Blue && dir.X > 0.05f) || (g.Side == Side.Red && dir.X < -0.05f);
                if (toward) return dir;       // 前进方向(朝敌)→用之(斜向前进阵型随之斜)
                return enemyDir;              // 后撤/横移 → 保持面朝敌不转身(规则③)
            }
            return enemyDir;
        }

        public static SimVec2 SlotOffset(Entity g, int slot)
        {
            // 2026-09-10 制作人定稿:平时(行军/跟随/驻立)一律 3×4 纵队跟在主将后方,美观;
            // 警戒/交火(索敌接战中)改圆圈包围主将,主将为圆心
            bool combat = g.State == EntityState.Attacking || g.AutoKind != 0;
            var facing = FacingOf(g);
            if (combat)
            {
                float angle = slot * (System.MathF.PI * 2f / SoldiersPerGeneral);
                var dir = new SimVec2(System.MathF.Cos(angle), System.MathF.Sin(angle));
                return new SimVec2(dir.X * facing.X - dir.Y * facing.Y, dir.X * facing.Y + dir.Y * facing.X) * 2.6f; // 圆圈随朝向整体旋转,主将为圆心
            }
            var perpC = new SimVec2(facing.Y, -facing.X);
            int row = slot / FormationCols;
            int col = slot % FormationCols;
            return facing * -(row + 1.2f) + perpC * (col - (FormationCols - 1) * 0.5f);
        }

        public static void Tick(SimCore sim, float dt)
        {
            // 快照遍历:士兵死亡结算会移除实体,禁止边枚举边修改
            var snapshot = new List<Entity>(sim.Entities.Values);
            foreach (var e in snapshot)
            {
                if (e.Kind != EntityKind.Unit || !e.IsSoldier || e.State == EntityState.Dead) continue;
                if (e.GarrisonBuildingId != 0) continue;
                if (e.TargetBuildingId != 0) continue; // 攻城命令临时脱离跟随，由 SimCore.MoveUnits 驱动入场。

                var g = e.GeneralId != 0 ? sim.GetEntity(e.GeneralId) : null;
                if (g != null && g.Kind == EntityKind.Unit && g.State != EntityState.Dead)
                {
                    if (e.State == EntityState.Routing && g.State != EntityState.Routing) e.State = EntityState.Idle; // 主将已到站/被打断 → 士兵撤出撤退态
                    // a) 主将存活:阵型跟随(士兵不写 HasMoveTarget/State)
                    var target = g.PosF + SlotOffset(g, e.FormationSlot);
                    float dist = SimVec2.Distance(e.PosF, target);
                    if (dist > 0.2f) StepMove(sim, e, target, dt);
                }
                else
                {
                    if (e.State == EntityState.Routing) e.State = EntityState.Idle; // 无将续战不是撤退,清除撤退态(撤减益不适用于拼死作战)
                    // b) 无将续战:就近索敌逐杀至死(永不溃逃/撤退/响应指令)
                    var enemy = NearestEnemy(sim, e);
                    if (enemy == null) continue;
                    float dist = SimVec2.Distance(e.PosF, enemy.PosF);
                    if (dist <= e.Range + 0.3f) continue; // 交战交给 CombatUpdate
                    StepMove(sim, e, enemy.PosF, dt);
                }
            }
        }

        /// <summary>士兵步进(M8.5d:旁路点状态机 + 卡死检测内联,与 SimCore.MoveUnits 同一套数值)。</summary>
        private static void StepMove(SimCore sim, Entity e, SimVec2 target, float dt)
        {
            // 旁路点:抵达(<0.8m)或超时(>45 tick)→ 清除,转回原目标
            if (e.Detour.HasValue)
            {
                e.DetourTicks++;
                if (SimVec2.Distance(e.PosF, e.Detour.Value) < NavigationAid.DetourClearDist
                    || e.DetourTicks > NavigationAid.DetourTimeoutTicks)
                {
                    e.Detour = null;
                    e.DetourTicks = 0;
                }
            }
            var goal = e.Detour ?? target;

            float spd = e.MoveSpeed * TerrainMap.MoveFactor(e.PosF);
            float step = System.Math.Min(spd * dt, SimVec2.Distance(e.PosF, goal));
            e.PosF = TerrainMap.ClampToEllipse(e.PosF + (goal - e.PosF).Normalized() * step);
            // 建筑推出(与主将路径同语义,士兵不得穿墙)
            foreach (var b in sim.Entities.Values)
                if (b.Kind == EntityKind.Building && !b.Rubbled && BuildingFootprint.Contains(b, e.PosF))
                    e.PosF = BuildingFootprint.ClosestPoint(b, e.PosF);
            e.Pos = e.PosF.ToGrid();

            // 卡死检测:位移 < 期望步长×0.25 → 停滞;满 15 tick 且无旁路 → 计算旁路点
            if (SimVec2.Distance(e.PosF, e.PrevPosF) < spd * dt * NavigationAid.StepRatio) e.StuckTicks++;
            else e.StuckTicks = 0;
            e.PrevPosF = e.PosF;
            if (e.StuckTicks >= NavigationAid.StuckThresholdTicks && !e.Detour.HasValue)
            {
                e.Detour = NavigationAid.ComputeDetour(sim, e, target);
                e.StuckTicks = 0;
            }
        }

        /// <summary>最近敌索敌:与 SimCore.FindNearestEnemy 同一欧氏语义(单位、非己方、非死亡、非城内/交换)。</summary>
        private static Entity NearestEnemy(SimCore sim, Entity e)
        {
            Entity best = null;
            float bestDist = float.MaxValue;
            foreach (var o in sim.Entities.Values)
            {
                if (o.Kind != EntityKind.Unit || o.Side == e.Side || o.State == EntityState.Dead) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging) continue;
                float d = SimVec2.Distance(e.PosF, o.PosF);
                if (d < bestDist) { best = o; bestDist = d; }
            }
            return best;
        }

        /// <summary>生成一名士兵(属性=主将/10,攻速/移速/射程=主将,变装=主将 Hero),落位槽位偏移点。SpawnGeneral 与补兵共用。</summary>
        public static Entity SpawnSoldier(SimCore sim, Entity general, int slot, SimVec2? atPos = null)
        {
            var off = SlotOffset(general, slot);
            var target = atPos ?? general.PosF + off;
            // 防崩(2026-09-10 圆阵/纵队适配):理想点被占 → 螺旋搜索(径向+环绕各向),
            // 找不到再保留原抛出语义;圆阵贴建筑时径向外推会撞更深,环绕跳位才能解。
            if (!SpawnPointFree(sim, target))
            {
                SimVec2 found = target; bool ok = false;
                for (int ring = 1; ring <= 3 && !ok; ring++)
                {
                    float rd = ring * 0.8f;
                    for (int a = 0; a < 12; a++)
                    {
                        float ang = a * (System.MathF.PI * 2f / 12f) + ring * 0.26f;
                        var cand = TerrainMap.ClampToEllipse(target + new SimVec2(System.MathF.Cos(ang) * rd, System.MathF.Sin(ang) * rd));
                        if (SpawnPointFree(sim, cand)) { found = cand; ok = true; break; }
                    }
                }
                if (ok) target = found;
                else
                {
                    for (int t = 0; t < 3; t++)
                        target = TerrainMap.ClampToEllipse(target + off.Normalized());
                }
            }
            var s = sim.SpawnEntity(general.Side, EntityKind.Unit, target.ToGrid());
            s.IsSoldier = true;
            s.FormationSlot = slot;
            s.Hero = general.Hero;
            s.MaxHp = general.MaxHp / 10f;
            s.Hp = s.MaxHp;
            s.Attack = general.Attack / 10f;
            s.AttackIntervalSec = general.AttackIntervalSec;
            s.MoveSpeed = general.MoveSpeed;
            s.Range = general.Range;
            s.Shield = general.Shield / 10f;
            s.ShieldMax = s.Shield;
            s.GeneralId = general.Id;
            s.PosF = target;
            s.PrevPosF = target;
            s.Pos = target.ToGrid();
            return s;
        }

        /// <summary>出生点是否可用(口径=SimCore.SpawnEntity 抛出条件:按落点格心判定,与 ReinforceSystem.SlotCellFree 同构)。</summary>
        static bool SpawnPointFree(SimCore sim, SimVec2 target)
        {
            var c = SimVec2.FromGrid(target.ToGrid());
            foreach (var b in sim.Entities.Values)
                if (b.Kind == EntityKind.Building && !b.Rubbled && BuildingFootprint.Contains(b, c)) return false;
            foreach (var u in sim.Entities.Values)
                if (u.Kind == EntityKind.Unit && u.State != EntityState.Dead && u.GarrisonBuildingId == 0
                    && SimVec2.Distance(u.PosF, c) < 0.5f) return false;
            return true;
        }

        /// <summary>生成主将 + 20 名士兵的完整兵团,返回主将实体。</summary>
        public static Entity SpawnGeneral(SimCore sim, Side side, HeroId hero, GridPos pos)
        {
            var c = sim.Game.Heroes[(int)hero];

            var g = sim.SpawnEntity(side, EntityKind.Unit, pos);
            g.Hero = hero;
            g.MaxHp = c.MaxHp;
            g.Hp = c.MaxHp;
            g.Attack = c.Attack;
            g.AttackIntervalSec = (float)c.AttackIntervalSec;
            g.Range = c.Range;
            g.MoveSpeed = (float)c.MoveSpeed;
            if (c.Shield > 0) { g.Shield = c.Shield; g.ShieldMax = c.Shield; }

            for (int slot = 0; slot < SoldiersPerGeneral; slot++)
                SpawnSoldier(sim, g, slot);
            return g;
        }
    }
}
