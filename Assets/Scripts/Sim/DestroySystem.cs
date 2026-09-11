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

                // 距离校验(米制欧氏):近战需贴到建筑碰撞半径边缘,远程按射程
                float dist = SimVec2.Distance(e.PosF, SimVec2.FromGrid(b.Pos));
                if (DamageCalculator.IsRanged(e) ? dist > e.Range : dist > SimCore.BuildingCollisionRadius(b) + 0.4f)
                    continue;

                // 优先级 D26:射程内存在敌军单位 → 本 tick 不拆塔
                if (EnemyInRange(sim, e)) continue;

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
                            b.Sealed = false;
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
            b.Sealed = false;
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
