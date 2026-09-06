using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

public class UpgradeTests
{
    private static GameConfig _cfg;
    private SimCore sim;

    [SetUp]
    public void SetUp()
    {
        if (_cfg == null)
            _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(1));
        sim.Game = _cfg;
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

    private void GiveEp(Side side, int amount)
    {
        for (int i = 0; i < amount; i++)
            sim.SpawnBuilding(side, BuildingId.ResourcePoint, new GridPos(14 + i % 3, 128 + i / 3));
        for (int i = 0; i < 905; i++) sim.Step();
    }

    [Test]
    public void T1_购买扣费与一次性()
    {
        GiveEp(Side.Blue, 2);
        Assert.AreEqual(2f, sim.EpOf(Side.Blue), 0.01f, "应有 2 EP");
        string err;
        Assert.IsTrue(sim.BuyUpgrade(Side.Blue, HeroId.PanShi, out err), "购买应成功");
        Assert.AreEqual(0f, sim.EpOf(Side.Blue), 0.01f, "EP 应扣光");
        Assert.IsFalse(sim.BuyUpgrade(Side.Blue, HeroId.PanShi, out err), "重复购买应失败");
        StringAssert.Contains("已升级", err, "错误应含『已升级』");
        Assert.IsFalse(sim.BuyUpgrade(Side.Blue, HeroId.ZhenYue, out err), "EP 不足应失败");
        StringAssert.Contains("EP", err, "错误应含『EP』");
    }

    [Test]
    public void T2_磐石升级免疫负面()
    {
        GiveEp(Side.Blue, 2);
        sim.BuyUpgrade(Side.Blue, HeroId.PanShi, out _);
        var pan = SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(15, 118));
        var xing = SpawnHero(Side.Red, HeroId.XingLuo, new GridPos(15, 119));
        var duan = SpawnHero(Side.Red, HeroId.DuanFeng, new GridPos(15, 120));
        HeroAbilitySystem.ApplyOnHitEffects(sim, xing, pan);
        Assert.IsFalse(pan.Status.Has(DebuffType.Silence), "升级磐石免疫沉默");
        HeroAbilitySystem.ApplyOnHitEffects(sim, duan, pan);
        Assert.IsFalse(pan.Status.Has(DebuffType.Slow), "升级磐石免疫迟滞");

