using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>主将战绩(2026-09-11 制作人:平安京式结算面板)。按(方,英雄)聚合,本局出场即有条目:
    /// 对英雄伤害/承伤/对建筑伤害(含城墙)/巷战杀敌(进城交换击杀)。纯 Sim,静态表随 MatchSetup.Reset 清空。</summary>
    public static class MatchStats
    {
        public struct Entry
        {
            public float HeroDamage;      // 对英雄(非士兵单位)伤害总量
            public float DamageTaken;     // 承受伤害总量(英雄与士兵承伤都归其主将)
            public float BuildingDamage;  // 对建筑伤害总量(含城墙)
            public int AlleyKills;        // 巷战杀敌:进城交换中消灭的敌兵数
        }

        private static readonly Dictionary<int, Entry> _table = new Dictionary<int, Entry>();

        static int Key(Side side, HeroId hero) { return ((int)side << 8) | (int)hero; }

        public static void Reset() => _table.Clear();

        public static void AddHeroDamage(Side side, HeroId hero, float amount)
        {
            _table.TryGetValue(Key(side, hero), out var e);   // 无则 default 起步
            e.HeroDamage += amount; _table[Key(side, hero)] = e;
        }

        public static void AddDamageTaken(Side side, HeroId hero, float amount)
        {
            _table.TryGetValue(Key(side, hero), out var e);   // 无则 default 起步
            e.DamageTaken += amount; _table[Key(side, hero)] = e;
        }

        public static void AddBuildingDamage(Side side, HeroId hero, float amount)
        {
            _table.TryGetValue(Key(side, hero), out var e);   // 无则 default 起步
            e.BuildingDamage += amount; _table[Key(side, hero)] = e;
        }

        public static void AddAlleyKill(Side side, HeroId hero, int count = 1)
        {
            _table.TryGetValue(Key(side, hero), out var e);   // 无则 default 起步
            e.AlleyKills += count; _table[Key(side, hero)] = e;
        }

        /// <summary>取某方全部主将条目(按英雄),未出场返回空。</summary>
        public static IReadOnlyDictionary<HeroId, Entry> SideEntries(Side side)
        {
            var r = new Dictionary<HeroId, Entry>();
            foreach (var kv in _table)
            {
                if ((kv.Key >> 8) != (int)side) continue;
                r[(HeroId)(kv.Key & 0xFF)] = kv.Value;
            }
            return r;
        }

        public static Entry Of(Side side, HeroId hero)
        {
            return _table.TryGetValue(Key(side, hero), out var e) ? e : default;
        }
    }
}
