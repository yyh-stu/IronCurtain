using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

public class TowerCombatTests
{
    private static GameConfig _cfg;
    private SimCore sim;
    private const float Dt = 0.1f;

    [SetUp]
    public void SetUp()
    {
        if (_cfg == null)
            _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(1));
        sim.Game = _cfg;
    }

    private Entity SpawnTower(int row, int col, Side side)
        => sim.SpawnBuilding(side, BuildingId.Tower, new GridPos(row, col));

    private Entity SpawnUnit(int row, int col, Side side, HeroId kind)
    {
        var c = sim.Game.Heroes[(int)kind];
        var u = sim.SpawnEntity(side, EntityKind.Unit, new GridPos(row, col));
        u.Hero = kind;
        u.MaxHp = c.MaxHp; u.Hp = c.MaxHp;
        u.Attack = c.Attack;
        u.AttackIntervalSec = (float)c.AttackIntervalSec;
        u.Range = c.Range; u.MoveSpeed = (float)c.MoveSpeed;
        if (c.Shield > 0) { u.Shield = c.Shield; u.ShieldMax = c.Shield; }
        return u;
    }

    /// <summary>驻军口径:生成蓝方兵团(远离战场),取其士兵 id 登记进塔的正式驻军列表。</summary>
    private List<int> GarrisonSoldierIds(SimCore s, int count)
    {
        var g = RegimentSystem.SpawnGeneral(s, Side.Blue, HeroId.PanShi, new GridPos(15, 30));
        var ids = s.Entities.Values
            .Where(x => x.Kind == EntityKind.Unit && x.IsSoldier && x.GeneralId == g.Id)
            .OrderBy(x => x.FormationSlot).Take(count).Select(x => x.Id).ToList();
        Assert.AreEqual(count, ids.Count, "驻军士兵数量");
        return ids;
    }

    private void RunTicks(int n)
    {
        for (int i = 0; i < n; i++) TowerCombatSystem.Tick(sim, Dt);
    }

    [Test]
    public void Tower_FiresAtRange5_FirstShotAtTick25()
    {
        SpawnTower(1, 6, Side.Blue);
        var rock = SpawnUnit(1, 11, Side.Red, HeroId.PanShi); // 距离 5,远程非破防 70% 减伤
        float shield0 = rock.Shield;

        RunTicks(1); // 入战立即首炮(与单位战斗一致)
        float expected = shield0 - 18f * 0.7f;
        Assert.AreEqual(expected, rock.Shield, 0.01f, "失败:首炮应造成 18×0.7=12.6 盾伤");

        float afterFirst = rock.Shield;
        RunTicks(24); // 共 25 tick:间隔 2.5s 未满,不应开第二炮
        Assert.AreEqual(afterFirst, rock.Shield, 0.01f, "失败:间隔内不应再次开火");
        RunTicks(1); // 第 26 tick 第 2 炮
        Assert.AreEqual(afterFirst - 18f * 0.7f, rock.Shield, 0.01f, "失败:第 2 炮应在第 26 tick 落地");
    }

    [Test]
    public void Tower_DoesNotFire_OutOfRange()
    {
        SpawnTower(1, 6, Side.Blue);
        var rock = SpawnUnit(1, 12, Side.Red, HeroId.PanShi); // 距离 6 > 射程 5
        RunTicks(100);
        Assert.AreEqual(rock.ShieldMax, rock.Shield, 0.01f, "失败:射程外目标不应受到任何伤害");
        Assert.AreEqual(rock.MaxHp, rock.Hp, 0.01f, "失败:射程外目标血量不应变化");
    }

    [Test]
    public void Tower_Splash_HitsNeighbor_SparesAllies()
    {
        SpawnTower(1, 6, Side.Blue);
        var main = SpawnUnit(1, 10, Side.Red, HeroId.DuanFeng);
        var sub = SpawnUnit(0, 10, Side.Red, HeroId.DuanFeng);
        var ally = SpawnUnit(1, 9, Side.Blue, HeroId.DuanFeng); // 塔方单位在溅射半径内

        RunTicks(25);
        Assert.AreEqual(main.MaxHp - 18f, main.Hp, 0.01f, "失败:主目标应受 18 点伤害(断峰无额外修正)");
        Assert.AreEqual(sub.MaxHp - 12f, sub.Hp, 0.01f, "失败:距主目标曼哈顿 1 的副目标应受溅射 12");
        Assert.AreEqual(ally.MaxHp, ally.Hp, 0.01f, "失败:溅射不应伤害塔方单位");
    }

    [Test]
    public void Tower_Garrison3_Bonus6()
    {
        var tower = SpawnTower(1, 6, Side.Blue);
        foreach (var id in GarrisonSoldierIds(sim, 3)) tower.GarrisonFormal.Add(id);
        var main = SpawnUnit(1, 10, Side.Red, HeroId.DuanFeng);

        RunTicks(25);
        Assert.AreEqual(main.MaxHp - 24f, main.Hp, 0.01f, "失败:3 人驻军应 +6 攻击,主伤 = 18+6 = 24");
    }

    [Test]
    public void Tower_Garrison9_BonusCappedAt12()
    {
        var tower = SpawnTower(1, 6, Side.Blue);
        foreach (var id in GarrisonSoldierIds(sim, 9)) tower.GarrisonFormal.Add(id);
        var main = SpawnUnit(1, 10, Side.Red, HeroId.DuanFeng);

        RunTicks(25);
        Assert.AreEqual(main.MaxHp - 30f, main.Hp, 0.01f, "失败:9 人驻军加成封顶 +12,主伤 = 30(而非 36)");
    }

    [Test]
    public void Tower_Garrison9_SplashRangePlus1()
    {
        var towerFull = SpawnTower(1, 6, Side.Blue);
        foreach (var id in GarrisonSoldierIds(sim, 9)) towerFull.GarrisonFormal.Add(id);
        var main1 = SpawnUnit(1, 10, Side.Red, HeroId.DuanFeng);
        var far1 = SpawnUnit(1, 12, Side.Red, HeroId.DuanFeng); // 距主目标 2 格

        RunTicks(25);
        Assert.AreEqual(far1.MaxHp - 12f, far1.Hp, 0.01f, "失败:满 9 人时溅射半径 +1,距 2 格副目标应受溅射 12");

        // 不足 9 人:同布局但 8 人
        var sim2 = new SimCore(new SimConfig(), new SeededRng(1));
        sim2.Game = _cfg;
        var tower2 = sim2.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(1, 6));
        foreach (var id in GarrisonSoldierIds(sim2, 8)) tower2.GarrisonFormal.Add(id);
        Entity main2 = SpawnOn(sim2, 1, 10, Side.Red, HeroId.DuanFeng);
        Entity far2 = SpawnOn(sim2, 1, 12, Side.Red, HeroId.DuanFeng);
        for (int i = 0; i < 25; i++) TowerCombatSystem.Tick(sim2, Dt);
        Assert.AreEqual(far2.MaxHp, far2.Hp, 0.01f, "失败:不足 9 人时距主目标 2 格不应受溅射");
    }

    private static Entity SpawnOn(SimCore s, int row, int col, Side side, HeroId kind)
    {
        var c = s.Game.Heroes[(int)kind];
        var u = s.SpawnEntity(side, EntityKind.Unit, new GridPos(row, col));
        u.Hero = kind;
        u.MaxHp = c.MaxHp; u.Hp = c.MaxHp;
        u.Attack = c.Attack;
        u.AttackIntervalSec = (float)c.AttackIntervalSec;
        u.Range = c.Range; u.MoveSpeed = (float)c.MoveSpeed;
        if (c.Shield > 0) { u.Shield = c.Shield; u.ShieldMax = c.Shield; }
        return u;
    }

    [Test]
    public void Tower_ZhenYueModifier_10Point08()
    {
        SpawnTower(1, 6, Side.Blue);
        var zhen = SpawnUnit(1, 11, Side.Red, HeroId.ZhenYue); // 距离 5
        float hp0 = zhen.Hp;

        RunTicks(25);
        float dealt = hp0 - zhen.Hp + (zhen.ShieldMax - zhen.Shield);
        Assert.AreEqual(18f * 0.8f * 0.7f, dealt, 0.01f, "失败:命中镇岳应为 18×0.8×0.7 = 10.08(血量差,容差 0.01)");
    }

    [Test]
    public void Tower_SkipsGarrisoned_And_Exchanging()
    {
        SpawnTower(1, 6, Side.Blue);
        var garrisoned = SpawnUnit(1, 10, Side.Red, HeroId.DuanFeng);
        garrisoned.GarrisonBuildingId = 888; // 驻军中,非零

        var exchanging = SpawnUnit(2, 10, Side.Red, HeroId.DuanFeng);
        exchanging.State = EntityState.Exchanging;

        RunTicks(50);
        Assert.AreEqual(garrisoned.MaxHp, garrisoned.Hp, 0.01f, "失败:驻军中的单位(GarrisonBuildingId≠0)不应被攻击");
        Assert.AreEqual(exchanging.MaxHp, exchanging.Hp, 0.01f, "失败:Exchanging 状态的单位不应被攻击");
    }
}