        var redPan = SpawnHero(Side.Red, HeroId.PanShi, new GridPos(15, 121));
        HeroAbilitySystem.ApplyOnHitEffects(sim, xing, redPan);
        Assert.IsTrue(redPan.Status.Has(DebuffType.Silence), "未升级磐石照常被沉默");
    }

    [Test]
    public void T3_镇岳迅捷移速不衰减()
    {
        GiveEp(Side.Blue, 2);
        Assert.IsTrue(sim.BuyUpgrade(Side.Blue, HeroId.ZhenYue, out _), "购买镇岳应成功");
        for (int i = 0; i < 1800; i++) sim.Step(); // 进入 3 分钟后
        var zy = SpawnHero(Side.Blue, HeroId.ZhenYue, new GridPos(15, 113));
        sim.SetMoveTarget(zy.Id, new GridPos(15, 123));
        for (int i = 0; i < 10; i++) sim.Step();
        Assert.AreEqual(2.2f, zy.PosF.X - 113.5f, 0.2f, "升级镇岳 10 tick 位移约 2.2 格(2.2 格/s)");

        var rzy = SpawnHero(Side.Red, HeroId.ZhenYue, new GridPos(15, 113));
        sim.SetMoveTarget(rzy.Id, new GridPos(15, 123));
        for (int i = 0; i < 10; i++) sim.Step();
        Assert.AreEqual(1.4f, rzy.PosF.X - 113.5f, 0.2f, "未升级镇岳 10 tick 位移约 1.4 格(1.4 格/s)");
    }

    [Test]
    public void T4_镇岳产速7点5秒()
    {
        GiveEp(Side.Blue, 2);
        sim.BuyUpgrade(Side.Blue, HeroId.ZhenYue, out _);
        var b = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 110));
        b.Prod = new ProductionSet();
        ProductionSystem.Enqueue(sim, b.Id, HeroId.ZhenYue, out _);
        for (int i = 0; i < 75; i++) sim.Step();
        Assert.AreEqual(1, CountHeroUnits(Side.Blue), "升级后 75 tick 出镇岳(7.5s)");

        sim = new SimCore(new SimConfig(), new SeededRng(2)); // 独立新局,避开蓝将行军占道
        sim.Game = _cfg;
        var rb = sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 134));
        rb.Prod = new ProductionSet();
        ProductionSystem.Enqueue(sim, rb.Id, HeroId.ZhenYue, out _);
        for (int i = 0; i < 75; i++) sim.Step();
        Assert.AreEqual(0, CountHeroUnits(Side.Red), "未升级 75 tick 未出");
        for (int i = 0; i < 125; i++) sim.Step(); // 20s 产速 = 200 tick
        Assert.AreEqual(1, CountHeroUnits(Side.Red), "未升级 200 tick 出镇岳");
    }

    private int CountHeroUnits(Side side)
    {
        int n = 0;
        foreach (var e in sim.Entities.Values)
            if (e.Kind == EntityKind.Unit && e.Side == side && e.Hero != null && !e.IsSoldier) n++;
        return n;
    }

    [Test]
    public void T5_断峰破军扇形()
    {
        GiveEp(Side.Red, 2);
        sim.BuyUpgrade(Side.Red, HeroId.DuanFeng, out _);
        var duan = SpawnHero(Side.Red, HeroId.DuanFeng, new GridPos(15, 118));
        var main = SpawnHero(Side.Blue, HeroId.XingLuo, new GridPos(15, 119)); // 正前 1 格
        var side = SpawnHero(Side.Blue, HeroId.XingLuo, new GridPos(16, 119)); // 侧前
        var back = SpawnHero(Side.Blue, HeroId.XingLuo, new GridPos(15, 117));  // 身后
        sim.Step();
        Assert.AreEqual(45f - 14f, main.Hp, 0.01f, "主目标吃全额 14");
        Assert.AreEqual(45f - 10.5f, side.Hp, 0.01f, "扇形内吃 75% = 10.5");
        Assert.AreEqual(45f, back.Hp, 0.01f, "身后不吃");
    }

    [Test]
    public void T6_逐风飞掠拆塔7每秒()
    {
        GiveEp(Side.Red, 2);
        sim.BuyUpgrade(Side.Red, HeroId.ZhuFeng, out _);
        var tower = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 114));
        var zf = SpawnHero(Side.Red, HeroId.ZhuFeng, new GridPos(15, 113));
        zf.TargetBuildingId = tower.Id;
        tower.Intent = SiegeIntent.Destroy;
        tower.IntentIssuer = Side.Red;
        sim.SetMoveTarget(zf.Id, zf.Pos);
        for (int i = 0; i < 20; i++) sim.Step();
        Assert.AreEqual(50f - 14f, tower.Hp, 0.01f, "飞掠每击 7×2=14");

        var rb = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 130));
        var bzf = SpawnHero(Side.Blue, HeroId.ZhuFeng, new GridPos(15, 129)); // 蓝方未升级
        bzf.TargetBuildingId = rb.Id;
        rb.Intent = SiegeIntent.Destroy;
        rb.IntentIssuer = Side.Blue;
        sim.SetMoveTarget(bzf.Id, bzf.Pos);
        for (int i = 0; i < 20; i++) sim.Step();
        Assert.AreEqual(50f - 6f, rb.Hp, 0.01f, "未升级每击 3×2=6");
    }

    [Test]
    public void T7_飞掠非战斗不锁塔()
    {
        GiveEp(Side.Red, 2);
        sim.BuyUpgrade(Side.Red, HeroId.ZhuFeng, out _);
        var tower = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 114));
        var zf = SpawnHero(Side.Red, HeroId.ZhuFeng, new GridPos(15, 117)); // 距塔 3,射程内
        sim.SetMoveTarget(zf.Id, new GridPos(15, 128)); // 途经
        float hp0 = zf.Hp;
        for (int i = 0; i < 100; i++) sim.Step();
        Assert.AreEqual(hp0, zf.Hp, 0.01f, "飞掠非战斗途经不被塔锁定");

        var rt = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 122));
        var bzf = SpawnHero(Side.Blue, HeroId.ZhuFeng, new GridPos(15, 119));
        sim.SetMoveTarget(bzf.Id, new GridPos(15, 134));
        float hp1 = bzf.Hp;
        for (int i = 0; i < 30; i++) sim.Step();
        Assert.Less(bzf.Hp, hp1, "未升级逐风途经被塔攻击");
    }

    [Test]
    public void T8_坠日诛心附加脆弱()
    {
        GiveEp(Side.Red, 2);
        sim.BuyUpgrade(Side.Red, HeroId.ZhuiRi, out _);
        var zr = SpawnHero(Side.Red, HeroId.ZhuiRi, new GridPos(15, 118));
        var zy = SpawnHero(Side.Blue, HeroId.ZhenYue, new GridPos(15, 119));
        var xing = SpawnHero(Side.Red, HeroId.XingLuo, new GridPos(15, 120));
        sim.Step(); // zy 先手打 zr;zr 命中 zy(7 伤 + 重伤 + 脆弱);xing 命中 zy:5×0.8×0.7×1.1=3.08
        Assert.IsTrue(zy.Status.Has(DebuffType.Fragile), "升级坠日打镇岳应附加脆弱");
        Assert.AreEqual(200f - 7f - 2.8f * 1.25f, zy.Hp, 0.01f, "D28 承伤层加算:2.8×(1+0.15+0.10)=3.5");
    }

    [Test]
    public void T9_星落诛灭()
    {
        GiveEp(Side.Red, 2);
        sim.BuyUpgrade(Side.Red, HeroId.XingLuo, out _);
        var xl = SpawnHero(Side.Red, HeroId.XingLuo, new GridPos(15, 118));
        var pan = SpawnHero(Side.Blue, HeroId.PanShi, new GridPos(15, 119));
        sim.Step();
        Assert.AreEqual(140f - 3.8f, pan.Shield, 0.01f, "诛灭每击 6×0.5+0.01×80=3.8");

        var bxl = SpawnHero(Side.Blue, HeroId.XingLuo, new GridPos(15, 120));
        var rpan = SpawnHero(Side.Red, HeroId.PanShi, new GridPos(15, 121));
        sim.Step();
        Assert.AreEqual(140f - 2.5f, rpan.Shield, 0.01f, "未升级每击 5×0.5=2.5");
    }
}
