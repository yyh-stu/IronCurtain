using System.Collections.Generic;

namespace IronCurtain.Sim
{
    public static class DestroySystem
    {
        public static void Tick(SimCore sim, float dt)
        {
            var cleared = new List<int>();
            var fortGone = new List<int>();
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e.TargetBuildingId == 0) continue;
                if (e.Kind != EntityKind.Unit) continue;

                // 逐风冲锋首击重置
                if (e.Hero == HeroId.ZhuFeng && !e.IsCharging)
                    e.ChargeHitUsed = false;

                var b = sim.GetEntity(e.TargetBuildingId);
                if (b == null || b.Rubbled || !sim.Entities.ContainsKey(e.TargetBuildingId))
                {
                    e.TargetBuildingId = 0;
                    e.State = EntityState.Idle;
                    continue;
                }

                // 意图校验
                if (b.Intent != SiegeIntent.Destroy || b.IntentIssuer != e.Side)
                    continue;

                // 中立非废墟建筑完全不受拆塔伤害
                if (b.Side == Side.Neutral && !b.Rubbled)
                    continue;

                // 优先级 D26:射程内存在敌军单位 → 本 tick 不拆塔
                if (EnemyInRange(sim, e)) continue;

                // 2026-09-15 制作人:"**还是不攻击建筑,全在发呆**"(附图:近战主将站在敌方箭塔旁不动)。
                // 探针实测:主将行军落点离塔心 7.00 米、士兵落点 3.00 米,而旧判定要求中心距 ≤ 2.80 米
                // → 到位后 `State=Attacking` 却**一点血都打不掉**(近战其实完全拆不动建筑,只有射手能拆)。
                // 这里补上"**差最后一段就继续逼近**":没进入攻击距离时重新给一个贴墙落点,
                // 而不是原地发呆。只在 `!HasMoveTarget`(真的停住了)时才补令 —— 走着的时候别插手,
                // 免得每 tick 重置 `SetMoveTarget` 里的停滞判定,把绕行机制废掉。
                if (!SimCore.InAttackReachOfBuilding(e, b))
                {
                    if (e.GarrisonBuildingId == 0 && e.State != EntityState.Exchanging && !e.HasMoveTarget)
                    {
                        var stand = BreachCell(b, e);
                        if (stand.HasValue
                            && SimVec2.Distance(e.PosF, SimVec2.FromGrid(stand.Value)) > 0.5f)
                        {
                            sim.SetMoveTarget(e.Id, stand.Value, false);   // manual=false:SIM 自主逼近,不夺玩家的自动层字段
                            e.State = EntityState.Moving;
                        }
                    }
                    continue;
                }

                // 进入攻击距离 = **对这条命令而言"到位了"** → 按 MoveUnits 抵达那套收尾把行军目标清干净。
                // 必须清:否则 `State` 被切成 Attacking 后 `MoveUnits` 不再推进它,而 `HasMoveTarget` 永久为真
                // —— `SiegeSystem` 第 0 步"主将**到位**才放兵入场"就永远不成立(探针里射手主将卡在 7.80 米、
                // 兵一个都放不出去,正是这个残留)。
                if (e.HasMoveTarget)
                {
                    e.HasMoveTarget = false;
                    e.Detour = null;
                    e.DetourTicks = 0;
                    e.MoveAccum = 0f;
                    e.PrevPosF = e.PosF;
                    e.BestGoalDist = float.MaxValue;
                }
                e.State = EntityState.Attacking;   // 拆塔结算在下方的攻击循环

                // 攻击循环(冷却共享)。兜底:AttackIntervalSec<=0 的单位(裸构造/配置缺失)
                // 会让 AttackCooldown += 0 永不出循环 —— 2026-09-11 实测踩死过两轮测试,
                // 属历史设计假定"单位必来自配置"的漏洞,这里直接挡掉。
                if (e.AttackIntervalSec <= 0f) continue;
                while (e.AttackCooldown <= 0f)
                {
                    float dmg = (float)Rate(sim, e) * e.AttackIntervalSec;
                    if (e.Hero == HeroId.ZhuFeng && e.IsCharging && !e.ChargeHitUsed)
                    {
                        dmg = (float)sim.Game.Siege.ZhuFengChargeHit;
                        e.ChargeHitUsed = true;
                    }

                    b.Hp -= dmg;
                    // 2026-09-14 ★制作人"射手攻击敌方建筑好像没有弹道":本系统给建筑扣血时**一个事件都不发**
                    // → BoardView 拿不到 DamageDealt → 没有弹道(箭塔当初是同一个毛病,见 TowerCombatSystem)。
                    // 这里按同一先例补发:纯驱动表现层(弹道/飘字/受击音),Sim 侧无监听者,不影响结算。
                    sim.Emit(new SimEvent(sim.Tick, SimEventType.DamageDealt, b.Id, dmg, e.Id));
                    e.AttackCooldown += (float)e.AttackIntervalSec;

                    if (b.Hp <= 0f)
                    {
                        if (b.Building == BuildingId.Base)
                        {
                            b.Hp = 0f;
                            sim.SetWinner(e.Side);
                        }
                        else if (b.IsFort)
                        {
                            // 2026-09-11 制作人:堡垒=某方放的临时防御工事,只有敌方会打它;
                            // 被打掉应当**直接消失**,不留废墟(非可重建资产)。
                            // 只有敌方打得动:己方摧毁令在 SiegeSystem.ValidateIntent 就被拒。
                            // 注意:此处仍在 foreach (sim.Entities) 内,不能直接移除,循环后统一处理。
                            b.Hp = 0f;
                            fortGone.Add(b.Id);
                            cleared.Add(b.Id);
                        }
                        else
                        {
                            b.Rubbled = true;
                            b.Side = Side.Neutral;
                            b.Hp = 0f;
                            b.Intent = SiegeIntent.None;
                            b.IntentIssuer = Side.Neutral;
                            GarrisonSystem.OnBuildingRubbled(sim, b);
                            cleared.Add(b.Id);
                        }
                        break;
                    }
                }
            }

