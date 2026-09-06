using System;
using System.Linq;
using System.Text;

namespace IronCurtain.Sim
{
    public static class GameConfigValidator
    {
        public static string Validate(GameConfig c)
        {
            var e = new StringBuilder();

            void Err(string m) => e.Append(m).Append("; ");

            if (c == null) return "config is null";

            // Heroes
            if (c.Heroes == null || c.Heroes.Length != 6) Err("Heroes length != 6");
            else
            {
                var ids = c.Heroes.Select(h => (int)h.Id).OrderBy(i => i).ToArray();
                if (ids.Distinct().Count() != 6 || ids[0] != 0 || ids[5] != 5) Err("Heroes ids not 0..5 unique");
                foreach (var h in c.Heroes)
                {
                    if (h.AttackIntervalSec <= 0) Err(h.Id + " interval<=0");
                    double dps = h.Attack / h.AttackIntervalSec;
                    var expect = ExpectedDps(h.Id);
                    if (Math.Abs(dps - expect) > 0.01) Err(h.Id + " DPS " + dps + " != " + expect);
                }
            }

            // Buildings
            if (c.Buildings == null || c.Buildings.Length != 4) Err("Buildings length != 4");
            else
            {
                foreach (var b in c.Buildings)
                {
                    if (b.Id == BuildingId.Tower)
                    {
                        if (b.Attack <= 0) Err("Tower attack<=0");
                        if (b.AttackIntervalSec <= 0) Err("Tower interval<=0");
                        if (b.Attack != 18 || b.SplashDamage != 12 || b.AttackIntervalSec != 2.5 || b.Range != 5)
                            Err("Tower values mismatch");
                    }
                    if (b.Id == BuildingId.Base && b.CanGarrison) Err("Base can garrison");
                    if (b.Id == BuildingId.Barracks && b.QueueCount != 3) Err("Barracks queue != 3");
                }
            }

            // Counters
            if (c.Counters == null || c.Counters.Length != 6) Err("Counters length != 6");
            else
            {
                if (!c.Counters.Any(x => x.Attacker == HeroId.ZhenYue && x.Target == HeroId.PanShi)) Err("missing ZhenYue->PanShi");
                if (!c.Counters.Any(x => x.Attacker == HeroId.PanShi)) Err("missing PanShi counter");
                if (!c.Counters.Any(x => x.Attacker == HeroId.ZhuiRi && x.Target == HeroId.ZhenYue)) Err("missing ZhuiRi->ZhenYue");
                if (!c.Counters.Any(x => x.Attacker == HeroId.DuanFeng && x.Target == HeroId.ZhuFeng)) Err("missing DuanFeng->ZhuFeng");
                if (!c.Counters.Any(x => x.Attacker == HeroId.XingLuo && x.Target == HeroId.ZhuFeng)) Err("missing XingLuo->ZhuFeng");
                if (!c.Counters.Any(x => x.Attacker == HeroId.ZhuFeng && x.Target == HeroId.ZhuiRi)) Err("missing ZhuFeng->ZhuiRi");
            }

            // Damage
            if (c.Damage == null) Err("Damage null");
            else
            {
                if (Math.Abs(c.Damage.ZhenYueArmor - 0.8) > 0.001) Err("ZhenYueArmor != 0.8");
                if (Math.Abs(c.Damage.ZhenYueVsPanShi - 2.5) > 0.001) Err("ZhenYueVsPanShi != 2.5");
                if (Math.Abs(c.Damage.ZhuFengCharge - 2.0) > 0.001) Err("ZhuFengCharge != 2.0");
                if (Math.Abs(c.Damage.XingLuoVsShield - 0.5) > 0.001) Err("XingLuoVsShield != 0.5");
                if (Math.Abs(c.Damage.ZhenYueRangedTaken - 0.7) > 0.001) Err("ZhenYueRangedTaken != 0.7");
                if (Math.Abs(c.Damage.ZhuFengRangedTaken - 1.25) > 0.001) Err("ZhuFengRangedTaken != 1.25");
            }

            // Key hero checks
            var zy = c.Heroes?.FirstOrDefault(h => h.Id == HeroId.ZhenYue);
            if (zy != null && (zy.MaxHp != 200 || zy.Attack != 14)) Err("ZhenYue 200/14 mismatch");
            var ps = c.Heroes?.FirstOrDefault(h => h.Id == HeroId.PanShi);
            if (ps != null && ps.Shield != 140) Err("PanShi shield != 140");

            // Upgrades
            if (c.Upgrades == null || c.Upgrades.Length != 6) Err("Upgrades length != 6");
            else
            {
                var hs = c.Upgrades.Select(u => u.Hero).Distinct().Count();
                if (hs != 6) Err("Upgrades do not cover all heroes");
                foreach (var u in c.Upgrades) if (u.Cost != 2) Err(u.Hero + " upgrade cost != 2");
            }

            // Hexes
            if (c.Hexes == null || c.Hexes.Length != 8) Err("Hexes length != 8");
            else
            {
                foreach (var hx in c.Hexes)
                {
                    if (hx.DrawNodes == null || !hx.DrawNodes.SequenceEqual(new[] { 3, 5, 7, 9 })) Err(hx.Id + " DrawNodes != {3,5,7,9}");
                    if (hx.Id != HexId.Vengeance && hx.DurationSeconds <= 0) Err(hx.Id + " duration<=0");
                }
            }

            // EP
            if (c.Ep == null) Err("Ep null");
            else if (c.Ep.Cap != 5) Err("Ep cap != 5");

            // Match
            if (c.Match == null) Err("Match null");
            else if (c.Match.BaseHp != 200) Err("Match.BaseHp != 200");

            // Production hint (55 pop in ~110s)
            if (c.Production != null)
            {
                // 提示性校验:不做硬性失败
            }

            return e.ToString().TrimEnd(' ', ';');
        }

        private static double ExpectedDps(HeroId id)
        {
            switch (id)
            {
                case HeroId.PanShi: return 6.0 / 2.0;
                case HeroId.ZhenYue: return 14.0 / 2.0;
                case HeroId.DuanFeng: return 14.0 / 2.5;
                case HeroId.ZhuFeng: return 15.0 / 2.0;
                case HeroId.ZhuiRi: return 10.0 / 2.0;
                case HeroId.XingLuo: return 5.0 / 1.0;
                default: return 0;
            }
        }
    }
}
