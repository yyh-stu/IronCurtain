using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

public class HexTests
{
    private static GameConfig _cfg;
    private SimCore sim;

    [SetUp]
    public void SetUp()
    {
        if (_cfg == null)
            _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(12345));
        sim.Game = _cfg;
        HexSystem.Reset();
        sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(1, 0));
        sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(1, 22));
    }

    private void GiveEp(Side side, int amount)
    {
        for (int i = 0; i < amount; i++)
            sim.SpawnBuilding(side, BuildingId.ResourcePoint, new GridPos(i % 3, 24 + i / 3));
        Advance(905);
    }

    private void Advance(int ticks)
    {
        for (int i = 0; i < ticks; i++) sim.Step();
    }

    [Test]
    public void T01_节点解锁_CanDraw窗口正确()
    {
        GiveEp(Side.Blue, 2);
        Advance(885); // GiveEp 已推进 905 → 1790(窗口前)
        Assert.IsFalse(HexSystem.CanDraw(sim, Side.Blue), "1800 tick 前不应解锁抽取");
        Advance(10);
        Assert.IsTrue(sim.Tick >= 1800, "当前应为 1800 tick");
        Assert.IsTrue(HexSystem.CanDraw(sim, Side.Blue), "tick 1800 节点窗口内应可抽取(EP 足够)");
        Advance(100);
        Assert.IsFalse(HexSystem.CanDraw(sim, Side.Blue), "节点窗口(100 tick)过后不可抽取");
    }

    [Test]
    public void T02_三选一不重复_且同种子确定()
    {
        Advance(1800);
        var a = HexSystem.DrawChoices(sim, Side.Blue);
        Assert.AreEqual(3, a.Count, "抽取应返回三张卡");
        Assert.AreEqual(3, a.Distinct().Count(), "三张卡互不重复");
        foreach (var c in a) Assert.IsTrue(System.Enum.IsDefined(typeof(HexId), c), "卡必须合法: " + c);
        var sim2 = new SimCore(new SimConfig(), new SeededRng(12345));
        sim2.Game = _cfg;
        Advance(0);
        var b = HexSystem.DrawChoices(sim2, Side.Blue);
        Assert.IsTrue(a.SequenceEqual(b), "同种子两次抽取结果应一致");
    }

    [Test]
    public void T03_激活覆盖_新卡替换旧卡()
    {
        GiveEp(Side.Blue, 4); // 两张卡各 2EP
        sim.SpendEp(Side.Blue, 0);
        Assert.IsTrue(HexSystem.UseHex(sim, Side.Blue, HexId.SteelTide, 0, out _), "激活钢铁洪流应成功");
        Assert.IsFalse(HexSystem.TowerExempt(sim, Side.Blue), "钢铁洪流不提供不锁塔");
        Assert.IsTrue(HexSystem.UseHex(sim, Side.Blue, HexId.HiddenPath, 0, out _), "激活曲径通幽应成功");
        Assert.AreEqual(HexId.HiddenPath, HexSystem.ActiveOf(Side.Blue), "新卡应覆盖旧卡");
        Assert.IsTrue(HexSystem.TowerExempt(sim, Side.Blue), "曲径通幽生效后塔应不锁该方单位");
        Assert.IsFalse(HexSystem.DebuffImmune(sim, sim.Entities.Values.First(e => e.Side == Side.Blue)), "钢铁洪流效果应已消失");
    }

    [Test]
    public void T04_钢铁洪流_重单位加速且免疫负面()
    {
        GiveEp(Side.Blue, 2);
        var zhen = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(1, 5));
        zhen.Hero = HeroId.ZhenYue; zhen.Hp = zhen.MaxHp = 200; zhen.MoveSpeed = 0.3f;
        var pan = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(2, 5));
        pan.Hero = HeroId.PanShi; pan.Hp = pan.MaxHp = 80; pan.MoveSpeed = 0.3f;
        Assert.IsTrue(HexSystem.UseHex(sim, Side.Blue, HexId.SteelTide, 0, out _), "激活钢铁洪流");
        Assert.AreEqual(1.5f, HexSystem.MoveMultiplierOf(sim, zhen), 0.01, "镇岳(200 血)激活期间移速应 ×1.5");
        Assert.AreEqual(1f, HexSystem.MoveMultiplierOf(sim, pan), 0.01, "磐石(80 血)移速不受影响");
        Assert.IsTrue(HexSystem.DebuffImmune(sim, zhen), "钢铁洪流期间该方单位应免疫负面");
    }

    [Test]
    public void T05_快速部署_生产加速与人口加成()
    {
        GiveEp(Side.Blue, 2);
        Assert.IsTrue(HexSystem.UseHex(sim, Side.Blue, HexId.RapidDeploy, 0, out _), "激活快速部署");
        Assert.AreEqual(1.0 / 1.4, HexSystem.ProductionSecondsMultiplier(sim, Side.Blue), 0.01, "生产时间应 ×1/1.4");
        double ticks = 60 * 0.1 * HexSystem.ProductionSecondsMultiplier(sim, Side.Blue) * 10; // 60 tick 基准
        Assert.AreEqual(60 / 1.4, ticks, 2.0, "60 tick 生产应约 42.9 tick 完成(容差 2 tick)");
        Assert.AreEqual(10, HexSystem.PopCapBonusOf(sim, Side.Blue), "人口上限应 +20%(基础 50 → +10)");
    }

    [Test]
    public void T06_陷阵之志_阵亡叠层加输出()
    {
        GiveEp(Side.Blue, 2);
        var attacker = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(1, 5));
        attacker.Attack = 6f;
        Assert.IsTrue(HexSystem.UseHex(sim, Side.Blue, HexId.Vengeance, 0, out _), "激活陷阵之志");
        HexSystem.OnUnitDied(sim, Side.Blue);
        Assert.AreEqual(0.05f, HexSystem.OutputBonusOf(sim, attacker), 0.01, "阵亡 1 层应 +5% 输出");
        Assert.AreEqual(6 * 1.05, attacker.Attack * (1 + HexSystem.OutputBonusOf(sim, attacker)), 0.01, "6 攻应变为 6.3");
        HexSystem.OnUnitDied(sim, Side.Blue);
        Assert.AreEqual(0.10f, HexSystem.OutputBonusOf(sim, attacker), 0.01, "阵亡 2 层应 +10% 输出");
        Assert.AreEqual(6.6, attacker.Attack * (1 + HexSystem.OutputBonusOf(sim, attacker)), 0.01, "6 攻应变为 6.6");
    }

    [Test]
    public void T07_铁壁防线_堡垒生成脉冲与到期撤军()
    {
        GiveEp(Side.Blue, 2);
        Assert.IsTrue(HexSystem.UseHex(sim, Side.Blue, HexId.IronWall, 0, out _), "激活铁壁防线");
        var fort = sim.Entities.Values.FirstOrDefault(e => e.Side == Side.Blue && e.Kind == EntityKind.Building && e.IsFort);
        Assert.IsNotNull(fort, "激活后应生成堡垒");
        Assert.AreEqual(sim.Game.Tools.FortHp, fort.MaxHp, 0.01, "堡垒应为 30 血");

        var enemy = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(fort.Pos.Row + 0, fort.Pos.Col + 1));
        enemy.Hp = enemy.MaxHp = 100f;
        float hp0 = enemy.Hp;
        for (int i = 0; i < 30; i++) HexSystem.Tick(sim, 0.1f); // 3 秒
        Assert.Less(enemy.Hp, hp0, "3 秒后 2 格内敌单位应受到堡垒伤害");
        Assert.AreEqual(hp0 - 15, enemy.Hp, 0.01, "应恰好受到 15 伤害");

        var unit = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(0, 0));
        fort.GarrisonTemp.Add(unit.Id);
        unit.GarrisonBuildingId = fort.Id;
        for (int i = 0; i < 300; i++) HexSystem.Tick(sim, 0.1f); // 共 30 秒+,堡垒到期
        Assert.IsFalse(sim.Entities.ContainsKey(fort.Id), "30 秒后堡垒应消失");
        Assert.AreEqual(0, unit.GarrisonBuildingId, "驻军应离城");
        Assert.AreEqual(EntityState.Idle, unit.State, "离城单位应为 Idle");
    }

    [Test]
    public void T08_以逸待劳_建筑范围内增益()
    {
        GiveEp(Side.Blue, 2);
        var hq = sim.Entities.Values.First(e => e.Side == Side.Blue && e.Kind == EntityKind.Building);
        var near = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(hq.Pos.Row + 0, hq.Pos.Col + 5));
        var far = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(hq.Pos.Row + 0, hq.Pos.Col + 12));
        near.Attack = far.Attack = 6f;
        Assert.IsTrue(HexSystem.UseHex(sim, Side.Blue, HexId.HoldFast, 0, out _), "激活以逸待劳");
        Assert.AreEqual(0.10f, HexSystem.OutputBonusOf(sim, near), 0.01, "10 格内友军攻击应 +10%");
        Assert.AreEqual(6.6, near.Attack * (1 + HexSystem.OutputBonusOf(sim, near)), 0.01, "6 攻应变为 6.6");
        Assert.AreEqual(-0.10f, HexSystem.DamageTakenBonusOf(sim, near), 0.01, "10 格内友军承伤应 -10%");
        Assert.AreEqual(0f, HexSystem.OutputBonusOf(sim, far), 0.01, "10 格外友军输出不受影响");
    }

    [Test]
    public void T09_沙包_目标承伤减半输出减半()
    {
        GiveEp(Side.Red, 2); // 沙包给红方用,EP 应给红方
        var target = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(1, 10));
        target.Hero = HeroId.DuanFeng; target.Attack = 14f; target.Hp = target.MaxHp = 60;
        Assert.IsTrue(HexSystem.UseHex(sim, Side.Red, HexId.Sandbag, target.Id, out _), "激活沙包");
        Assert.AreEqual(0.5f, HexSystem.DamageReductionOf(sim, target), 0.01, "沙包目标承伤应 ×0.5");
        Assert.AreEqual(-0.5f, HexSystem.OutputBonusOf(sim, target), 0.01, "沙包目标输出应 ×0.5");
        Assert.AreEqual(7f, target.Attack * (1 + HexSystem.OutputBonusOf(sim, target)), 0.01, "反击 14 应变为 7");
        Assert.AreEqual(3f, 6 * HexSystem.DamageReductionOf(sim, target), 0.01, "承受 6 打应只掉 3");
    }

    [Test]
    public void T10_道具_攻城锥直伤与临时阵地()
    {
        GiveEp(Side.Blue, 4); // 四枚攻城锥各 1EP
        var enemyB = sim.Entities.Values.First(e => e.Side == Side.Red && e.Kind == EntityKind.Building && !e.Rubbled);
        float max = enemyB.MaxHp;
        enemyB.Hp = 50f;
        Assert.IsTrue(HexSystem.UseRam(sim, Side.Blue, enemyB.Id, out _), "使用攻城锥应成功");
        Assert.AreEqual(35f, enemyB.Hp, 0.01, "50 血建筑应受 15 直伤变 35");
        for (int i = 0; i < 2; i++)
        {
            Assert.IsTrue(HexSystem.UseRam(sim, Side.Blue, enemyB.Id, out _), "攻城锥 " + i);
            enemyB.Hp = 5f; // 压到 5 血验证废墟化
            Assert.IsTrue(HexSystem.UseRam(sim, Side.Blue, enemyB.Id, out _), "最后一击");
            if (enemyB.Rubbled) break;
        }
        Assert.IsTrue(enemyB.Rubbled, "攻城锥连续命中后建筑应废墟化");

        var cell = new GridPos(0, 10);
        Assert.IsTrue(HexSystem.UseFort(sim, Side.Blue, cell, out _), "空闲格放临时阵地应成功");
        var fort = sim.Entities.Values.First(e => e.Side == Side.Blue && e.IsFort);
        Assert.AreEqual(sim.Game.Tools.FortHp, fort.Hp, 0.01, "临时阵地应为 30 血");
        string err;
        Assert.IsFalse(HexSystem.UseFort(sim, Side.Blue, cell, out err), "占用格再放应报错");
        Assert.IsTrue(err.Contains("目标"), "错误信息应含'目标'");
    }
}