            foreach (int fid in fortGone)
            {
                var b = sim.GetEntity(fid);
                if (b == null) continue;
                b.GarrisonFormal.Clear();
                b.GarrisonTemp.Clear();
                sim.RemoveEntity(fid);
            }

            if (cleared.Count > 0)
            {
                foreach (var kv in sim.Entities)
                {
                    var u = kv.Value;
                    if (u.Kind == EntityKind.Unit && u.TargetBuildingId != 0 && cleared.Contains(u.TargetBuildingId))
                    {
                        u.TargetBuildingId = 0;
                        u.State = EntityState.Idle;
                    }
                }
            }
        }

        /// <summary>
        /// 2026-09-15:拆建筑时的"贴墙落点" —— 从单位当前方位取占地矩形的最近边点,再沿外向推
        /// <c>0.5</c> 米后**量化到格**。落点与矩形边的实际距离落在 0.1~0.6 米之间
        /// (格中心恒为 k+0.5、矩形边不一定是格中心),所以 <see cref="SimCore.MeleeBuildingReach"/>
        /// 取 0.75 留出余量。远程单位改为按射程留站位(不出射程圈)。
        /// </summary>
        private static GridPos? BreachCell(Entity b, Entity u)
        {
            var c = SimVec2.FromGrid(b.Pos);
            if (DamageCalculator.IsRanged(u) && u.Range > 1f)
            {
                // 远程:站在射程内沿(留 1 米余量),别贴脸
                float want = System.Math.Min(SimCore.BuildingCollisionRadius(b) + 5f, System.Math.Max(1.5f, u.Range - 1f));
                var dv = u.PosF - c;
                float len = dv.Magnitude;
                var dirr = len > 0.01f ? new SimVec2(dv.X / len, dv.Y / len) : new SimVec2(1f, 0f);
                return TerrainMap.ClampToEllipse(c + dirr * want).ToGrid();
            }
            var edge = BuildingFootprint.ClosestPoint(b, u.PosF);
            var d = edge - c;
            var outv = d.Magnitude > 0.01f ? d.Normalized()
                : (u.PosF - c).Magnitude > 0.01f ? (u.PosF - c).Normalized() : new SimVec2(1f, 0f);
            return TerrainMap.ClampToEllipse(edge + outv * 0.5f).ToGrid();
        }

        private static bool EnemyInRange(SimCore sim, Entity e)
        {
            foreach (var kv in sim.Entities)
            {
                var o = kv.Value;
                if (o.Id == e.Id) continue;
                if (o.Kind != EntityKind.Unit) continue;
                if (o.Side == Side.Neutral || o.Side == e.Side) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Dead || o.State == EntityState.Exchanging) continue;
                float d = SimVec2.Distance(e.PosF, o.PosF);
                if (DamageCalculator.IsRanged(e) ? d <= e.Range : d <= 1.2f)
                    return true;
            }
            return false;
        }

        private static double Rate(SimCore sim, Entity e)
        {
            // 逐风升级·飞掠:拆塔 7/s
            if (e.Hero == HeroId.ZhuFeng && sim.IsUpgraded(e.Side, HeroId.ZhuFeng))
                return sim.Game.Siege.FlyoverPerSec;
            var s = sim.Game.Siege;
            switch (e.Hero)
            {
                case HeroId.ZhenYue: return s.ZhenYuePerSec;
                case HeroId.ZhuiRi: return s.RangedPerSec;
                case HeroId.XingLuo: return s.RangedPerSec;
                case HeroId.ZhuFeng: return s.ZhuFengPerSec;
                default: return s.MeleePerSec; // 磐石/断峰 及 Hero==null 单位
            }
        }

        /// <summary>对建筑结算伤害并走完整废墟/判负流程(攻城锥等直伤共用入口)。</summary>
        public static void ApplyBuildingDamage(SimCore sim, Entity b, float amount, Side attackerSide)
        {
            b.Hp -= amount;
            if (sim != null) sim.Emit(new SimEvent(sim.Tick, SimEventType.DamageDealt, b.Id, amount, 0));
            if (b.Hp > 0f) return;
            if (b.Building == BuildingId.Base)
            {
                b.Hp = 0f;
                sim.SetWinner(attackerSide);
                return;
            }
            b.Rubbled = true;
            b.Side = Side.Neutral;
            b.Hp = 0f;
            b.Intent = SiegeIntent.None;
            b.IntentIssuer = Side.Neutral;
            GarrisonSystem.OnBuildingRubbled(sim, b);
            foreach (var e in sim.Entities.Values)
                if (e.TargetBuildingId == b.Id) { e.TargetBuildingId = 0; e.State = EntityState.Idle; }
        }

        public static bool Rebuild(SimCore sim, int buildingId, Side side, out string error)
        {
            error = null;
            if (!sim.Entities.TryGetValue(buildingId, out var b) || b.Kind != EntityKind.Building)
            {
                error = "建筑不存在";
                return false;
            }
            if (!b.Rubbled)
            {
                error = "非废墟";
                return false;
            }
            if (!sim.SpendEp(side, (float)sim.Game.Ep.RebuildCost))
            {
                error = "EP 不足";
                return false;
            }
            b.Rubbled = false;
            b.Side = side;
            b.Hp = (float)sim.BuildingConfigOf(b.Building.Value).MaxHp;
            return true;
        }
    }
}
