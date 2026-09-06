using System;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 英雄特殊机制(磐石回盾/星落沉默/坠日重伤/断峰迟滞/拒马/禁冲锋区)。
    /// 未定义参数采用决策记录默认值(见 docs 设计变更记录 D11-D16)。
    /// </summary>
    public static class HeroAbilitySystem
    {
        public const float SilenceSeconds = 2f;      // D15:沉默时长
        public const float PanShiRegenPerSec = 20f;  // D12:脱战回盾速率
        public const float PanShiRegenDelay = 5f;    // 脱战 5 秒开始回盾
        public const float PanShiChargeBlockRange = 1;  // D13:磐石禁冲锋范围(格)
        public const float DuanFengChargeBreakRange = 2; // 断峰拒马范围(格)

        /// <summary>命中后施加机制效果(在伤害结算之后调用)。</summary>
        public static void ApplyOnHitEffects(SimCore sim, Entity attacker, Entity defender)
        {
            // 士兵不继承英雄被动:任一方为士兵则全部跳过(士兵 Hero 字段仅用于渲染/类型标识)
            if (attacker.IsSoldier || defender.IsSoldier) return;

            // 磐石升级:完全免疫减速、沉默、重伤
            if (defender.Hero == HeroId.PanShi && sim.IsUpgraded(defender.Side, HeroId.PanShi))
                return;
            // 钢铁洪流:免疫负面(30s)
            if (HexSystem.DebuffImmune(sim, defender))
                return;

            // 星落:普攻沉默非镇岳单位
            if (attacker.Hero == HeroId.XingLuo && defender.Hero != HeroId.ZhenYue)
                defender.Status.Apply(DebuffType.Silence, SilenceSeconds);

            // 断峰:攻击自带迟滞
            if (attacker.Hero == HeroId.DuanFeng)
                defender.Status.Apply(DebuffType.Slow, 3f);

            // 坠日:攻击镇岳附加重伤
            if (attacker.Hero == HeroId.ZhuiRi && defender.Hero == HeroId.ZhenYue)
                defender.Status.Apply(DebuffType.Wound, 3f);

            // 坠日升级·诛心:攻击镇岳附加脆弱(承伤 +10%)
            if (attacker.Hero == HeroId.ZhuiRi && defender.Hero == HeroId.ZhenYue
                && sim.IsUpgraded(attacker.Side, HeroId.ZhuiRi))
                defender.Status.Apply(DebuffType.Fragile, 3f);
        }

        /// <summary>磐石脱战回盾:脱战满 5 秒后每秒回复 20 点。</summary>
        public static void UpdatePanShiShield(Entity e, float dt)
        {
            if (e.IsSoldier) return; // 士兵不继承英雄被动:磐石士兵不回盾
            if (e.Hero != HeroId.PanShi) return;
            if (e.SecondsSinceCombat < PanShiRegenDelay) return;
            if (e.Shield >= e.ShieldMax) return;
            e.Shield = Math.Min(e.ShieldMax, e.Shield + PanShiRegenPerSec * dt);
        }

        /// <summary>逐风冲锋约束:沉默或附近有磐石/断峰时冲锋被打断。</summary>
        public static bool CanCharge(Entity zf, SimCore sim)
        {
            if (zf.Status.Has(DebuffType.Silence)) return false;
            foreach (var o in sim.Entities.Values)
            {
                if (o.Kind != EntityKind.Unit || o.Side == zf.Side || o.State == EntityState.Dead) continue;
                if (o.IsSoldier) continue; // 士兵不是真磐石/断峰,不构成冲锋阻断源
                float d = SimVec2.Distance(zf.PosF, o.PosF);
                if (o.Hero == HeroId.PanShi && d <= PanShiChargeBlockRange) return false;
                if (o.Hero == HeroId.DuanFeng && d <= DuanFengChargeBreakRange) return false;
            }
            return true;
        }
    }
}
