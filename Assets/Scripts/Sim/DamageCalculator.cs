namespace IronCurtain.Sim
{
    /// <summary>
    /// 统一伤害公式:最终伤害 = 基础攻击力 × 护甲系数 × 克制系数 × 特殊修正系数
    /// 数值全部来自 GameConfig.Damage;磐石非破防减伤 0.7 为 GDD 明文(表外常量)。
    /// M8.2b:士兵平伤——攻击方或防守方任一为士兵,伤害=攻击方 Attack(不继承英雄特殊规则)。
    /// </summary>
    public static class DamageCalculator
    {
        /// <summary>磐石护盾对非破防攻击的减伤系数(GDD 3.1.1 明文)。</summary>
        public const float PanShiShieldReduction = 0.7f;

        /// <summary>远程判定:射手英雄或箭塔。</summary>
        public static bool IsRanged(Entity a)
        {
            if (a.Kind == EntityKind.Building)
                return a.Building == BuildingId.Tower;
            return a.Hero == HeroId.ZhuiRi || a.Hero == HeroId.XingLuo;
        }

        public static float CalculateDamage(Entity attacker, Entity defender, GameConfig cfg, bool isCharge)
        {
            // 士兵平伤(M8.2b):任一方为士兵 → 攻击方 Attack 直接返回
            if (attacker.IsSoldier || defender.IsSoldier)
                return attacker.Attack;

            // 护甲系数:镇岳 0.8;坠日攻击镇岳强制重置为 1.0
            float armor = 1f;
            if (defender.Hero == HeroId.ZhenYue)
                armor = attacker.Hero == HeroId.ZhuiRi ? 1.0f : (float)cfg.Damage.ZhenYueArmor;

            // 克制系数:镇岳破防磐石 2.5;逐风冲锋(任意目标)2.0
            float counter = 1f;
            if (attacker.Hero == HeroId.ZhenYue && defender.Hero == HeroId.PanShi)
                counter *= (float)cfg.Damage.ZhenYueVsPanShi;
            if (isCharge && attacker.Hero == HeroId.ZhuFeng)
                counter *= (float)cfg.Damage.ZhuFengCharge;

            // 特殊修正:磐石盾减伤(破防除外)/镇岳受远程/逐风受远程
            float special = 1f;
            if (defender.Hero == HeroId.PanShi && attacker.Hero != HeroId.ZhenYue)
                special *= attacker.Hero == HeroId.XingLuo ? (float)cfg.Damage.XingLuoVsShield : PanShiShieldReduction;
            if (defender.Hero == HeroId.ZhenYue && IsRanged(attacker))
                special *= (float)cfg.Damage.ZhenYueRangedTaken;
            if (defender.Hero == HeroId.ZhuFeng && IsRanged(attacker))
                special *= (float)cfg.Damage.ZhuFengRangedTaken;

            return attacker.Attack * armor * counter * special;
        }
    }
}
