using System.Linq;
using NUnit.Framework;
using UnityEngine;
using IronCurtain.Sim;

public class ConfigTests
{
    private static GameConfig Load()
    {
        var asset = Resources.Load<TextAsset>("gameconfig");
        Assert.NotNull(asset, "gameconfig.json not found in Resources");
        return GameConfigParser.FromJson(asset.text);
    }

    [Test]
    public void FromJson_DoesNotThrow_AndParsesAllSections()
    {
        var c = Load();
        Assert.AreEqual(6, c.Heroes.Length);
        Assert.AreEqual(4, c.Buildings.Length);
        Assert.AreEqual(6, c.Counters.Length);
        Assert.AreEqual(6, c.Upgrades.Length);
        Assert.AreEqual(8, c.Hexes.Length);
        Assert.NotNull(c.Damage);
        Assert.NotNull(c.Production);
        Assert.NotNull(c.Ep);
        Assert.NotNull(c.Siege);
        Assert.NotNull(c.Match);
        Assert.NotNull(c.Tools);
        Assert.NotNull(c.TowerGarrison);
    }

    [Test]
    public void Validate_ReturnsEmpty()
    {
        var c = Load();
        var result = GameConfigValidator.Validate(c);
        Assert.AreEqual("", result, result);
    }

    [Test]
    public void HeroKeyValues_MatchGdd()
    {
        var c = Load();
        var zy = c.Heroes.First(h => h.Id == HeroId.ZhenYue);
        Assert.AreEqual(200, zy.MaxHp);
        Assert.AreEqual(14, zy.Attack);
        Assert.AreEqual(2.0, zy.AttackIntervalSec);
        Assert.AreEqual(2, zy.Population);

        var ps = c.Heroes.First(h => h.Id == HeroId.PanShi);
        Assert.AreEqual(140, ps.Shield);

        var xl = c.Heroes.First(h => h.Id == HeroId.XingLuo);
        Assert.AreEqual(5, xl.Attack);
        Assert.AreEqual(1.0, xl.AttackIntervalSec);

        var zf = c.Heroes.First(h => h.Id == HeroId.ZhuFeng);
        Assert.AreEqual(3.0, zf.MoveSpeed);
        Assert.AreEqual(4.0, zf.ChargeSpeed);
    }

    [Test]
    public void Counters_AllSixPresent_AndDamageFactors()
    {
        var c = Load();
        Assert.AreEqual(2.5, c.Damage.ZhenYueVsPanShi);
        Assert.AreEqual(2.0, c.Damage.ZhuFengCharge);
        Assert.AreEqual(0.5, c.Damage.XingLuoVsShield);
        Assert.AreEqual(0.7, c.Damage.ZhenYueRangedTaken);
        Assert.AreEqual(1.25, c.Damage.ZhuFengRangedTaken);
        Assert.AreEqual(0.8, c.Damage.ZhenYueArmor);
        Assert.AreEqual(6, c.Counters.Length);
        Assert.IsTrue(c.Counters.Any(x => x.Attacker == HeroId.ZhenYue && x.Target == HeroId.PanShi && x.Multiplier == 2.5));
        Assert.IsTrue(c.Counters.Any(x => x.Attacker == HeroId.ZhuFeng && x.Multiplier == 2.0));
    }

    [Test]
    public void Hexes_NodesUnlockAndPenalty()
    {
        var c = Load();
        foreach (var hx in c.Hexes)
        {
            Assert.IsTrue(hx.DrawNodes.SequenceEqual(new[] { 3, 5, 7, 9 }), hx.Id + " nodes");
            Assert.AreEqual(3, hx.UnlockMinute);
            Assert.AreEqual(0.5, hx.UsedWeightPenalty);
        }
        Assert.AreEqual(0, c.Hexes.First(h => h.Id == HexId.Vengeance).DurationSeconds);
    }

    [Test]
    public void ToolsAndTowerGarrison_MatchDecisions()
    {
        var c = Load();
        Assert.AreEqual(15.0, c.Tools.RamDamage);
        Assert.AreEqual(100, c.Tools.FortHp); // 2026-09-11 制作人:堡垒 100 血(道具口径)
        // 2026-09-17 死配置清理:FortGarrison 断言删除 —— 堡垒不可驻军(2026-09-11 裁决)后该配置零引用,已从 json+POCO 移除
        Assert.AreEqual(30.0, c.Tools.FortDurationSeconds);
        Assert.AreEqual(2.0, c.TowerGarrison.AttackPerGarrison);
        Assert.AreEqual(12.0, c.TowerGarrison.AttackBonusCap);
        Assert.AreEqual(9.0, c.TowerGarrison.SplashRangeBonusAtGarrison);
        Assert.AreEqual(1.0, c.TowerGarrison.SplashRangeBonus);
    }

    [Test]
    public void EconomyValues_MatchGdd()
    {
        var c = Load();
        Assert.AreEqual(5, c.Ep.Cap);
        Assert.AreEqual(2, c.Ep.UpgradeCost);
        Assert.AreEqual(2, c.Ep.HexDrawCost);
        Assert.AreEqual(90.0, c.Ep.IntervalSeconds);
        // 2026-09-17 死配置清理:v1.6 兵营时代遗留(Pop1Seconds/ZhenYueSeconds/QueuesPerBarracks,
        // 将兵化后生产口径 = ProductionSystem.GeneralProductionSeconds=20s 常量 + ZhenYueUpgradedSeconds 配置)
        // 已从 json+POCO 移除;此处改断言**现行口径**。
        Assert.AreEqual(7.5, c.Production.ZhenYueUpgradedSeconds);
        Assert.AreEqual(20f, ProductionSystem.GeneralProductionSeconds);
        var tower = c.Buildings.First(b => b.Id == BuildingId.Tower);
        Assert.AreEqual(18, tower.Attack);
        Assert.AreEqual(12, tower.SplashDamage);
        Assert.AreEqual(2.5, tower.AttackIntervalSec);
        Assert.AreEqual(5, tower.Range);
        Assert.AreEqual(200, c.Match.BaseHp);
    }
}
