using System;

namespace IronCurtain.Sim
{
    public class HeroCfg
    {
        public HeroId Id;
        public string Name;
        public string Tag;
        public int MaxHp;
        public int Attack;
        public double AttackIntervalSec;
        public double MoveSpeed;
        public double ChargeSpeed;
        public int Range;
        public int Population;
        public int Shield;
        public string PassiveText;
    }

    public class BuildingCfg
    {
        public BuildingId Id;
        public string Name;
        public int MaxHp;
        public int Attack;
        public double AttackIntervalSec;
        public int SplashDamage;
        public int SplashRange;
        public int Range;
        public int PopulationBonus;
        public bool ProducesEp;
        public double EpIntervalSec;
        public int GarrisonFormal;
        public int GarrisonTemp;
        public int QueueCount;
        public bool CanGarrison;
    }

    public class CounterCfg
    {
        public HeroId Attacker;
        public HeroId Target;
        public string Description;
        public double Multiplier;
    }

    public class DamageCfg
    {
        public double ZhenYueArmor;
        public double ZhenYueVsPanShi;
        public double ZhuFengCharge;
        public double XingLuoVsShield;
        public double ZhenYueRangedTaken;
        public double ZhuFengRangedTaken;
    }

    public class ProductionCfg
    {
        public double Pop1Seconds;
        public double ZhenYueSeconds;
        public double ZhenYueUpgradedSeconds;
        public int QueuesPerBarracks;
        public int BasePopulation;
        public int ResourcePointPopulation;
        public int InitialOwnedResourcePoints;
        public int MaxResourcePoints;
    }

    public class EpCfg
    {
        public int Cap;
        public int Start;
        public double IntervalSeconds;
        public int UpgradeCost;
        public int HexDrawCost;
        public int RebuildCost;
        public int RamCost;
        public int FortCost;
    }

    public class UpgradeCfg
    {
        public HeroId Hero;
        public string Name;
        public string Description;
        public int Cost;
        public double MoveSpeedOverride;
        public double ProductionSecondsOverride;
        public int AttackOverride;
        public double SectorDamageMultiplier;
        public double SectorAngleDeg;
        public double SectorRadius;
        public double FlyoverDamage;
        public double FlyoverRange;
        public double TowerDamagePerSecOverride;
        public double FragileBonus;
        public bool ImmuneAllDebuffs;
        public double MaxHpPercentBonus;
    }

    public class HexCfg
    {
        public HexId Id;
        public string Name;
        public string Description;
        public double DurationSeconds;
        public int[] DrawNodes;
        public int UnlockMinute;
        public double UsedWeightPenalty;
    }

    public class SiegeCfg
    {
        public double MeleePerSec;
        public double ZhenYuePerSec;
        public double RangedPerSec;
        public double ZhuFengPerSec;
        public double ZhuFengChargeHit;
        public double FlyoverPerSec;
    }

    public class MatchCfg
    {
        public int BaseHp;
        public int DurationMinutes;
        public int OvertimeMinutes;
        public int OvertimePopBonus;
        public double OvertimeProductionBonus;
        public double OvertimeEpInterval;
        public int ScoreBaseHpWeight;
        public int ScoreHeroWeight;
        public int ScoreEpWeight;
        public int ScoreBuildingWeight;
    }

    public class ToolCfg
    {
        public double RamDamage;
        public int FortHp;
        public int FortGarrison;
        public double FortDurationSeconds;
    }

    public class TowerGarrisonCfg
    {
        public double AttackPerGarrison;
        public double AttackBonusCap;
        public int SplashRangeBonusAtGarrison;
        public int SplashRangeBonus;
    }
}
