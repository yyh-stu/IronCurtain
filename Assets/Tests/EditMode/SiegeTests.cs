using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

public class SiegeTests
{
    static GameConfig _cfg;
    SimCore sim;
    int blueTowerId, redTowerId, neutralId;
    int defCol = 200; // 守方兵团生成列(远离战场,登记后由驻军逻辑接管)

    [SetUp]
    public void SetUp()
    {
        if (_cfg == null)
            _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(1));
        sim.Game = _cfg;
        SiegeSystem.Reset();
        blueTowerId = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 114)).Id;
        redTowerId = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 130)).Id;
        neutralId = sim.SpawnBuilding(Side.Neutral, BuildingId.ResourcePoint, new GridPos(15, 122)).Id;
        defCol = 200;
    }

    Entity SpawnUnit(Side side, GridPos pos, HeroId? hero = null)
    {
        var u = sim.SpawnEntity(side, EntityKind.Unit, pos);
        if (hero != null)
        {
            var c = sim.Game.Heroes[(int)hero.Value];
            u.Hero = hero; u.MaxHp = c.MaxHp; u.Hp = c.MaxHp; u.Attack = c.Attack;
            u.AttackIntervalSec = (float)c.AttackIntervalSec; u.Range = c.Range; u.MoveSpeed = (float)c.MoveSpeed;
            if (c.Shield > 0) { u.Shield = c.Shield; u.ShieldMax = c.Shield; }
        }
        else { u.MaxHp = 100; u.Hp = 100; u.MoveSpeed = 5f; }
        return u;
    }

    List<Entity> SoldiersOf(Entity g)
        => sim.Entities.Values
               .Where(x => x.Kind == EntityKind.Unit && x.IsSoldier && x.GeneralId == g.Id)
               .OrderBy(x => x.FormationSlot).ToList();

    /// <summary>守军:生成兵团并直接登记其士兵为正式驻军(驻军士兵不参与兵团跟随/索敌)。</summary>
    List<Entity> Garrison(int buildingId, int count, Side side)
    {
        var g = RegimentSystem.SpawnGeneral(sim, side, HeroId.PanShi, new GridPos(5, defCol));
        defCol += 25;
        var b = sim.Entities[buildingId];
        var defs = SoldiersOf(g).Take(count).ToList();
        foreach (var s in defs) { s.GarrisonBuildingId = buildingId; b.GarrisonFormal.Add(s.Id); }
        return defs;
    }

    bool RunUntil(System.Func<bool> cond, int maxTicks = 3000)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            sim.Step();
            if (cond()) return true;
        }
        return cond();
    }

    /// <summary>下攻城订单并令兵团士兵视作已抵城(士兵移动由 RegimentSystem 接管,HasMoveTarget 清除即抵达)。</summary>
    void AttackAndArrive(int buildingId, params Entity[] units)
    {
        var ids = new List<int>();
        foreach (var u in units) ids.Add(u.Id);
        Assert.IsTrue(SiegeSystem.IssueOrder(sim, ids, buildingId, out var err), "下订单失败:" + err);
        foreach (var u in units) u.HasMoveTarget = false;
    }

    [Test]
    public void T1_中立无人占领_直接占领并补配置血量_幸存士兵不驻入()
    {
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var u = SoldiersOf(g).First(); // 攻方士兵
        SiegeSystem.SetIntent(sim, neutralId, Side.Blue, SiegeIntent.Capture, out _);
        AttackAndArrive(neutralId, u);
        Assert.IsTrue(RunUntil(() => sim.Entities[neutralId].Side == Side.Blue), "超时未完成占领");
        var b = sim.Entities[neutralId];
        Assert.AreEqual(Side.Blue, b.Side, "建筑应归属蓝方");
        Assert.Greater(b.Hp, 0, "中立建筑占领后应补上配置血量");
        Assert.AreEqual(sim.BuildingConfigOf(BuildingId.ResourcePoint).MaxHp, b.MaxHp, "MaxHp 应为配置值");
        // 资源点驻军为 0:幸存士兵不驻入
        Assert.AreEqual(0, b.GarrisonFormal.Count, "资源点驻军恢复 0,不应有士兵驻入");
        var su = sim.GetEntity(u.Id);
        Assert.IsNotNull(su, "进攻士兵应幸存(资源点无人,无交换损耗)");
        Assert.AreEqual(0, su.GarrisonBuildingId, "士兵不应绑定驻军建筑");
        Assert.AreEqual(g.Id, su.GeneralId, "士兵仍属原兵团");
        Assert.AreNotEqual(EntityState.Dead, su.State, "士兵不应阵亡");
    }

    [Test]
    public void T2_意图后写覆盖()
    {
        Assert.IsTrue(SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _), "首次设置 Capture 应成功");
        Assert.IsTrue(SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Destroy, out _), "覆盖为 Destroy 应成功");
        Assert.AreEqual(SiegeIntent.Destroy, SiegeSystem.GetIntent(sim, redTowerId), "意图应为后写的 Destroy");
        Assert.AreEqual(Side.Blue, sim.Entities[redTowerId].IntentIssuer, "意图发起者应为 Blue");
    }

    [Test]
    public void T3_封城锁意图()
    {
        Garrison(redTowerId, 3, Side.Red);
        SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _);
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var atk = SoldiersOf(g).Take(2).ToList();
        AttackAndArrive(redTowerId, atk.ToArray());
        Assert.IsTrue(RunUntil(() => sim.Entities[redTowerId].Sealed, 50), "超时未进入封城");
        Assert.IsTrue(sim.Entities[redTowerId].Sealed, "攻方入城后建筑应封城");
        Assert.IsFalse(SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Destroy, out var err), "封城期间切换意图应被拒绝");
        Assert.IsNotNull(err, "应返回错误信息");
        StringAssert.Contains("封城", err, "错误信息应包含『封城』");
    }

    [Test]
    public void T4_交换1比1_守军与攻方士兵同归于尽()
    {
        var defs = Garrison(redTowerId, 1, Side.Red);
        SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _);
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var a = SoldiersOf(g)[0];
        var b2 = SoldiersOf(g)[1];
        AttackAndArrive(redTowerId, a, b2);
        Assert.IsTrue(RunUntil(() => sim.Entities[redTowerId].Side == Side.Blue), "超时未完成占领");
        var b = sim.Entities[redTowerId];
        Assert.AreEqual(Side.Blue, b.Side, "建筑应归属蓝方");
        Assert.IsFalse(sim.Entities.ContainsKey(defs[0].Id), "守军士兵应阵亡(1:1 互换)");
        Assert.IsFalse(sim.Entities.ContainsKey(a.Id), "攻方士兵 a 应阵亡(1:1 互换)");
        // 攻方 b2 幸存并自动驻军
        Assert.IsTrue(sim.Entities.ContainsKey(b2.Id) && b2.GarrisonBuildingId == redTowerId, "幸存攻方士兵应自动驻军");
        Assert.IsFalse(b.Sealed, "占领后应解除封城");
        Assert.IsNotNull(sim.GetEntity(g.Id), "攻方主将存活(永不因交换阵亡)");
        Assert.AreEqual(1, b.GarrisonFormal.Count, "幸存 1 人驻军");
    }

    [Test]
    public void T5_攻方多余_幸存者自动正式驻军()
    {
        Garrison(redTowerId, 2, Side.Red);
        SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _);
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var us = SoldiersOf(g).Take(4).ToList();
        AttackAndArrive(redTowerId, us.ToArray());
        Assert.IsTrue(RunUntil(() => sim.Entities[redTowerId].Side == Side.Blue), "超时未完成占领");
        var b = sim.Entities[redTowerId];
        Assert.AreEqual(Side.Blue, b.Side, "建筑应归属蓝方");
        Assert.AreEqual(2, b.GarrisonFormal.Count, "攻 4 守 2 应幸存 2 人自动驻军");
        foreach (var id in b.GarrisonFormal)
            Assert.AreEqual(redTowerId, sim.Entities[id].GarrisonBuildingId, "驻军士兵应记录所在建筑");
        Assert.IsNotNull(sim.GetEntity(g.Id), "攻方主将存活");
    }

    [Test]
    public void T6_交换1比1_守军清零且主将存活()
    {
        var defs = Garrison(redTowerId, 1, Side.Red);
        SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _);
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhenYue, new GridPos(5, 105));
        var atk = SoldiersOf(g).Take(2).ToList(); // 镇岳兵团:主将 + 士兵
        AttackAndArrive(redTowerId, atk.ToArray());
        Assert.IsTrue(RunUntil(() => sim.Entities[redTowerId].Side == Side.Blue), "超时未完成占领");
        var b = sim.Entities[redTowerId];
        Assert.AreEqual(Side.Blue, b.Side, "建筑应归属蓝方");
        Assert.IsFalse(sim.Entities.ContainsKey(defs[0].Id), "守军士兵应被清零");
        Assert.AreEqual(1, b.GarrisonFormal.Count, "攻 2 守 1:1:1 互换后 1 名攻方士兵幸存驻军");
        Assert.AreEqual(redTowerId, sim.Entities[b.GarrisonFormal[0]].GarrisonBuildingId, "幸存者登记驻军");
        var gen = sim.GetEntity(g.Id);
        Assert.IsNotNull(gen, "攻方主将应存活");
        Assert.Greater(gen.Hp, 0, "主将永不因交换阵亡");
        // 另 1 名攻方士兵阵亡
        int aliveAtk = 0;
        foreach (var a in atk) if (sim.Entities.ContainsKey(a.Id)) aliveAtk++;
        Assert.AreEqual(1, aliveAtk, "攻方士兵剩 1");
    }

    [Test]
    public void T7_攻方被肃清_解封且意图保留()
    {
        var defs = Garrison(redTowerId, 3, Side.Red);
        SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _);
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var a = SoldiersOf(g).First();
        AttackAndArrive(redTowerId, a);
        Assert.IsTrue(RunUntil(() => !sim.Entities.ContainsKey(a.Id)), "超时攻方未被肃清");
        var b = sim.Entities[redTowerId];
        Assert.AreEqual(Side.Red, b.Side, "建筑应仍属红方");
        Assert.IsFalse(b.Sealed, "攻方肃清后应解除封城");
        Assert.AreEqual(SiegeIntent.Capture, b.Intent, "意图应保留为 Capture");
        Assert.AreEqual(2, b.GarrisonFormal.Count, "剩余守军应留城(3-1=2)");
        Assert.IsFalse(sim.Entities.ContainsKey(a.Id), "攻方士兵阵亡");
        Assert.IsNotNull(sim.GetEntity(g.Id), "攻方主将虽败不死于交换(城外待命)");
    }

    [Test]
    public void T8_封城期间增援进城_最终守军清零()
    {
        var defs = Garrison(redTowerId, 3, Side.Red);
        SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _);
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var ss = SoldiersOf(g).ToList();
        AttackAndArrive(redTowerId, ss[0]);
        Assert.IsTrue(RunUntil(() => sim.Entities[redTowerId].Sealed, 50), "超时未封城");
        AttackAndArrive(redTowerId, ss[1], ss[2]); // 封城期间增援
        Assert.IsTrue(RunUntil(() => sim.Entities[redTowerId].Side == Side.Blue), "超时未完成占领");
        var b = sim.Entities[redTowerId];
        Assert.AreEqual(Side.Blue, b.Side, "最终建筑应归属蓝方");
        foreach (var d in defs)
            Assert.IsFalse(sim.Entities.ContainsKey(d.Id), "守军士兵应清零");
        Assert.IsFalse(b.Sealed, "占领后应解封");
    }

    [Test]
    public void T9_中立建筑不可摧毁()
    {
        Assert.IsFalse(SiegeSystem.SetIntent(sim, neutralId, Side.Blue, SiegeIntent.Destroy, out var err), "中立建筑设 Destroy 应被拒绝");
        Assert.IsNotNull(err, "应返回错误信息");
        StringAssert.Contains("中立", err, "错误信息应包含『中立』");
    }

    [Test]
    public void T10_大本营不可占领()
    {
        var baseId = sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 136)).Id;
        Assert.IsFalse(SiegeSystem.SetIntent(sim, baseId, Side.Blue, SiegeIntent.Capture, out var err), "敌方大本营设 Capture 应被拒绝");
        Assert.IsNotNull(err, "应返回错误信息");
        StringAssert.Contains("大本营", err, "错误信息应包含『大本营』");
    }
}
