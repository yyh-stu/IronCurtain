using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 兵团底座(M8.2a 第一批):1 主将 + 20 兵,兵随将走(4×5 阵型跟随);
    /// 主将死亡士兵永不溃逃、无将状态就近逐杀至死。纯 tick,无 UnityEngine 依赖。
    /// </summary>
    public static class RegimentSystem
    {
        public const int SoldiersPerGeneral = 20;
        public const int FormationRows = 4;   // 4 行 × 5 列,行列距 1.0 米
        public const int FormationCols = 5;

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
            var facing = FacingOf(g);
            var perp = new SimVec2(facing.Y, -facing.X);
            int row = slot / FormationCols;
            int col = slot % FormationCols;
            return facing * -(row + 1) + perp * (col - 2);
        }

        public static void Tick(SimCore sim, float dt)
        {
            // 快照遍历:士兵死亡结算会移除实体,禁止边枚举边修改
            var snapshot = new List<Entity>(sim.Entities.Values);
            foreach (var e in snapshot)
            {
                if (e.Kind != EntityKind.Unit || !e.IsSoldier || e.State == EntityState.Dead) continue;
                if (e.GarrisonBuildingId != 0) continue;

                var g = e.GeneralId != 0 ? sim.GetEntity(e.GeneralId) : null;
                if (g != null && g.Kind == EntityKind.Unit && g.State != EntityState.Dead)
                {
                    // a) 主将存活:阵型跟随(士兵不写 HasMoveTarget/State)
                    var target = g.PosF + SlotOffset(g, e.FormationSlot);
                    float dist = SimVec2.Distance(e.PosF, target);
                    if (dist > 0.2f) StepMove(e, target, dist, dt);
                }
                else
                {
                    // b) 无将续战:就近索敌逐杀至死(永不溃逃/撤退/响应指令)
                    var enemy = NearestEnemy(sim, e);
                    if (enemy == null) continue;
                    float dist = SimVec2.Distance(e.PosF, enemy.PosF);
                    if (dist <= e.Range + 0.3f) continue; // 交战交给 CombatUpdate
                    StepMove(e, enemy.PosF, dist, dt);
                }
            }
        }

        private static void StepMove(Entity e, SimVec2 target, float dist, float dt)
        {
            float spd = e.MoveSpeed * TerrainMap.MoveFactor(e.PosF);
            float step = System.Math.Min(spd * dt, dist);
            e.PosF = TerrainMap.ClampToEllipse(e.PosF + (target - e.PosF).Normalized() * step);
            e.Pos = e.PosF.ToGrid();
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
            var target = atPos ?? general.PosF + SlotOffset(general, slot);
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
            s.Pos = target.ToGrid();
            return s;
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
