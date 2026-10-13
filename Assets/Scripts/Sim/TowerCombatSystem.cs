using System;
using System.Collections.Generic;
using System.Linq;

namespace IronCurtain.Sim
{
    /// <summary>
    /// M3.2 箭塔自动战斗系统:自动攻击 / 溅射 / 驻军强化。
    /// 只做数值变更(扣护盾/扣血/标记死亡),不发事件,不移除实体——由编排者后续统一收口。
    /// </summary>
    public static class TowerCombatSystem
    {
        /// <summary>浮点比较容差(防 0.1f 累积漂移,编排者补)。</summary>
        private const float Epsilon = 1e-4f;
        public static void Tick(SimCore sim, float dt)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));

            foreach (var tower in sim.Entities.Values
                         .Where(e => e.Kind == EntityKind.Building && e.Building == BuildingId.Tower
                                     // 2026-09-15 制作人:"**被摧毁的箭塔还是会攻击,有点抽象**"。
                                     // 这里原来**完全不看 `Rubbled`** → 已被拆成废墟的箭塔照常开火
                                     // (探针实测:塔变废墟、驻军清 0、`Side=Neutral` 之后,5 秒仍打掉红单位 20.2 血)。
                                     // 更歪的是:废墟的 `Side` 被置成 `Neutral`,而找目标那条过滤是
                                     // `if (e.Side == tower.Side) continue;` —— 中立塔拦不住任何一方,
                                     // 于是**废墟会对敌我双方开火**。废墟就是一堆石头,不该有任何主动行为。
                                     && !e.Rubbled)
                         .OrderBy(e => e.Id)
                         .ToList())
            {
                tower.AttackCooldown -= dt;
                if (tower.AttackCooldown > Epsilon) continue;

                var target = FindTarget(sim, tower);
                if (target == null)
                {
                    // 射程内无可攻击目标:冷却清 0,不空转
                    tower.AttackCooldown = 0f;
                    continue;
                }

                Fire(sim, tower, target);
                var cfgB = sim.BuildingConfigOf(BuildingId.Tower);
                tower.AttackCooldown = (float)cfgB.AttackIntervalSec;
            }
        }

        /// <summary>射程内最近的敌军单位(欧氏米制,平局取小 Id);跳过建筑、驻军中、交换中、已死亡单位。</summary>
        private static Entity FindTarget(SimCore sim, Entity tower)
        {
            var cfgB = sim.BuildingConfigOf(BuildingId.Tower);
            float range = cfgB.Range;

            Entity best = null;
            float bestDist = float.MaxValue;
            SimVec2 tc = SimVec2.FromGrid(tower.Pos);
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Unit) continue;
                if (e.Side == tower.Side) continue;
                if (e.State == EntityState.Dead) continue;
                if (e.GarrisonBuildingId != 0) continue;
                if (e.State == EntityState.Exchanging) continue;
                if (e.Hero == HeroId.ZhuFeng && sim.IsUpgraded(e.Side, HeroId.ZhuFeng) && e.State != EntityState.Attacking) continue; // 飞掠:非战斗途经不锁塔
                if (HexSystem.TowerExempt(sim, e.Side) && e.State != EntityState.Attacking) continue; // 曲径通幽

                float dist = SimVec2.Distance(tc, e.PosF);
                if (dist > range) continue;

                if (dist < bestDist || (System.Math.Abs(dist - bestDist) <= 1e-4f && best != null && e.Id < best.Id))
                {
                    best = e;
                    bestDist = dist;
                }
            }
            return best;
        }

        /// <summary>对主目标结算主伤害,并对溅射半径内其余敌军单位结算溅射伤害。</summary>
        private static void Fire(SimCore sim, Entity tower, Entity mainTarget)
        {
            var cfgB = sim.BuildingConfigOf(BuildingId.Tower);
            var g = sim.Game.TowerGarrison;

            // 驻军攻击加成:每人 +AttackPerGarrison,封顶 AttackBonusCap(不写入实体 Attack 字段)
            float bonus = (float)Math.Min(g.AttackBonusCap, tower.GarrisonFormal.Count * g.AttackPerGarrison);
            float attack = cfgB.Attack + bonus;

            // 主目标
            float dmg = DamageCalculator.CalculateDamage(tower, mainTarget, sim.Game, false);
            dmg = ScaleBase(dmg, cfgB.Attack, attack);
            ApplyDamage(sim, tower, mainTarget, dmg);

            // 溅射半径 = SplashRange + (满员时 +SplashRangeBonus)
            int splashRadius = cfgB.SplashRange;
            if (tower.GarrisonFormal.Count >= g.SplashRangeBonusAtGarrison)
                splashRadius += g.SplashRangeBonus;

            if (cfgB.SplashDamage > 0 && splashRadius > 0)
            {
                float splashDmg = DamageCalculator.CalculateDamage(tower, mainTarget, sim.Game, false);
                splashDmg = ScaleBase(splashDmg, cfgB.Attack, cfgB.SplashDamage);

                foreach (var e in sim.Entities.Values
                             .Where(e => IsValidSplashVictim(tower, mainTarget, e, splashRadius))
                             .OrderBy(e => e.Id)
                             .ToList())
                {
                    float d = DamageCalculator.CalculateDamage(tower, e, sim.Game, false);
                    d = ScaleBase(d, cfgB.Attack, cfgB.SplashDamage);
                    ApplyDamage(sim, tower, e, d);
                }
            }
        }

        /// <summary>把以基准攻击力算出的伤害等比换算到实际攻击力(驻军加成后)。基准为 0 时直接取实际伤害值。</summary>
        private static float ScaleBase(float calcDamage, float baseAttack, float actualAttack)
        {
            if (baseAttack <= 0f) return calcDamage;
            return calcDamage * (actualAttack / baseAttack);
        }

        private static bool IsValidSplashVictim(Entity tower, Entity mainTarget, Entity e, int splashRadius)
        {
            if (e == mainTarget) return false;
            if (e.Kind != EntityKind.Unit) return false;
            if (e.Side == tower.Side) return false;
            if (e.State == EntityState.Dead) return false;
            if (e.GarrisonBuildingId != 0) return false;
            if (e.State == EntityState.Exchanging) return false;
            return SimVec2.Distance(mainTarget.PosF, e.PosF) <= splashRadius;
        }

        /// <summary>先扣护盾,再扣血;血量归零标记 Dead。逻辑照抄 SimCore.ApplyAttack 的护盾顺序。</summary>
        private static void ApplyDamage(SimCore sim, Entity tower, Entity e, float damage)
        {
            if (damage <= 0f) return;
            float remain = damage;
            if (e.Shield > 0f)
            {
                float absorbed = Math.Min(e.Shield, remain);
                e.Shield -= absorbed;
                remain -= absorbed;
            }
            if (remain > 0f)
            {
                e.Hp -= remain;
                if (e.Hp <= 0f)
                {
                    e.Hp = 0f;
                    e.State = EntityState.Dead;
                }
            }
            // 2026-09-13 补发伤害事件。原实现按类注释"只做数值变更,不发事件",导致箭塔开火在表现层
            // **完全静默**:无弹道、无受击闪白、无飘字、无音效 —— 制作人据此判定"敌方箭塔不攻击我方军队"
            // (实测它在打,只是看不见)。单位攻击(SimCore.ApplyAttack)一直是发事件的,塔这里漏了。
            // 已确认 Sim 侧没有 DamageDealt 监听者,补发只驱动表现层,不影响结算。
            sim.Emit(new SimEvent(sim.Tick, SimEventType.DamageDealt, e.Id, damage, tower.Id));
        }
    }
}
