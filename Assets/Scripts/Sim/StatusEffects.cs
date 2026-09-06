using System.Collections.Generic;

namespace IronCurtain.Sim
{
    public enum DebuffType
    {
        Slow,     // 迟滞:移速-30%、攻速-10%
        Silence,  // 沉默:禁止冲锋
        Wound,    // 重伤:全属性-15%(移速/攻击-15%,承伤+15%)
        Fragile   // 脆弱(坠日升级被动):承伤+10%
    }

    /// <summary>
    /// 单位状态效果集合。所有时长以"秒"为单位,由 CombatUpdate 每 tick 衰减。
    /// </summary>
    public sealed class StatusEffects
    {
        private readonly Dictionary<DebuffType, float> _remaining = new Dictionary<DebuffType, float>();

        public bool Has(DebuffType t) => _remaining.ContainsKey(t) && _remaining[t] > 0f;

        /// <summary>施加/刷新:新时长大于剩余时才刷新(同 GDD "多重减速只保留最高比例" 思路)。</summary>
        public void Apply(DebuffType t, float seconds)
        {
            if (!_remaining.TryGetValue(t, out float cur) || seconds > cur)
                _remaining[t] = seconds;
        }

        public void Tick(float dt)
        {
            var keys = new List<DebuffType>(_remaining.Keys);
            foreach (var k in keys)
            {
                _remaining[k] -= dt;
                if (_remaining[k] <= 0f) _remaining.Remove(k);
            }
        }

        public void Clear() => _remaining.Clear();

        public float MoveMultiplier => Has(DebuffType.Slow) ? 0.7f : Has(DebuffType.Wound) ? 0.85f : 1f;

        /// <summary>攻击间隔乘数(攻速):迟滞 +10%。</summary>
        public float AttackIntervalMultiplier => Has(DebuffType.Slow) ? 1.1f : 1f;

        /// <summary>输出增幅(加数,D28):重伤 -0.15。</summary>
        public float AttackBonus => Has(DebuffType.Wound) ? -0.15f : 0f;

        /// <summary>承伤增幅(加数,D28):重伤 +0.15、脆弱 +0.10,同层加算。</summary>
        public float DamageTakenBonus
        {
            get
            {
                float b = 0f;
                if (Has(DebuffType.Wound)) b += 0.15f;
                if (Has(DebuffType.Fragile)) b += 0.10f;
                return b;
            }
        }

        /// <summary>免伤积(乘数,D28):海克斯免伤逐项乘算;默认 1f。</summary>
        public float DamageReduction = 1f;
    }
}
