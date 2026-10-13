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
        public const int ZhenYueSoldiers = 8;     // 2026-09-11 制作人:镇岳军团 8 兵(士兵 1:2,等效 16)

        /// <summary>该主将的军团士兵数(镇岳 8,其余 12)。</summary>
        public static int CompanySize(Entity g)
        {
            return g != null && g.Hero == HeroId.ZhenYue ? ZhenYueSoldiers : SoldiersPerGeneral;
        }

        /// <summary>该主将的平时纵队列数(镇岳两路 2 列,其余 3 列)。</summary>
        public static int FormationColsOf(Entity g)
        {
            return g != null && g.Hero == HeroId.ZhenYue ? 2 : FormationCols;
        }
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
            // 2026-09-10 制作人定稿:平时(行军/跟随/驻立)纵队跟在主将后方,美观;
            // 警戒/交火(索敌接战中)改圆圈包围主将,主将为圆心。
            // 2026-09-11 制作人:镇岳军团 8 兵——平时两路 2×4,警戒圆圈围主将(逻辑不变)。
            int n = CompanySize(g);
            bool combat = g.State == EntityState.Attacking || g.AutoKind != 0;
            var facing = FacingOf(g);
            if (combat)
            {
                float angle = slot * (System.MathF.PI * 2f / n);
                var dir = new SimVec2(System.MathF.Cos(angle), System.MathF.Sin(angle));
                return new SimVec2(dir.X * facing.X - dir.Y * facing.Y, dir.X * facing.Y + dir.Y * facing.X) * 2.6f; // 圆圈随朝向整体旋转,主将为圆心
            }
            int cols = FormationColsOf(g);
            var perpC = new SimVec2(facing.Y, -facing.X);
            int row = slot / cols;
            int col = slot % cols;
            return facing * -(row + 1.2f) + perpC * (col - (cols - 1) * 0.5f);
        }

        /// <summary>2026-09-14 制作人口径:"警戒时围住主将,**战斗时废除**阵型,优先保证可以攻击到敌人"。
        /// 交战中的兵团集合(主将或其士兵最近 3 秒内有伤害进出)。每 tick 扫一遍单位即可,不做逐兵 O(n²) 查询。</summary>
        private static readonly HashSet<int> InCombatGenerals = new HashSet<int>();

        private static void RefreshCombatGenerals(List<Entity> snapshot)
        {
            InCombatGenerals.Clear();
            foreach (var e in snapshot)
            {
                if (e.Kind != EntityKind.Unit || e.Side == Side.Neutral) continue;
                if (e.State == EntityState.Dead || e.GarrisonBuildingId != 0) continue;
                if (e.SecondsSinceCombat >= RegimentBehaviorSystem.MeleeEngagedSeconds) continue;
                int gid = e.IsSoldier ? e.GeneralId : e.Id;
                if (gid != 0) InCombatGenerals.Add(gid);
            }
        }

        /// <summary>战斗中最近的敌人(用于"废阵型、贴上去打");10 米内没有就返回 null(退回阵型跟随)。</summary>
        private static Entity NearestEnemyWithin(SimCore sim, Entity e, float maxDist)
        {
            Entity best = null; float bestDist = maxDist;
            foreach (var o in sim.Entities.Values)
            {
                if (o.Kind != EntityKind.Unit || o.Side == e.Side || o.State == EntityState.Dead) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging) continue;
                float d = SimVec2.Distance(e.PosF, o.PosF);
                if (d < bestDist - 1e-4f) { best = o; bestDist = d; }
            }
            return best;
        }

        public static void Tick(SimCore sim, float dt)
        {
            // 快照遍历:士兵死亡结算会移除实体,禁止边枚举边修改
            var snapshot = new List<Entity>(sim.Entities.Values);
            RefreshCombatGenerals(snapshot);
            foreach (var e in snapshot)
            {
                if (e.Kind != EntityKind.Unit || !e.IsSoldier || e.State == EntityState.Dead) continue;
                if (e.GarrisonBuildingId != 0) continue;
                if (e.TargetBuildingId != 0) continue; // 攻城命令临时脱离跟随，由 SimCore.MoveUnits 驱动入场。

                var g = e.GeneralId != 0 ? sim.GetEntity(e.GeneralId) : null;
                if (g != null && g.Kind == EntityKind.Unit && g.State != EntityState.Dead)
                {
                    if (e.State == EntityState.Routing && g.State != EntityState.Routing) e.State = EntityState.Idle; // 主将已到站/被打断 → 士兵撤出撤退态
                    // a-0) ★战斗时**废除阵型**(2026-09-14 制作人口径):士兵不再回圆阵槽位,而是贴向最近的敌人;
                    //      进射程就停下交给 CombatUpdate 开火(它自己会打)。旧实现里士兵宁可站在半径 2.6 米的
                    //      圆阵上、离敌人差一点点也够不到,表现就是"一圈人围着主将不打人"(实测 0 伤害)。
                    if (InCombatGenerals.Contains(g.Id))
                    {
                        var foe = NearestEnemyWithin(sim, e, 10f);
                        if (foe != null)
                        {
                            if (SimVec2.Distance(e.PosF, foe.PosF) > e.Range + 0.3f) StepMove(sim, e, foe.PosF, dt);
                            continue;   // 战斗中不再回阵型槽位
                        }
                    }
                    // a) 主将存活:阵型跟随(士兵不写 HasMoveTarget/State)
                    var target = g.PosF + SlotOffset(g, e.FormationSlot);
                    float dist = SimVec2.Distance(e.PosF, target);
                    if (dist > 0.2f)
                    {
                        // 2026-09-14 制作人:"兵被卡住…能不能给一个移速补偿" ——
                        // **掉队补偿**:离阵位越远走得越快(最高 ×1.8),被建筑/友军挤住一段时间后
                        // 一旦脱困就能追上主将,不会永远吊在后面(上限内不改变正常跟随的手感)。
                        // 门槛取 5 米:正常阵型间距(<4 米)完全不触发,只有真正被挤住/掉队的士兵才补偿
                        float gap = SimVec2.Distance(e.PosF, g.PosF);
                        float boost = gap <= 5f ? 1f : System.Math.Min(1.6f, 1f + (gap - 5f) * 0.1f);
                        StepMove(sim, e, target, dt, boost);
                    }
                }
                else
                {
                    if (e.State == EntityState.Routing) e.State = EntityState.Idle; // 无将续战不是撤退,清除撤退态(撤减益不适用于拼死作战)
                    // b) 无将续战:就近索敌逐杀至死(永不溃逃/撤退/响应指令)
                    var enemy = NearestEnemy(sim, e);
                    if (enemy != null && SimVec2.Distance(e.PosF, enemy.PosF) <= OrphanReturnClearRange)
                    {
                        float dist = SimVec2.Distance(e.PosF, enemy.PosF);
                        if (dist <= e.Range + 0.3f) continue; // 交战交给 CombatUpdate
                        StepMove(sim, e, enemy.PosF, dt);
                        continue;
                    }
                    // b2) 2026-09-14 制作人:「当战斗结束、主将阵亡、士兵没死,会在原地发呆 —— 他正常应该
                    //     回到最近的兵营变成补兵」。附近(c. 12 米)没有敌人 = 战斗结束 → 走回**最近的己方兵营**
                    //     (没有兵营则回大本营),抵达后**并入补兵池**(池满则原地待命等空位)。
                    //     注:这不是"溃逃"(不触发撤退减益),是重整为补兵。
                    var home = NearestOwnDepot(sim, e);
                    if (home == null) continue;
                    if (SimVec2.Distance(e.PosF, home.PosF) > SimCore.BuildingCollisionRadius(home) + 1.2f)
                    {
                        StepMove(sim, e, home.PosF, dt);
                        continue;
                    }
                    if (sim.ReinforcePool[e.Side] < ReinforceSystem.PoolCap)
                    {
                        sim.ReinforcePool[e.Side]++;
                        sim.RemoveEntity(e.Id);   // 入池即离场
                    }
                }
            }
        }

        /// <summary>
        /// "战斗还没结束"的判定半径:孤儿士兵 **20 米**内有敌人就继续打(与"20 格内支援"同一套"附近"口径),
        /// 只有附近确实没有敌人才回营变补兵(制作人:"当战斗结束、主将阵亡、士兵没死…应该回到最近的兵营变成补兵")。
        /// 取 20 米而不是几米:同一条路上十几米外的敌人仍属于这场战斗(旧口径"无将就近逐杀至死"要继续成立)。
        /// </summary>
        public const float OrphanReturnClearRange = 20f;

        /// <summary>最近的己方**兵营**(没有则大本营)—— 孤儿士兵回营转补兵的集结点。</summary>
        private static Entity NearestOwnDepot(SimCore sim, Entity e)
        {
            Entity best = null; float bestDist = float.MaxValue;
            foreach (var b in sim.Entities.Values)
            {
                if (b.Kind != EntityKind.Building || b.Side != e.Side || b.Rubbled) continue;
                if (b.Building != BuildingId.Barracks && b.Building != BuildingId.Base) continue;
                float d = SimVec2.Distance(e.PosF, b.PosF);
                bool better = b.Building == BuildingId.Barracks;
                if (best == null) { best = b; bestDist = d; continue; }
                bool bestIsBarracks = best.Building == BuildingId.Barracks;
                if (better == bestIsBarracks ? d < bestDist : better) { best = b; bestDist = d; }
            }
            return best;
        }

        /// <summary>士兵步进(M8.5d:旁路点状态机 + 卡死检测内联,与 SimCore.MoveUnits 同一套数值)。</summary>
        private static void StepMove(SimCore sim, Entity e, SimVec2 target, float dt, float speedBoost = 1f)
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

            float spd = e.MoveSpeed * TerrainMap.MoveFactor(e.PosF) * speedBoost;   // 掉队补偿
            float step = System.Math.Min(spd * dt, SimVec2.Distance(e.PosF, goal));
            var nextF = TerrainMap.ClampToEllipse(e.PosF + (goal - e.PosF).Normalized() * step);
            // 三路城墙(2026-09-11):敌方完好段不可穿(自家走门洞);被挡则停留原位等待
            // 2026-09-14:带起点的判定 → 被挤进薄壳里的士兵也能向外走出来
            e.PosF = WallSystem.CanPass(e.Side, e.PosF, nextF) ? nextF : e.PosF;
            // 建筑推出(与主将路径同语义,士兵不得穿墙)
            foreach (var b in sim.Entities.Values)
                if (b.Kind == EntityKind.Building && !b.Rubbled && BuildingFootprint.Contains(b, e.PosF))
                    e.PosF = BuildingFootprint.ClosestPoint(b, e.PosF);
            e.Pos = e.PosF.ToGrid();

            // 卡死检测:位移 < 期望步长×0.25 → 停滞;满 15 tick 且无旁路 → 计算旁路点
            if (SimVec2.Distance(e.PosF, e.PrevPosF) < spd * dt * NavigationAid.StepRatio) e.StuckTicks++;
            else e.StuckTicks = 0;
            // 注:士兵的"同阵营互卡穿透"与主将同一套 —— 计数在 SoftCollide 里,不在这里。
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
            var s = sim.SpawnEntity(general.Side, EntityKind.Unit, target.ToGrid(), false);   // 配好身份再声明(见 SpawnEntity 说明)
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
            sim.AnnounceSpawn(s);   // 士兵的视图必须等 IsSoldier/Hero 都就位(否则会拿主将模型建一遍)
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
            MatchStats.Register(side, hero);   // 出场注册:结算面板按"出场主将"列行(2026-09-12)

            var g = sim.SpawnEntity(side, EntityKind.Unit, pos, false);   // 配好 Hero/属性再声明(见 SpawnEntity 说明)
            g.Hero = hero;
            g.MaxHp = c.MaxHp;
            g.Hp = c.MaxHp;
            g.Attack = c.Attack;
            g.AttackIntervalSec = (float)c.AttackIntervalSec;
            g.Range = c.Range;
            g.MoveSpeed = (float)c.MoveSpeed;
            if (c.Shield > 0) { g.Shield = c.Shield; g.ShieldMax = c.Shield; }

            sim.AnnounceSpawn(g);   // ★身份/属性/位置都就位了才声明 —— 视图据此建模型(否则主将拿不到 Hero)
            for (int slot = 0; slot < CompanySize(g); slot++)
                SpawnSoldier(sim, g, slot);
            return g;
        }
    }
}
