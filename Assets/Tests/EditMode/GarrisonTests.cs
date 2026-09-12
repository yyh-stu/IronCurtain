using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

public class GarrisonTests
{
    private static GameConfig _cfg;
    private SimCore sim;
    private Entity blueBarracks, blueTower, redTower, blueHQ;
    private readonly GridPos barracksPos = new GridPos(15, 116);
    private readonly GridPos blueTowerPos = new GridPos(15, 124);
    private readonly GridPos redTowerPos = new GridPos(15, 132);

    [SetUp]
    public void SetUp()
    {
        if (_cfg == null)
            _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(1));
        sim.Game = _cfg;
        blueBarracks = sim.SpawnBuilding(Side.Blue, BuildingId.Barracks, barracksPos);
        blueTower = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, blueTowerPos);
        redTower = sim.SpawnBuilding(Side.Red, BuildingId.Tower, redTowerPos);
        blueHQ = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 108));
    }

    private Entity SpawnHero(Side side, HeroId heroId, GridPos pos)
    {
        var c = sim.Game.Heroes[(int)heroId];
        var u = sim.SpawnEntity(side, EntityKind.Unit, pos);
        u.Hero = heroId; u.MaxHp = c.MaxHp; u.Hp = c.MaxHp; u.Attack = c.Attack;
        u.AttackIntervalSec = (float)c.AttackIntervalSec; u.Range = c.Range; u.MoveSpeed = (float)c.MoveSpeed;
        if (c.Shield > 0) { u.Shield = c.Shield; u.ShieldMax = c.Shield; }
        return u;
    }

    private Entity Gen(Side side, HeroId hero, GridPos pos)
        => RegimentSystem.SpawnGeneral(sim, side, hero, pos);

    private List<Entity> SoldiersOf(Entity g)
        => sim.Entities.Values
               .Where(x => x.Kind == EntityKind.Unit && x.IsSoldier && x.GeneralId == g.Id)
               .OrderBy(x => x.FormationSlot).ToList();

    private void TickN(int n) { for (int i = 0; i < n; i++) GarrisonSystem.Tick(sim, 0.1f); }

    [Test]
    public void 正式入城_数量与登记释放()
    {
        var g = Gen(Side.Blue, HeroId.PanShi, new GridPos(5, 140));
        var ss = SoldiersOf(g).Take(3).ToList();
        string err;
        bool ok = GarrisonSystem.EnterFormal(sim, ss.Select(x => x.Id).ToList(), blueBarracks.Id, out err);
        Assert.IsTrue(ok, "3 名士兵正式入城应成功");
        Assert.AreEqual(3, blueBarracks.GarrisonFormal.Count, "正式驻军应为 3");
        foreach (var s in ss)
        {
            Assert.AreEqual(blueBarracks.Id, s.GarrisonBuildingId, "士兵应绑定兵营 id");
            Assert.AreEqual(0, s.TempGarrisonId, "正式入城士兵不应有临时登记(原格释放语义)");
        }
    }

    [Test]
    public void 正式驻军不限量_第31只照常入城()
    {
        var g1 = Gen(Side.Blue, HeroId.PanShi, new GridPos(5, 140));
        var g2 = Gen(Side.Blue, HeroId.PanShi, new GridPos(5, 165));
        var g3 = Gen(Side.Blue, HeroId.DuanFeng, new GridPos(5, 190)); // 2026-09-10 12 兵/团:三团 36 ≥ 31
        var all = SoldiersOf(g1).Concat(SoldiersOf(g2)).Concat(SoldiersOf(g3)).ToList();
        Assert.GreaterOrEqual(all.Count, 31, "三兵团应提供 31 名士兵");
        string err;
        bool ok30 = GarrisonSystem.EnterFormal(sim, all.Take(30).Select(x => x.Id).ToList(), blueBarracks.Id, out err);
        Assert.IsTrue(ok30, "前 30 名应成功入城");
        Assert.AreEqual(30, blueBarracks.GarrisonFormal.Count, "前 30 名入城");
        var s31 = all[30];
        bool ok31 = GarrisonSystem.EnterFormal(sim, new List<int> { s31.Id }, blueBarracks.Id, out err);
        Assert.IsTrue(ok31, "第 31 名入城成功");
        Assert.AreEqual(31, blueBarracks.GarrisonFormal.Count, "2026-09-11 制作人:单城不限量驻守,第 31 名也登记");
        Assert.AreNotEqual(0, s31.GarrisonBuildingId, "第 31 名已入城(不限量)");
    }

    [Test]
    public void 临时驻军不限量_第31只照常登记()
    {
        var g1 = Gen(Side.Blue, HeroId.PanShi, new GridPos(5, 140));
        var g2 = Gen(Side.Blue, HeroId.DuanFeng, new GridPos(10, 140));
        var g3 = Gen(Side.Blue, HeroId.ZhuiRi, new GridPos(15, 140)); // 2026-09-10 12 兵/团:三团 36 ≥ 31
        var all = SoldiersOf(g1).Concat(SoldiersOf(g2)).Concat(SoldiersOf(g3)).ToList();
        string err;
        Assert.IsTrue(GarrisonSystem.EnterTemp(sim, all.Take(30).Select(x => x.Id).ToList(), blueBarracks.Id, out err), "30 名临时登记应成功");
        Assert.AreEqual(30, blueBarracks.GarrisonTemp.Count, "前 30 名临时登记");
        var extra = all[30];
        Assert.IsTrue(GarrisonSystem.EnterTemp(sim, new List<int> { extra.Id }, blueBarracks.Id, out err), "第 31 名登记成功");
        Assert.AreEqual(31, blueBarracks.GarrisonTemp.Count, "2026-09-11 制作人:临时驻军同样不限量");
        Assert.AreNotEqual(0, extra.TempGarrisonId, "第 31 名已登记临时驻军");
    }

    [Test]
    public void 临时驻军自动转正式_无计时()
    {
        var g = Gen(Side.Blue, HeroId.PanShi, new GridPos(5, 122)); // 主将距兵营 <15m,城后等候有效
        var ss = SoldiersOf(g).Take(3).ToList();
        string err;
        Assert.IsTrue(GarrisonSystem.EnterTemp(sim, ss.Select(x => x.Id).ToList(), blueBarracks.Id, out err), "临时登记应成功");
        TickN(1); // 废除 10 秒计时:正式有空缺即转
        Assert.AreEqual(3, blueBarracks.GarrisonFormal.Count, "正式空缺即自动转正");
        Assert.AreEqual(0, blueBarracks.GarrisonTemp.Count, "临时表应清空");
        foreach (var s in ss)
        {
            Assert.AreEqual(0, s.TempGarrisonId, "临时登记应清除");
            Assert.AreEqual(blueBarracks.Id, s.GarrisonBuildingId, "转正后登记正式驻军");
        }
    }

    [Test]
    public void 临时驻军随时转正_无封城概念()
    {
        // 2026-09-11 制作人:废除封城——临时驻军随时转正(不再有 Sealed 窗口)
        var g = Gen(Side.Blue, HeroId.PanShi, new GridPos(5, 122));
        var ss = SoldiersOf(g).Take(2).ToList();
        string err;
        GarrisonSystem.EnterTemp(sim, ss.Select(x => x.Id).ToList(), blueBarracks.Id, out err);
        TickN(5);
        Assert.AreEqual(2, blueBarracks.GarrisonFormal.Count, "无封城,直接转正");
        Assert.AreEqual(0, blueBarracks.GarrisonTemp.Count, "临时表清空");
    }

    [Test]
    public void 主将入城被拒_仅士兵可驻军()
    {
        var g = Gen(Side.Blue, HeroId.PanShi, new GridPos(5, 122));
        string err;
        bool okF = GarrisonSystem.EnterFormal(sim, new List<int> { g.Id }, blueBarracks.Id, out err);
        Assert.IsTrue(okF, "调用不整体失败");
        StringAssert.Contains("仅士兵可驻军", err, "正式入城应拒绝主将并提示");
        Assert.AreEqual(0, blueBarracks.GarrisonFormal.Count, "主将不入正式列表");
        Assert.AreEqual(0, g.GarrisonBuildingId, "主将不绑定建筑");
        bool okT = GarrisonSystem.EnterTemp(sim, new List<int> { g.Id }, blueBarracks.Id, out err);
        Assert.IsTrue(okT, "调用不整体失败");
        StringAssert.Contains("仅士兵可驻军", err, "临时登记应拒绝主将并提示");
        Assert.AreEqual(0, blueBarracks.GarrisonTemp.Count, "主将不入临时列表");
        Assert.AreEqual(0, g.TempGarrisonId, "主将无临时登记");
    }

    [Test]
    public void Exit撤出一名坠日士兵()
    {
        var g1 = Gen(Side.Blue, HeroId.PanShi, new GridPos(5, 140));
        var g2 = Gen(Side.Blue, HeroId.ZhuiRi, new GridPos(5, 165));
        var ps = SoldiersOf(g1).Take(2).ToList();
        var zr = SoldiersOf(g2).First();
        string err;
        var ids = ps.Select(x => x.Id).Concat(new List<int> { zr.Id }).ToList();
        GarrisonSystem.EnterFormal(sim, ids, blueBarracks.Id, out err);
        bool ok = GarrisonSystem.Exit(sim, blueBarracks.Id, HeroId.ZhuiRi, out err);
        Assert.IsTrue(ok, "撤出坠日士兵应成功");
        Assert.AreEqual(0, zr.GarrisonBuildingId, "坠日士兵应离城");
        Assert.AreEqual(0, zr.TempGarrisonId, "临时登记应清除");
        Assert.AreEqual(EntityState.Idle, zr.State, "出城后回团 Idle 待主循环接战");
        Assert.AreEqual(2, blueBarracks.GarrisonFormal.Count, "磐石士兵×2 仍在");
        Assert.IsTrue(blueBarracks.GarrisonFormal.Contains(ps[0].Id) && blueBarracks.GarrisonFormal.Contains(ps[1].Id), "磐石士兵仍在正式列表");
    }

    [Test]
    public void Exit城门无空位()
    {
        // 非士兵旧数据路径:主将单位被直接登记进正式列表(M8.3a 兼容口径),城门区全占 → Exit 失败
        var c = SpawnHero(Side.Blue, HeroId.ZhuiRi, new GridPos(16, 125));
        blueBarracks.GarrisonFormal.Add(c.Id);
        // 城门区(M8.1b 矩形敌侧缘+0.6:兵营 HalfW=2.5 → 敌缘列 ≈119,行 13..17,外扩 4 列):行 12..18 × 列 118..123 全部占满 → 无空位
        for (int row = 12; row <= 18; row++)
            for (int col = 118; col <= 123; col++)
                SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(row, col));
        string err;
        bool ok = GarrisonSystem.Exit(sim, blueBarracks.Id, HeroId.ZhuiRi, out err);
        Assert.IsFalse(ok, "城门区全占应失败");
        StringAssert.Contains("无空位", err, "错误信息应包含『无空位』");
    }

    [Test]
    public void 城池易主_临时士兵清登记不溃逃()
    {
        var bg = Gen(Side.Blue, HeroId.PanShi, new GridPos(5, 122)); // 主将距蓝塔 ~10m <15m,城后等候有效
        var ss = SoldiersOf(bg).ToList();
        var f = ss[0]; var t1 = ss[1]; var t2 = ss[2];
        string err;
        Assert.IsTrue(GarrisonSystem.EnterFormal(sim, new List<int> { f.Id }, blueTower.Id, out err), "正式入城");
        Assert.IsTrue(GarrisonSystem.EnterTemp(sim, new List<int> { t1.Id, t2.Id }, blueTower.Id, out err), "临时登记");

        var rg = Gen(Side.Red, HeroId.PanShi, new GridPos(5, 60));
        var atk = SoldiersOf(rg).Take(2).ToList();
        SiegeSystem.SetIntent(sim, blueTower.Id, Side.Red, SiegeIntent.Capture, out err);
        Assert.IsTrue(SiegeSystem.IssueOrder(sim, atk.Select(x => x.Id).ToList(), blueTower.Id, out err), "下订单失败:" + err);
        foreach (var a in atk) a.HasMoveTarget = false; // 兵团士兵移动由 RegimentSystem 接管,视作已抵城

        bool captured = false;
        for (int i = 0; i < 3000 && !captured; i++) { sim.Step(); captured = blueTower.Side == Side.Red; }
        Assert.IsTrue(captured, "城池应易主");
        for (int i = 0; i < 200; i++) sim.Step(); // 易主后推进,士兵回团收敛

        Assert.AreEqual(0, blueTower.GarrisonTemp.Count, "旧主临时驻军应清登记");
        Assert.IsFalse(blueTower.GarrisonTemp.Contains(t1.Id) || blueTower.GarrisonTemp.Contains(t2.Id), "临时士兵不在列表");
        foreach (var t in new[] { t1, t2 })
        {
            Assert.AreEqual(0, t.GarrisonBuildingId, "登记应清除");
            Assert.AreEqual(0, t.TempGarrisonId, "临时登记应清除");
            Assert.AreNotEqual(EntityState.Routing, t.State, "士兵永不溃逃");
            Assert.IsFalse(t.HasMoveTarget, "不设溃逃 MoveTarget");
            var slot = bg.PosF + RegimentSystem.SlotOffset(bg, t.FormationSlot);
            Assert.Less(SimVec2.Distance(t.PosF, slot), 1.5f, "士兵应回团跟随主将(阵型槽位)");
        }
        Assert.IsNotNull(sim.GetEntity(bg.Id), "主将仍在场");
    }

    [Test]
    public void 士兵无溃逃状态()
    {
        var g = Gen(Side.Blue, HeroId.PanShi, new GridPos(5, 122));
        var s = SoldiersOf(g).First();
        string err;
        GarrisonSystem.EnterTemp(sim, new List<int> { s.Id }, blueTower.Id, out err);
        GarrisonSystem.OnCityCaptured(sim, blueTower); // 易主路径
        Assert.AreEqual(EntityState.Idle, s.State, "士兵不会获得溃逃状态(Idle)");
        Assert.AreNotEqual(EntityState.Routing, s.State, "士兵永不 Routing");
        Assert.AreEqual(0, s.TempGarrisonId, "登记清除");
        Assert.AreEqual(0, s.GarrisonBuildingId, "登记清除");
        Assert.AreEqual(0, blueTower.GarrisonTemp.Count, "临时列表清空");
    }

    [Test]
    public void 城池主动攻击_射手驻将守城5伤半秒一发_特效归因主将()
    {
        var g = Gen(Side.Red, HeroId.ZhuiRi, new GridPos(5, 128));
        // 城内驻将=占领进驻语义(交换力10/12):直接登记(EnterFormal 仅士兵,另一条路)
        redTower.GarrisonFormal.Add(g.Id);
        g.GarrisonBuildingId = redTower.Id;
        var target = SpawnHero(Side.Blue, HeroId.ZhenYue, new GridPos(15, 129)); // 距塔 3m,塔视野 24 内
        float before = target.Hp;
        TickN(100);   // 10s ≈ 20 发
        float dealt = before - target.Hp;
        Assert.GreaterOrEqual(dealt, 15 * GarrisonSystem.CityAttackDamage, "10s 至少 15 发(0.5s 间隔)");
        Assert.LessOrEqual(dealt, 25 * GarrisonSystem.CityAttackDamage, "10s 至多 25 发(0.5s 间隔)");
    }

    [Test]
    public void 城池主动攻击_射手士兵驻军不开火_无射手驻将也不开火()
    {
        // 只有射手士兵(非主将)驻城 → 不触发
        var g1 = Gen(Side.Red, HeroId.ZhuiRi, new GridPos(5, 128));
        var archerSoldier = SoldiersOf(g1).First();
        string err;
        GarrisonSystem.EnterTemp(sim, new List<int> { archerSoldier.Id }, redTower.Id, out err);
        // 磐石士兵驻城 → 不触发
        var g2 = Gen(Side.Red, HeroId.PanShi, new GridPos(6, 128));
        var melee = SoldiersOf(g2).First();
        GarrisonSystem.EnterTemp(sim, new List<int> { melee.Id }, redTower.Id, out err);
        var target = SpawnHero(Side.Blue, HeroId.ZhenYue, new GridPos(15, 129));
        float before = target.Hp;
        TickN(50);
        Assert.AreEqual(before, target.Hp, "无射手驻将的城不应主动开火");
    }

    [Test]
    public void 近战出击断峰士兵()
    {
        var g = Gen(Side.Red, HeroId.DuanFeng, new GridPos(5, 128));
        var melee = SoldiersOf(g).First();
        string err;
        Assert.IsTrue(GarrisonSystem.EnterFormal(sim, new List<int> { melee.Id }, redTower.Id, out err), "正式入城");
        SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(15, 130)); // 距塔中心 2 ≤ 2.5,触发近战出击
        TickN(1);
        Assert.AreEqual(0, melee.GarrisonBuildingId, "断峰士兵应出城(GarrisonBuildingId==0)");
        Assert.AreEqual(0, melee.TempGarrisonId, "无临时登记");
        Assert.AreEqual(EntityState.Idle, melee.State, "出城后应 Idle 待主循环接战");
    }

    [Test]
    public void 建筑脱战回复()
    {
        blueTower.Hp = 20f;
        blueTower.SecondsSinceCombat = 25f;
        TickN(50);
        Assert.AreEqual(40f, blueTower.Hp, 0.01f, "50 tick 后应回血至 40(20+4×5)");
    }

    [Test]
    public void 大本营不回复()
    {
        blueHQ.Hp = 50f;
        blueHQ.SecondsSinceCombat = 25f;
        TickN(100);
        Assert.AreEqual(50f, blueHQ.Hp, 0.01f, "大本营不回复,应仍为 50");
    }
}
