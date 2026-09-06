using System.Collections.Generic;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

public class DestroyTests
{
    private static GameConfig _cfg;
    private SimCore sim;
    private Entity blueTower, redTower, neutralPoint, blueHQ;

    [SetUp]
    public void SetUp()
    {
        if (_cfg == null)
            _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(1));
        sim.Game = _cfg;
        blueTower = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(1, 6));
        redTower = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(1, 22));
        neutralPoint = sim.SpawnBuilding(Side.Neutral, BuildingId.ResourcePoint, new GridPos(1, 14));
        blueHQ = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(1, 0));
    }

    private Entity SpawnHero(Side side, HeroId heroId, GridPos pos)
    {
        var c = sim.Game.Heroes[(int)heroId];
        var u = sim.SpawnEntity(side, EntityKind.Unit, pos);
        u.Hero = heroId; u.MaxHp = c.MaxHp; u.Hp = c.MaxHp;
        u.Attack = c.Attack; u.AttackIntervalSec = (float)c.AttackIntervalSec;
        u.Range = c.Range; u.MoveSpeed = (float)c.MoveSpeed;
        if (c.Shield > 0) { u.Shield = c.Shield; u.ShieldMax = c.Shield; }
        return u;
    }

    private void OrderDestroy(Entity u, Entity b)
    {
        u.TargetBuildingId = b.Id;
        b.Intent = SiegeIntent.Destroy;
        b.IntentIssuer = u.Side;
        sim.SetMoveTarget(u.Id, u.Pos); // 原地,不移动(测试就近布局)
    }

    private void Step(int n) { for (int i = 0; i < n; i++) sim.Step(); }

    [Test]
    public void T1_磐石近战拆塔_每击4()
    {
        var ps = SpawnHero(Side.Red, HeroId.PanShi, new GridPos(1, 5)); // 邻接蓝塔
        OrderDestroy(ps, blueTower);
        Step(20); // 1 击(间隔 2s)
        Assert.AreEqual(50f - 4f, blueTower.Hp, 0.01f, "磐石每击 2×2=4");
    }

    [Test]
    public void T2_镇岳拆塔_每击8()
    {
        var zy = SpawnHero(Side.Red, HeroId.ZhenYue, new GridPos(1, 5));
        OrderDestroy(zy, blueTower);
        Step(20);
        Assert.AreEqual(50f - 8f, blueTower.Hp, 0.01f, "镇岳每击 4×2=8");
    }

    [Test]
    public void T3_射手远程拆塔_每击8()
    {
        var zr = SpawnHero(Side.Red, HeroId.ZhuiRi, new GridPos(1, 10)); // 距塔 4 ≤ 射程 5
        zr.Hp = 200f; zr.MaxHp = 200f; // 扛住塔的反击,验证远程拆塔本身
        OrderDestroy(zr, blueTower);
        Step(20);
        Assert.AreEqual(50f - 8f, blueTower.Hp, 0.01f, "射手每击 4×2=8,远程无需邻接");
    }

    [Test]
    public void T4_逐风冲锋首击5_后续6_取消冲锋复位()
    {
        var zf = SpawnHero(Side.Red, HeroId.ZhuFeng, new GridPos(1, 5));
        zf.IsCharging = true;
        OrderDestroy(zf, blueTower);
        Step(20); // 首击
        Assert.AreEqual(50f - 5f, blueTower.Hp, 0.01f, "冲锋首击 5");
        Assert.IsTrue(zf.ChargeHitUsed, "首击标记已置位");
        Step(20); // 次击
        Assert.AreEqual(50f - 5f - 6f, blueTower.Hp, 0.01f, "后续每击 3×2=6");
        zf.IsCharging = false;
        Step(1);
        Assert.IsFalse(zf.ChargeHitUsed, "取消冲锋后首击标记复位");
    }

    [Test]
    public void T5_优先级D26_先打单位不拆塔()
    {
        var ps = SpawnHero(Side.Red, HeroId.PanShi, new GridPos(1, 5));
        OrderDestroy(ps, blueTower);
        var df = SpawnHero(Side.Blue, HeroId.DuanFeng, new GridPos(2, 5)); // 距磐石 1(射程内)
        Step(20); // tick1:塔溅射 12 + 磐石普攻 6
        Assert.AreEqual(50f, blueTower.Hp, 0.01f, "有敌单位在射程内时先接战,不拆塔");
        Assert.AreEqual(80f - 6f, df.Hp, 0.01f, "断峰只吃磐石普攻 6(己方塔溅射不伤友军)");
    }

    [Test]
    public void T6_废墟化_守军后撤()
    {
        // 将兵制:守军=兵团士兵正式驻军;主将远离战场(10,8)(超出坠日射程 9,不干扰拆塔)
        var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(10, 8));
        var soldiers = new List<int>();
        foreach (var e in sim.Entities.Values)
            if (e.IsSoldier && e.GeneralId == g.Id && soldiers.Count < 2) soldiers.Add(e.Id);
        string err;
        Assert.IsTrue(GarrisonSystem.EnterFormal(sim, soldiers, blueTower.Id, out err), "2 名士兵正式驻军应成功");
        var zr = SpawnHero(Side.Red, HeroId.ZhuiRi, new GridPos(1, 10));
        zr.Hp = 300f; zr.MaxHp = 300f; // 扛塔反击
        OrderDestroy(zr, blueTower);
        for (int i = 0; i < 300 && !blueTower.Rubbled; i++) sim.Step();
        Assert.IsTrue(blueTower.Rubbled, "塔应化为废墟");
        Assert.AreEqual(Side.Neutral, blueTower.Side, "废墟无阵营归属");
        Assert.AreEqual(0, blueTower.GarrisonFormal.Count, "驻军清空");
        foreach (var id in soldiers)
        {
            var s = sim.GetEntity(id);
            Assert.IsNotNull(s, "守军士兵存活(士兵永不溃逃)");
            Assert.AreEqual(0, s.GarrisonBuildingId, "士兵已解除驻军登记");
            Assert.AreEqual(0, s.TempGarrisonId, "士兵无临时登记");
        }
    }

    [Test]
    public void T7_中立不可摧毁_意图被拒()
    {
        string err;
        bool ok = SiegeSystem.SetIntent(sim, neutralPoint.Id, Side.Red, SiegeIntent.Destroy, out err);
        Assert.IsFalse(ok, "中立建筑不可下达摧毁意图");
        StringAssert.Contains("中立", err, "错误信息应含『中立』");
    }

    [Test]
    public void T8_废墟意图锁()
    {
        blueTower.Rubbled = true;
        string err;
        bool ok = SiegeSystem.SetIntent(sim, blueTower.Id, Side.Red, SiegeIntent.Destroy, out err);
        Assert.IsFalse(ok, "废墟不可下达意图");
        StringAssert.Contains("重建", err, "错误信息应含『重建』");
    }

    [Test]
    public void T9_重建归重建者_EP不足报错()
    {
        blueTower.Rubbled = true;
        blueTower.Side = Side.Neutral;
        blueTower.Hp = 0f;
        string err;
        bool ok = DestroySystem.Rebuild(sim, blueTower.Id, Side.Red, out err);
        Assert.IsFalse(ok, "EP 不足应失败");
        StringAssert.Contains("EP", err, "错误信息应含『EP』");

        // 红方 1 资源点积累 900 tick → 1 EP
        sim.SpawnBuilding(Side.Red, BuildingId.ResourcePoint, new GridPos(1, 24));
        Step(905);
        Assert.AreEqual(1f, sim.EpOf(Side.Red), 0.01f, "红方应有 1 EP");
        ok = DestroySystem.Rebuild(sim, blueTower.Id, Side.Red, out err);
        Assert.IsTrue(ok, "EP 足够应重建成功");
        Assert.AreEqual(Side.Red, blueTower.Side, "重建后归重建者");
        Assert.IsFalse(blueTower.Rubbled, "废墟标记清除");
        Assert.AreEqual(blueTower.MaxHp, blueTower.Hp, 0.01f, "满血恢复");
    }

    [Test]
    public void T10_大本营判负()
    {
        var zy = SpawnHero(Side.Red, HeroId.ZhenYue, new GridPos(1, 1)); // 邻接蓝大本营(1,0)
        OrderDestroy(zy, blueHQ);
        for (int i = 0; i < 600 && sim.Winner == null; i++) sim.Step();
        Assert.AreEqual(Side.Red, sim.Winner, "大本营清零应判红方胜");
    }
}
