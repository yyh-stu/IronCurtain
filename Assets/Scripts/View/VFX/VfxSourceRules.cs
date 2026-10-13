using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// M1.2 表现数据适配·来源身份规则(纯函数,可定向测试)。
    /// 事实来源见 M0_presentation-contract §1.2:
    /// - 墙体拦截路径会发一条"攻击者对自身"的 DamageDealt(WallSystem 注释在案)→ 必须滤除自伤;
    /// - AttackerId=0 = 无明确来源,不得臆测(不是攻城锥、不是塔);
    /// - 驻军射手主将的弹道出发点应挂其驻扎建筑(GarrisonBuildingId),冷却/归属仍在主将;
    /// - 一击多伤:同 tick 同攻击者多受害者只播一次出招——已知局限:同 tick 真多次合法攻击会被
    ///   误聚合(AttackSeq 契约维持备选,出现实测误聚合再提)。
    /// </summary>
    public static class VfxSourceRules
    {
        /// <summary>墙体拦截的自伤伤害事件(攻击者==受害者):不产生命中/弹道表现。</summary>
        public static bool IsWallSelfDamage(SimEvent e) =>
            e.Type == SimEventType.DamageDealt && e.AttackerId != 0 && e.AttackerId == e.SubjectId;

        /// <summary>是否有可归因来源(AttackerId=0 → 无)。</summary>
        public static bool HasSource(SimEvent e) => e.AttackerId != 0;

        /// <summary>发射点实体:驻军射手 → 其驻扎建筑;其余 → 自身;无来源 → 0。</summary>
        public static int EmitterEntityId(SimCore sim, int attackerId)
        {
            if (attackerId == 0) return 0;
            var a = sim.GetEntity(attackerId);
            if (a == null) return 0;
            if (a.Kind == EntityKind.Unit && a.GarrisonBuildingId != 0) return a.GarrisonBuildingId;
            return a.Id;
        }

        /// <summary>结算后摘要的表现分类(纯数据推导,无 View 重算伤害)。</summary>
        public enum SettledKind : byte { ShieldAbsorb, ShieldBreak, Damage, Death }

        public static SettledKind Classify(SimEvent e)
        {
            if (e.Type != SimEventType.SettledDamage) throw new System.ArgumentException("not SettledDamage");
            if (e.Died) return SettledKind.Death;
            bool shieldDrop = e.ShieldAfter < e.ShieldBefore;
            if (e.ShieldBefore > 0f && e.ShieldAfter <= 0f) return SettledKind.ShieldBreak;
            if (shieldDrop) return SettledKind.ShieldAbsorb;
            return SettledKind.Damage;
        }
    }

    /// <summary>同 tick 同攻击者出招去重(一击多伤只出一次招)。
    /// 环形容量按"单 tick 最大受害者数"量级取值;跨 tick 自动过期。</summary>
    public sealed class VfxSwingDedup
    {
        private readonly Dictionary<long, HashSet<int>> _byTick = new Dictionary<long, HashSet<int>>();
        private readonly int _cap;

        public VfxSwingDedup(int maxPerTick = 512) { _cap = maxPerTick; }

        /// <summary>本 tick 该攻击者是否首次出招(true=播出招,false=已被同 swing 覆盖)。</summary>
        public bool FirstOfSwing(int attackerId, long tick)
        {
            if (!_byTick.TryGetValue(tick, out var set))
            {
                if (_byTick.Count > 64) _byTick.Clear();   // 简单回收:远旧 tick 整体丢弃
                set = new HashSet<int>();
                _byTick[tick] = set;
            }
            if (set.Count >= _cap) return false;           // 超量保守:不出招(装饰级降级)
            return set.Add(attackerId);
        }
    }
}
