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
        => SoldiersOf(sim, g);

    static List<Entity> SoldiersOf(SimCore owner, Entity g)
        => owner.Entities.Values
               .Where(x => x.Kind == EntityKind.Unit && x.IsSoldier && x.GeneralId == g.Id)
               .OrderBy(x => x.FormationSlot).ToList();

    SimCore NewOrderSim(ulong seed, out Entity target, out Entity general)
    {
        var owner = new SimCore(new SimConfig(), new SeededRng(seed)) { Game = _cfg };
        target = owner.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 130));
        general = RegimentSystem.SpawnGeneral(owner, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        return owner;
    }

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
    public void T3_意图随时可切_封城已废除()
    {
        // 2026-09-11 制作人:废除封城——交换进行中也可切换意图
        Garrison(redTowerId, 3, Side.Red);
        SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _);
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var atk = SoldiersOf(g).Take(2).ToList();
        AttackAndArrive(redTowerId, atk.ToArray());
        RunUntil(() => false, 5); // 推进若干 tick(交换可能进行中)
        Assert.IsTrue(SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Destroy, out var err), "交换期间切换意图应放行(2026-09-11 废封城)");
    }

    [Test]
    public void T4_交换1比1_守军与攻方士兵同归于尽_幸存者随军出城()
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
        // 2026-09-14 制作人口径:「每个军团要么全留,要么全出,不要主将出去兵还在城里」——
        // 本用例主将未参战(只在城外),所以这个团**整团出城**,幸存士兵不驻入。
        Assert.IsTrue(sim.Entities.ContainsKey(b2.Id) && b2.GarrisonBuildingId == 0, "主将未参战 → 幸存士兵随军出城");
        Assert.IsNotNull(sim.GetEntity(g.Id), "攻方主将存活(永不因交换阵亡)");
        Assert.AreEqual(0, b.GarrisonFormal.Count, "半团不驻入:主将在城外时一个兵都不留城");
    }

    [Test]
    public void T5_攻方多余_幸存者整团出城不驻入()
    {
        Garrison(redTowerId, 2, Side.Red);
        SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _);
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var us = SoldiersOf(g).Take(4).ToList();
        AttackAndArrive(redTowerId, us.ToArray());
        Assert.IsTrue(RunUntil(() => sim.Entities[redTowerId].Side == Side.Blue), "超时未完成占领");
        var b = sim.Entities[redTowerId];
        Assert.AreEqual(Side.Blue, b.Side, "建筑应归属蓝方");
        // 2026-09-14 制作人:「每个军团要么全留,要么全出」——主将没参战,该团整团出城
        Assert.AreEqual(0, b.GarrisonFormal.Count, "攻 4 守 2:主将未参战 → 幸存者整团出城,不驻入");
        int survivors = us.Count(u => sim.Entities.ContainsKey(u.Id));
        Assert.Greater(survivors, 0, "应有幸存士兵(2 名:攻 4 守 2,1:1 互换)");
        foreach (var u in us)
            if (sim.Entities.ContainsKey(u.Id))
                Assert.AreEqual(0, sim.GetEntity(u.Id).GarrisonBuildingId, "幸存士兵应留在城外");
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
        // 2026-09-14 制作人口径:「每个军团要么全留,要么全出」——主将没参战 → 整团出城
        Assert.AreEqual(0, b.GarrisonFormal.Count, "攻 2 守 1:主将未参战 → 幸存者随军出城,不驻入");
        var survivor = sim.GetEntity(atk[0].Id) ?? sim.GetEntity(atk[1].Id);
        Assert.IsNotNull(survivor, "应有 1 名攻方士兵幸存");
        Assert.AreEqual(0, survivor.GarrisonBuildingId, "幸存士兵应留在城外");
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
        Assert.AreEqual(SiegeIntent.Capture, b.Intent, "意图应保留为 Capture");
        Assert.AreEqual(2, b.GarrisonFormal.Count, "剩余守军应留城(3-1=2)");
        Assert.IsFalse(sim.Entities.ContainsKey(a.Id), "攻方士兵阵亡");
        Assert.IsNotNull(sim.GetEntity(g.Id), "攻方主将虽败不死于交换");
        Assert.AreEqual(EntityState.Idle, sim.GetEntity(g.Id).State, "攻城失败后主将应收兵出城(不留在 Exchanging)");
    }

    [Test]
    public void T8_封城期间增援进城_最终守军清零()
    {
        var defs = Garrison(redTowerId, 3, Side.Red);
        SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _);
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var ss = SoldiersOf(g).ToList();
        AttackAndArrive(redTowerId, ss[0]);
        AttackAndArrive(redTowerId, ss[1], ss[2]); // 交换期间增援照常进城(2026-09-11 废封城)
        Assert.IsTrue(RunUntil(() => sim.Entities[redTowerId].Side == Side.Blue), "超时未完成占领");
        var b = sim.Entities[redTowerId];
        Assert.AreEqual(Side.Blue, b.Side, "最终建筑应归属蓝方");
        foreach (var d in defs)
            Assert.IsFalse(sim.Entities.ContainsKey(d.Id), "守军士兵应清零");
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

    [Test]
    public void T11_兵团命令只展开士兵_主将带兵团行军到位后放兵入场()
    {
        // 2026-09-14 修正用例场景:原来这口箭塔**没有守军**,主将一"到场"就满足
        // 「无人建筑直接占领」→ 城池易主,攻方整队被占领复位取消(见 Capture 里的全量复位),
        // 于是"到位放兵"这一步在用例里从未真正被检到。补 3 名守军,回到本用例的本意:
        // 攻城中,令下只展开士兵、主将获城外集结点,到场后士兵入场。
        Garrison(redTowerId, 3, Side.Red);
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var soldiers = SoldiersOf(g);
        var generalPos = g.PosF;
        var firstPos = soldiers[0].PosF;

        var result = SiegeSystem.TryIssueRegimentOrder(
            sim, new[] { g.Id }, redTowerId, Side.Blue, SiegeIntent.Capture);

        Assert.IsTrue(result.Success, result.Error.ToString());
        Assert.AreEqual(RegimentSystem.SoldiersPerGeneral, result.SoldierIds.Count);
        // 2026-09-09 制作人二次裁决:兵随主将——令下主将(标记+行军),士兵不受令随行
        Assert.IsTrue(g.HasMoveTarget, "主将应受令行军");
        Assert.AreEqual(redTowerId, g.TargetBuildingId, "主将带兵团攻城标记(不参与交换)");
        Assert.IsTrue(soldiers.All(s => !s.HasMoveTarget && s.TargetBuildingId == 0), "下令时士兵不受令");

        sim.Step();
        Assert.AreNotEqual(generalPos, g.PosF, "主将应向城外集结点行军");
        // 模拟主将抵达 → 放兵
        g.HasMoveTarget = false;
        var tb = sim.GetEntity(redTowerId);
        g.PosF = new SimVec2(tb.Pos.Col + 3.5f, tb.Pos.Row + 3.5f);
        sim.Step();
        Assert.IsTrue(soldiers.All(s => s.HasMoveTarget && s.TargetBuildingId == redTowerId), "到位后士兵入场");
        Assert.AreNotEqual(firstPos, soldiers[0].PosF, "士兵须向攻城前排移动");
    }

    [Test]
    public void T12_混合重复选择_确定性去重并过滤死亡驻军跨阵营()
    {
        var validGeneral = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var deadGeneral = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.DuanFeng, new GridPos(25, 105));
        var enemyGeneral = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(25, 150));
        var valid = SoldiersOf(validGeneral);
        valid[0].Hp = 0;
        valid[0].State = EntityState.Dead;
        valid[1].GarrisonBuildingId = blueTowerId;
        sim.Entities[blueTowerId].GarrisonFormal.Add(valid[1].Id);
        deadGeneral.Hp = 0;
        deadGeneral.State = EntityState.Dead;

        var result = SiegeSystem.TryIssueRegimentOrder(sim,
            new[] { validGeneral.Id, validGeneral.Id, valid[2].Id, valid[0].Id, valid[1].Id,
                    deadGeneral.Id, enemyGeneral.Id, 999999 },
            redTowerId, Side.Blue, SiegeIntent.Destroy);

        Assert.IsTrue(result.Success, result.Error.ToString());
        Assert.AreEqual(10, result.SoldierIds.Count, "12 人中死亡与驻军各过滤 1，重复不得重复下令");
        Assert.AreEqual(10, result.SoldierIds.Distinct().Count());
        Assert.IsTrue(result.SoldierIds.All(id => sim.Entities[id].Side == Side.Blue));
        Assert.IsFalse(deadGeneral.HasMoveTarget);
        Assert.IsFalse(enemyGeneral.HasMoveTarget);
        Assert.IsTrue(SoldiersOf(deadGeneral).All(s => !s.HasMoveTarget), "死亡主将不得展开其遗兵");
        Assert.IsTrue(SoldiersOf(enemyGeneral).All(s => !s.HasMoveTarget), "敌方兵团不得被下令");
    }

    [Test]
    public void T13_跨阵营选择_返回NoEligible且零副作用()
    {
        var enemy = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(25, 150));
        var result = SiegeSystem.TryIssueRegimentOrder(
            sim, new[] { enemy.Id }, redTowerId, Side.Blue, SiegeIntent.Destroy);

        Assert.AreEqual(SiegeOrderError.NoEligibleSoldiers, result.Error);
        Assert.AreEqual(SiegeIntent.None, sim.Entities[redTowerId].Intent);
        Assert.IsFalse(enemy.HasMoveTarget);
        Assert.IsTrue(SoldiersOf(enemy).All(s => !s.HasMoveTarget && s.TargetBuildingId == 0));
    }

    [Test]
    public void T14_零兵武将占空点_孤将可单独占领()
    {
        var lone = SpawnUnit(Side.Blue, new GridPos(5, 105), HeroId.PanShi);
        var before = lone.PosF;
        var target = sim.Entities[neutralId];

        var result = SiegeSystem.TryIssueRegimentOrder(
            sim, new[] { lone.Id }, neutralId, Side.Blue, SiegeIntent.Capture);

        // 2026-09-14 制作人:「主将和他的兵团就是一个单位」→ 零兵孤将同样可单独受令占点
        // (旧用例断言"必须被拒且零副作用",与这条口径冲突,已按新口径改写)。
        Assert.IsTrue(result.Success, "孤将应能受令:" + result.Error);
        Assert.AreEqual(0, result.SoldierIds.Count, "孤将没有士兵可展开");
        Assert.AreEqual(SiegeIntent.Capture, target.Intent, "应写下占领意图");
        Assert.IsTrue(lone.HasMoveTarget, "孤将应获行军令");
        Assert.AreEqual(neutralId, lone.TargetBuildingId, "孤将应带攻城标记");
        sim.Step();
        Assert.AreNotEqual(before, lone.PosF, "孤将应开始向集结点移动");
    }

    [Test]
    public void T15_任一士兵无合法落点_整批命令零执行()
    {
        var edgeTarget = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 0));
        var trapped = SpawnUnit(Side.Blue, new GridPos(10, 0)); trapped.IsSoldier = true;
        var movable = SpawnUnit(Side.Blue, new GridPos(15, 10)); movable.IsSoldier = true;

        var result = SiegeSystem.TryIssueRegimentOrder(sim,
            new[] { trapped.Id, movable.Id }, edgeTarget.Id, Side.Blue, SiegeIntent.Destroy);

        Assert.AreEqual(SiegeOrderError.NoValidPlacement, result.Error);
        Assert.AreEqual(SiegeIntent.None, edgeTarget.Intent);
        Assert.IsFalse(trapped.HasMoveTarget);
        Assert.IsFalse(movable.HasMoveTarget, "后一个单位即使有落点也不得部分执行");
        Assert.AreEqual(0, trapped.TargetBuildingId);
        Assert.AreEqual(0, movable.TargetBuildingId);
    }

    [Test]
    public void T16_两局相同输入_展开顺序与命令结果一致()
    {
        var first = NewOrderSim(77, out var firstTarget, out var firstGeneral);
        var second = NewOrderSim(77, out var secondTarget, out var secondGeneral);

        var a = SiegeSystem.TryIssueRegimentOrder(first, new[] { firstGeneral.Id, firstGeneral.Id },
            firstTarget.Id, Side.Blue, SiegeIntent.Capture);
        var b = SiegeSystem.TryIssueRegimentOrder(second, new[] { secondGeneral.Id, secondGeneral.Id },
            secondTarget.Id, Side.Blue, SiegeIntent.Capture);

        Assert.IsTrue(a.Success && b.Success);
        CollectionAssert.AreEqual(a.SoldierIds, b.SoldierIds);
        foreach (int id in a.SoldierIds)
        {
            Assert.AreEqual(first.Entities[id].MoveTarget, second.Entities[id].MoveTarget);
            Assert.AreEqual(first.Entities[id].TargetBuildingId, second.Entities[id].TargetBuildingId);
        }
        // 2026-09-09 制作人二次裁决:主将令(标记+集结点)同样确定性
        Assert.IsTrue(firstGeneral.HasMoveTarget);
        Assert.IsTrue(secondGeneral.HasMoveTarget);
        Assert.AreEqual(firstGeneral.MoveTarget, secondGeneral.MoveTarget);
        Assert.AreEqual(firstGeneral.TargetBuildingId, secondGeneral.TargetBuildingId);
    }

    [Test]
    public void T17_旧入口保留士兵兼容_拒绝主将后门()
    {
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var soldier = SoldiersOf(g)[0];
        Assert.IsTrue(SiegeSystem.SetIntent(sim, redTowerId, Side.Blue, SiegeIntent.Capture, out _));

        Assert.IsFalse(SiegeSystem.IssueOrder(sim, new[] { g.Id }, redTowerId, out var generalError));
        StringAssert.Contains("士兵", generalError);
        Assert.IsFalse(g.HasMoveTarget);
        Assert.AreEqual(0, g.TargetBuildingId);

        Assert.IsTrue(SiegeSystem.IssueOrder(sim, new[] { soldier.Id }, redTowerId, out var soldierError), soldierError);
        Assert.IsTrue(soldier.HasMoveTarget);
        Assert.AreEqual(redTowerId, soldier.TargetBuildingId);
    }

    [Test]
    public void T18_己方建筑Capture与Destroy_两入口均拒绝且零副作用()
    {
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(5, 105));
        var soldiers = SoldiersOf(g);
        var own = sim.Entities[blueTowerId];
        own.IntentIssuer = Side.Red; // 非默认哨兵，验证拒绝路径不改写 issuer。

        foreach (var intent in new[] { SiegeIntent.Capture, SiegeIntent.Destroy })
        {
            Assert.IsFalse(SiegeSystem.SetIntent(sim, blueTowerId, Side.Blue, intent, out var error));
            StringAssert.Contains("己方", error);
            Assert.AreEqual(SiegeIntent.None, own.Intent);
            Assert.AreEqual(Side.Red, own.IntentIssuer);

            var result = SiegeSystem.TryIssueRegimentOrder(
                sim, new[] { g.Id }, blueTowerId, Side.Blue, intent);
            Assert.AreEqual(SiegeOrderError.IntentRejected, result.Error);
            Assert.AreEqual(SiegeIntent.None, own.Intent);
            Assert.AreEqual(Side.Red, own.IntentIssuer);
            Assert.IsFalse(g.HasMoveTarget);
            Assert.IsTrue(soldiers.All(s => !s.HasMoveTarget && s.TargetBuildingId == 0),
                "拒绝命令不得部分移动任何士兵");
        }
    }

    [Test]
    public void T19_意图阵营规则矩阵_仅允许敌塔双意图敌Base摧毁与中立占领()
    {
        var enemyTower = sim.Entities[redTowerId];
        Assert.IsTrue(SiegeSystem.SetIntent(sim, enemyTower.Id, Side.Blue, SiegeIntent.Capture, out _));
        Assert.AreEqual(SiegeIntent.Capture, enemyTower.Intent);
        Assert.IsTrue(SiegeSystem.SetIntent(sim, enemyTower.Id, Side.Blue, SiegeIntent.Destroy, out _));
        Assert.AreEqual(SiegeIntent.Destroy, enemyTower.Intent);

        var enemyBase = sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 136));
        Assert.IsTrue(SiegeSystem.SetIntent(sim, enemyBase.Id, Side.Blue, SiegeIntent.Destroy, out _));
        Assert.AreEqual(SiegeIntent.Destroy, enemyBase.Intent);
        Assert.IsFalse(SiegeSystem.SetIntent(sim, enemyBase.Id, Side.Blue, SiegeIntent.Capture, out _));
        Assert.AreEqual(SiegeIntent.Destroy, enemyBase.Intent, "拒绝 Capture 不得覆盖既有 Destroy");
        Assert.AreEqual(Side.Blue, enemyBase.IntentIssuer);

        var neutral = sim.Entities[neutralId];
        Assert.IsTrue(SiegeSystem.SetIntent(sim, neutral.Id, Side.Blue, SiegeIntent.Capture, out _));
        Assert.AreEqual(SiegeIntent.Capture, neutral.Intent);
        Assert.IsFalse(SiegeSystem.SetIntent(sim, neutral.Id, Side.Blue, SiegeIntent.Destroy, out _));
        Assert.AreEqual(SiegeIntent.Capture, neutral.Intent, "拒绝 Destroy 不得覆盖既有 Capture");
        Assert.AreEqual(Side.Blue, neutral.IntentIssuer);
    }
}
