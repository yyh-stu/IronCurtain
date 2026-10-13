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
        sim = NewSim(12345);
    }

        private SimCore NewSim(ulong seed)
        {
            var s = new SimCore(new SimConfig(), new SeededRng(seed)) { Game = _cfg };
            // 2026-09-16 三十七轮补记:蓝基地从 (1,0)(地图轮廓外的角落,临时阵地放置规则上线后不再可用)
            // 移到中路己方广场内 (80,8) —— 与真实对局同款几何。
            s.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(80, 8));
            s.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(1, 22));
            return s;
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

    private void PrepareHeld(HexId card, ulong seed, Side side = Side.Blue)
    {
        sim = NewSim(seed);
        GiveEp(side, 2);
        Advance(895); // tick 1800 = 3 分钟节点
        var draw = HexSystem.TryDraw(sim, side);
        Assert.IsTrue(draw.Success, "抽取失败:" + draw.Error);
        CollectionAssert.Contains(draw.Choices, card, "测试 seed 未抽到目标卡:" + card);
        Assert.AreEqual(HexChooseError.None, HexSystem.TryChoose(sim, side, card), "选择目标卡");
        CollectionAssert.Contains(HexSystem.HandOf(sim, side), card, "目标卡应进入手牌");
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
        GiveEp(Side.Blue, 2);
        Advance(895);
        float ep0 = sim.EpOf(Side.Blue);
        var drawA = HexSystem.TryDraw(sim, Side.Blue);
        Assert.IsTrue(drawA.Success);
        var a = drawA.Choices;
        Assert.AreEqual(3, a.Count, "抽取应返回三张卡");
        Assert.AreEqual(3, a.Distinct().Count(), "三张卡互不重复");
        foreach (var c in a) Assert.IsTrue(System.Enum.IsDefined(typeof(HexId), c), "卡必须合法: " + c);
        Assert.AreEqual(ep0 - sim.Game.Ep.HexDrawCost, sim.EpOf(Side.Blue), 0.001f, "成功抽取只在此时扣费");

        var sim2 = NewSim(12345);
        sim = sim2;
        GiveEp(Side.Blue, 2);
        Advance(895);
        var b = HexSystem.TryDraw(sim2, Side.Blue).Choices;
        Assert.IsTrue(a.SequenceEqual(b), "同种子两次抽取结果应一致");
    }

    [Test]
    public void T03_激活覆盖_新卡替换旧卡()
    {
        GiveEp(Side.Blue, 4);
        Advance(895);
        var firstDraw = HexSystem.TryDraw(sim, Side.Blue);
        var first = firstDraw.Choices[0];
        Assert.AreEqual(HexChooseError.None, HexSystem.TryChoose(sim, Side.Blue, first));
        Advance(1200); // 5 分钟节点；首张仍在手牌
        var secondDraw = HexSystem.TryDraw(sim, Side.Blue);
        var second = secondDraw.Choices.First(x => x != first && x != HexId.Sandbag);
        Assert.AreEqual(HexChooseError.None, HexSystem.TryChoose(sim, Side.Blue, second));
        Assert.AreEqual(2, HexSystem.HandOf(sim, Side.Blue).Count, "手牌可跨时间保存多张");
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, first));
        Assert.AreEqual(first, HexSystem.ActiveOf(sim, Side.Blue));
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, second));
        Assert.AreEqual(second, HexSystem.ActiveOf(sim, Side.Blue), "新卡应覆盖旧卡");
        Assert.AreEqual(0, HexSystem.HandOf(sim, Side.Blue).Count, "两张牌各消耗一次");
    }

    [Test]
    public void T04_钢铁洪流_重单位加速且免疫负面()
    {
        PrepareHeld(HexId.SteelTide, 2);
        var zhen = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(1, 5));
        zhen.Hero = HeroId.ZhenYue; zhen.Hp = zhen.MaxHp = 200; zhen.MoveSpeed = 0.3f;
        var pan = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(2, 5));
        pan.Hero = HeroId.PanShi; pan.Hp = pan.MaxHp = 80; pan.MoveSpeed = 0.3f;
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.SteelTide), "激活钢铁洪流");
        Assert.AreEqual(1.5f, HexSystem.MoveMultiplierOf(sim, zhen), 0.01, "镇岳(200 血)激活期间移速应 ×1.5");
        Assert.AreEqual(1f, HexSystem.MoveMultiplierOf(sim, pan), 0.01, "磐石(80 血)移速不受影响");
        Assert.IsTrue(HexSystem.DebuffImmune(sim, zhen), "钢铁洪流期间该方单位应免疫负面");
    }

    [Test]
    public void T05_快速部署_生产加速与人口加成()
    {
        PrepareHeld(HexId.RapidDeploy, 2);
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.RapidDeploy), "激活快速部署");
        Assert.AreEqual(1.0 / 1.4, HexSystem.ProductionSecondsMultiplier(sim, Side.Blue), 0.01, "生产时间应 ×1/1.4");
        double ticks = 60 * 0.1 * HexSystem.ProductionSecondsMultiplier(sim, Side.Blue) * 10; // 60 tick 基准
        Assert.AreEqual(60 / 1.4, ticks, 2.0, "60 tick 生产应约 42.9 tick 完成(容差 2 tick)");
        // 2026-09-16 三十七轮反写:v1.6 旧口径"基础 50 → +10"已废,名额制基础 8(GDD)→ +2;
        // 真实路径(生产/名额校验实际读 PopulationCapOf)由 HexIntegrationTests.T02 覆盖
        Assert.AreEqual(2, HexSystem.PopCapBonusOf(sim, Side.Blue), "人口上限应 +20%(名额制基础 8 → +2)");
    }

    [Test]
    public void T06_陷阵之志_阵亡叠层加输出()
    {
        PrepareHeld(HexId.Vengeance, 17);
        var attacker = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(1, 5));
        attacker.Attack = 6f;
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.Vengeance), "激活陷阵之志");
        HexSystem.OnUnitDied(sim, Side.Blue);
        Assert.AreEqual(0.05f, HexSystem.OutputBonusOf(sim, attacker), 0.01, "阵亡 1 层应 +5% 输出");
        Assert.AreEqual(6 * 1.05, attacker.Attack * (1 + HexSystem.OutputBonusOf(sim, attacker)), 0.01, "6 攻应变为 6.3");
        HexSystem.OnUnitDied(sim, Side.Blue);
        Assert.AreEqual(0.10f, HexSystem.OutputBonusOf(sim, attacker), 0.01, "阵亡 2 层应 +10% 输出");
        Assert.AreEqual(6.6, attacker.Attack * (1 + HexSystem.OutputBonusOf(sim, attacker)), 0.01, "6 攻应变为 6.6");
    }

    [Test]
    public void T07_铁壁防线_重装军团承伤减免()
    {
        // 2026-09-16 制作人拍板:铁壁防线由"生成 30 血堡垒"改为**对重装军团生效**的增益卡。
        // 重装 = 主将 Tag 以「重装」开头(磐石·重装塔盾 / 镇岳·重装攻城);军团口径 = 主将 + 其士兵。
        PrepareHeld(HexId.IronWall, 1);
        var zhen = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 12));
        zhen.Hero = HeroId.ZhenYue;
        var pan = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 13));
        pan.Hero = HeroId.PanShi;
        var duan = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 14));
        duan.Hero = HeroId.DuanFeng;
        var soldier = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 15));
        soldier.IsSoldier = true; soldier.GeneralId = zhen.Id;   // 镇岳的兵:随主将整团生效

        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.IronWall), "激活铁壁防线");
        Assert.AreEqual(-0.30f, HexSystem.DamageTakenBonusOf(sim, zhen), 0.01, "镇岳(重装攻城)承伤应 -30%");
        Assert.AreEqual(-0.30f, HexSystem.DamageTakenBonusOf(sim, pan), 0.01, "磐石(重装塔盾)承伤应 -30%");
        Assert.AreEqual(-0.30f, HexSystem.DamageTakenBonusOf(sim, soldier), 0.01, "重装主将的士兵随军团整团生效");
        Assert.AreEqual(0f, HexSystem.DamageTakenBonusOf(sim, duan), 0.01, "断峰(战士·长矛)不受影响");
        Assert.IsFalse(sim.Entities.Values.Any(e => e.IsFort), "铁壁防线不再生成堡垒(旧为 30 血堡垒)");

        Advance(305); // 30s+ 过期
        Assert.AreEqual(0f, HexSystem.DamageTakenBonusOf(sim, zhen), 0.01, "到期后增益应消失");
    }

    [Test]
    public void T08_以逸待劳_建筑范围内增益()
    {
        PrepareHeld(HexId.HoldFast, 1);
        var hq = sim.Entities.Values.First(e => e.Side == Side.Blue && e.Kind == EntityKind.Building);
        var near = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(hq.Pos.Row + 0, hq.Pos.Col + 5));
        var far = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(hq.Pos.Row + 0, hq.Pos.Col + 12));
        near.Attack = far.Attack = 6f;
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.HoldFast), "激活以逸待劳");
        Assert.AreEqual(0.10f, HexSystem.OutputBonusOf(sim, near), 0.01, "10 格内友军攻击应 +10%");
        Assert.AreEqual(6.6, near.Attack * (1 + HexSystem.OutputBonusOf(sim, near)), 0.01, "6 攻应变为 6.6");
        Assert.AreEqual(-0.10f, HexSystem.DamageTakenBonusOf(sim, near), 0.01, "10 格内友军承伤应 -10%");
        Assert.AreEqual(0f, HexSystem.OutputBonusOf(sim, far), 0.01, "10 格外友军输出不受影响");
    }

    [Test]
    public void T09_沙包_目标承伤减半输出减半()
    {
        PrepareHeld(HexId.Sandbag, 7, Side.Red);
        var target = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(1, 10));
        target.Hero = HeroId.DuanFeng; target.Attack = 14f; target.Hp = target.MaxHp = 60;
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Red, HexId.Sandbag, target.Id), "激活沙包");
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
        Assert.IsTrue(ToolsSystem.UseRam(sim, Side.Blue, enemyB.Id, out _), "使用攻城锥应成功");
        Assert.AreEqual(35f, enemyB.Hp, 0.01, "50 血建筑应受 15 直伤变 35");
        for (int i = 0; i < 2; i++)
        {
            Assert.IsTrue(ToolsSystem.UseRam(sim, Side.Blue, enemyB.Id, out _), "攻城锥 " + i);
            enemyB.Hp = 5f; // 压到 5 血验证废墟化
            Assert.IsTrue(ToolsSystem.UseRam(sim, Side.Blue, enemyB.Id, out _), "最后一击");
            if (enemyB.Rubbled) break;
        }
        Assert.IsTrue(enemyB.Rubbled, "攻城锥连续命中后建筑应废墟化");

        // 2026-09-16 三十七轮补记:格子换到中路己方视野内(基地 (80,8) 视野 40,此格距 8);
        // 并把旧 T07(铁壁堡垒)的 脉冲/到期/不可驻军 覆盖迁到道具堡垒上(铁壁已改增益卡)。
        var cell = new GridPos(80, 16);
        Assert.IsTrue(ToolsSystem.UseFort(sim, Side.Blue, cell, out _), "空闲格放临时阵地应成功");
        var fort = sim.Entities.Values.First(e => e.Side == Side.Blue && e.IsFort);
        Assert.AreEqual(sim.Game.Tools.FortHp, fort.Hp, 0.01, "临时阵地应为 100 血(道具口径)");
        string err;
        Assert.IsFalse(ToolsSystem.UseFort(sim, Side.Blue, cell, out err), "占用格再放应报错");
        Assert.IsTrue(err.Contains("目标"), "错误信息应含'目标'");

        // 堡垒不可驻军(2026-09-11 制作人裁决:只可摧毁,持续攻击)
        var unit = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 15));
        string garrisonError;
        Assert.IsFalse(GarrisonSystem.EnterTemp(sim, new[] { unit.Id }, fort.Id, out garrisonError), "堡垒应拒绝临时驻军");
        Assert.AreEqual("堡垒不可驻军(2026-09-11 制作人裁决)", garrisonError);

        // 脉冲:每 3 秒对 2 米内敌军 15 伤(自旧 T07 迁来;道具堡垒持续攻击)
        var foe = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 17));
        foe.Hp = foe.MaxHp = 100f;
        float f0 = foe.Hp;
        Advance(30);
        Assert.AreEqual(f0 - 15, foe.Hp, 0.01, "3 秒后 2 米内敌军应恰受 15 脉冲伤害");
        Advance(310); // 共 30 秒+,到期
        Assert.IsFalse(sim.Entities.ContainsKey(fort.Id), "30 秒后临时阵地应消失");
    }

    [Test]
    public void T11_抽选存用原子性_失败不扣费不丢状态()
    {
        GiveEp(Side.Blue, 2);
        Advance(895);
        float before = sim.EpOf(Side.Blue);
        Assert.AreEqual(HexUseError.CardNotHeld,
            HexSystem.TryUseHeld(sim, Side.Blue, HexId.SteelTide), "未持牌不可释放");
        Assert.AreEqual(before, sim.EpOf(Side.Blue), 0.001f, "无牌释放不应扣费");

        var draw = HexSystem.TryDraw(sim, Side.Blue);
        Assert.IsTrue(draw.Success);
        float afterDraw = sim.EpOf(Side.Blue);
        Assert.AreEqual(before - sim.Game.Ep.HexDrawCost, afterDraw, 0.001f, "抽取恰扣一次费用");
        var pending = HexSystem.PendingChoicesOf(sim, Side.Blue).ToArray();
        var repeat = HexSystem.TryDraw(sim, Side.Blue);
        Assert.AreEqual(HexDrawError.NodeConsumed, repeat.Error, "同节点只可抽一次");
        Assert.AreEqual(afterDraw, sim.EpOf(Side.Blue), 0.001f, "重复抽取不得再扣费");
        CollectionAssert.AreEqual(pending, HexSystem.PendingChoicesOf(sim, Side.Blue));

        Assert.AreEqual(HexChooseError.ChoiceNotOffered,
            HexSystem.TryChoose(sim, Side.Blue, (HexId)99), "不可选候选外卡牌");
        CollectionAssert.AreEqual(pending, HexSystem.PendingChoicesOf(sim, Side.Blue));
        Assert.AreEqual(HexChooseError.None, HexSystem.TryChoose(sim, Side.Blue, pending[0]));
        Advance(150);
        CollectionAssert.Contains(HexSystem.HandOf(sim, Side.Blue), pending[0], "手牌应跨抽取窗口保存");
    }

    [Test]
    public void T12_沙包非法目标_保留手牌且不覆盖效果()
    {
        PrepareHeld(HexId.Sandbag, 7, Side.Red);
        Assert.AreEqual(HexUseError.InvalidTarget,
            HexSystem.TryUseHeld(sim, Side.Red, HexId.Sandbag, 999999));
        CollectionAssert.Contains(HexSystem.HandOf(sim, Side.Red), HexId.Sandbag, "失败后牌仍在手中");
        Assert.IsNull(HexSystem.ActiveOf(sim, Side.Red), "失败不得写入活动效果");
    }

    [Test]
    public void T13_两局海克斯状态完全隔离()
    {
        PrepareHeld(HexId.SteelTide, 2);
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.SteelTide));
        var first = sim;
        var second = NewSim(2);

        Assert.AreEqual(HexId.SteelTide, HexSystem.ActiveOf(first, Side.Blue));
        Assert.IsNull(HexSystem.ActiveOf(second, Side.Blue));
        Assert.AreEqual(0, HexSystem.HandOf(second, Side.Blue).Count);
        Assert.AreEqual(0, HexSystem.PendingChoicesOf(second, Side.Blue).Count);
    }

    [Test]
    public void T14_单次抽取只推进全局Rng一次()
    {
        sim = NewSim(12345);
        GiveEp(Side.Blue, 2);
        Advance(895);
        var expected = new SeededRng(12345);
        expected.NextUInt64();
        ulong expectedNext = expected.NextUInt64();

        Assert.IsTrue(HexSystem.TryDraw(sim, Side.Blue).Success);
        Assert.AreEqual(expectedNext, sim.Rng.NextUInt64(), "三张候选应只消费一个全局随机数");
    }

    [Test]
    public void T15_临时阵地越界_不扣费不抛异常()
    {
        GiveEp(Side.Blue, 1);
        float before = sim.EpOf(Side.Blue);
        Assert.IsFalse(ToolsSystem.UseFort(sim, Side.Blue, new GridPos(999, 999), out var error));
        StringAssert.Contains("越界", error);
        Assert.AreEqual(before, sim.EpOf(Side.Blue), 0.001f, "非法落点不得扣 EP");
    }

    [Test]
    public void T16_临时阵地废墟占位_不扣费不抛异常()
    {
        // 2026-09-16 三十七轮补记:废墟建筑摆在**中路己方视野内**(基地 (80,8) 视野 40 内),
        // 让校验穿过 轮廓/水域/路权 三关后停在"占用"上(旧格在轮廓外会先报别的)。
        var rubble = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(80, 20));
        rubble.Rubbled = true; // SimCore remains authoritative for the reserved building cell.
        GiveEp(Side.Blue, 1);
        float before = sim.EpOf(Side.Blue);

        Assert.IsFalse(ToolsSystem.UseFort(sim, Side.Blue, rubble.Pos, out var error));
        StringAssert.Contains("占用", error);
        Assert.AreEqual(before, sim.EpOf(Side.Blue), 0.001f, "SpawnEntity 会拒绝的格子不得先扣 EP");
    }

    [Test]
    public void T17_手牌允许同卡重复持有并逐张消耗()
    {
        const ulong seed = 12345;
        GiveEp(Side.Blue, 4);
        Advance(895);
        var first = HexSystem.TryDraw(sim, Side.Blue);
        Assert.IsTrue(first.Success);
        Assert.AreEqual(HexChooseError.None, HexSystem.TryChoose(sim, Side.Blue, first.Choices[0]));
        Advance(1200);
        var second = HexSystem.TryDraw(sim, Side.Blue);
        Assert.IsTrue(second.Success);
        var duplicate = first.Choices.FirstOrDefault(second.Choices.Contains);
        Assert.IsTrue(first.Choices.Contains(duplicate) && second.Choices.Contains(duplicate),
            "固定种子的两个节点应有公共候选以验证重复持牌");

        // 用同种子重放，并在两个节点选择同一张公共候选。
        sim = NewSim(seed);
        GiveEp(Side.Blue, 4);
        Advance(895);
        Assert.IsTrue(HexSystem.TryDraw(sim, Side.Blue).Choices.Contains(duplicate));
        Assert.AreEqual(HexChooseError.None, HexSystem.TryChoose(sim, Side.Blue, duplicate));
        Advance(1200);
        Assert.IsTrue(HexSystem.TryDraw(sim, Side.Blue).Choices.Contains(duplicate));
        Assert.AreEqual(HexChooseError.None, HexSystem.TryChoose(sim, Side.Blue, duplicate));
        Assert.AreEqual(2, HexSystem.HandOf(sim, Side.Blue).Count(x => x == duplicate));
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, duplicate));
        Assert.AreEqual(1, HexSystem.HandOf(sim, Side.Blue).Count(x => x == duplicate),
            "一次释放只消耗一张同名手牌");
    }

    [Test]
    public void T18_重复HexId配置_抽取前拒绝且不扣费()
    {
        var invalid = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        invalid.Hexes[1].Id = invalid.Hexes[0].Id;
        sim = new SimCore(new SimConfig(), new SeededRng(12345)) { Game = invalid };
        sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(1, 0));
        GiveEp(Side.Blue, 2);
        Advance(895);
        float before = sim.EpOf(Side.Blue);

        var draw = HexSystem.TryDraw(sim, Side.Blue);

        Assert.AreEqual(HexDrawError.InvalidState, draw.Error);
        Assert.AreEqual(before, sim.EpOf(Side.Blue), 0.001f);
        Assert.AreEqual(0, HexSystem.PendingChoicesOf(sim, Side.Blue).Count);
    }

    [Test]
    public void T19_训练场自由抽取_不限时不限次()
    {
        // 2026-09-17 制作人拍板:"训练场海克斯抽卡不限时间不限次数"。
        // HexFreeDraw=true:解锁分钟之前(tick≈905,远未到第 3 分钟窗口)可抽,且连续抽两次不记节点消耗;
        // 对照:常规对局同一时刻 NotInDrawWindow。
        sim.HexFreeDraw = true;
        GiveEp(Side.Blue, 4);
        float ep0 = sim.EpOf(Side.Blue);
        var d1 = HexSystem.TryDraw(sim, Side.Blue);
        Assert.IsTrue(d1.Success, "窗口外应可自由抽取,错误=" + d1.Error);
        Assert.AreEqual(HexChooseError.None, HexSystem.TryChoose(sim, Side.Blue, d1.Choices[0]));
        var d2 = HexSystem.TryDraw(sim, Side.Blue);
        Assert.IsTrue(d2.Success, "同节点应可再次抽取(不限次),错误=" + d2.Error);
        Assert.AreEqual(ep0 - 2 * sim.Game.Ep.HexDrawCost, sim.EpOf(Side.Blue), 0.001f, "每次抽取仍照扣 EP");

        var control = NewSim(77);
        control.SpawnBuilding(Side.Blue, BuildingId.ResourcePoint, new GridPos(0, 24));
        for (int i = 0; i < 905; i++) control.Step();
        Assert.AreEqual(HexDrawError.NotInDrawWindow, HexSystem.TryDraw(control, Side.Blue).Error,
            "对照:常规对局(未设 HexFreeDraw)窗口外仍不可抽");
    }
}
