using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

/// <summary>
/// 三十七轮(2026-09-16 异构全量检测):海克斯/道具的**端到端**用例。
///
/// 既有 HexTests 的教训:T05"快速部署"只断言了查询函数自身的返回值
/// (ProductionSecondsMultiplier==1/1.4、PopCapBonusOf==+10),
/// 而生产系统与 PopulationCapOf **从未调用过这两个函数** —— 卡能抽、能选、能"激活",
/// 实际效果为零,398 项全绿期间没有任何用例能发现。
/// 本套全部走**真实系统路径**(生产 tick / 名额查询 / 塔战斗 tick / 真实移速),
/// 另补 8 张卡中从未被测试引用的 轻装机动/曲径通幽,以及临时阵地的放置校验。
/// </summary>
public class HexIntegrationTests
{
    private static GameConfig _cfg;
    private SimCore sim;

    [SetUp]
    public void SetUp()
    {
        if (_cfg == null)
            _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = NewSim(11);
    }

    private SimCore NewSim(ulong seed)
    {
        var s = new SimCore(new SimConfig(), new SeededRng(seed)) { Game = _cfg };
        s.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 110));
        s.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 180));
        return s;
    }

    private static void GiveEpOn(SimCore s, Side side, int amount)
    {
        for (int i = 0; i < amount; i++)
            s.SpawnBuilding(side, BuildingId.ResourcePoint, new GridPos(i % 3, 24 + i / 3));
        AdvanceOn(s, 905);
    }

    private static void AdvanceOn(SimCore s, int ticks)
    {
        for (int i = 0; i < ticks; i++) s.Step();
    }

    /// <summary>确定性种子搜索:抽取结果由全局 RNG 决定,遍历固定种子表直到目标卡出现在三选一里。
    /// 比写死单颗种子抗 RNG 变更;正常情况第一两颗种子即命中,失败信息明确到卡。</summary>
    private static readonly ulong[] SeedTable =
        { 21, 2, 7, 17, 3, 5, 11, 13, 19, 23, 29, 31, 37, 41, 43, 47 };

    private SimCore PrepareHeldSearch(HexId card, Side side)
    {
        foreach (var seed in SeedTable)
        {
            var s = NewSim(seed);
            GiveEpOn(s, side, 2);
            AdvanceOn(s, 895); // tick 1800 = 3 分钟抽取节点
            var draw = HexSystem.TryDraw(s, side);
            if (!draw.Success || !draw.Choices.Contains(card)) continue;
            Assert.AreEqual(HexChooseError.None, HexSystem.TryChoose(s, side, card), "选择目标卡");
            return s;
        }
        Assert.Fail("种子表未覆盖卡:" + card + "(扩大 SeedTable 后重试)");
        return null;
    }

    private static Entity AttachProd(SimCore s, Side side)
    {
        var b = s.Entities.Values.First(e => e.Side == side && e.Kind == EntityKind.Building
                                             && e.Building == BuildingId.Base);
        b.Prod = new ProductionSet();
        return b;
    }

    private static List<Entity> GeneralsOf(SimCore s, Side side)
    {
        var list = new List<Entity>();
        foreach (var e in s.Entities.Values)
            if (e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == side
                && e.Hero != null && e.State != EntityState.Dead)
                list.Add(e);
        return list;
    }

    private static int TicksUntilFirstGeneral(SimCore s, Entity b)
    {
        Assert.IsTrue(ProductionSystem.Enqueue(s, b.Id, HeroId.PanShi, out _), "入队应成功");
        int t = 0;
        while (t < 400 && GeneralsOf(s, b.Side).Count == 0) { s.Step(); t++; }
        Assert.Greater(GeneralsOf(s, b.Side).Count, 0, "400 tick 内应有将产出(测速前提)");
        return t;
    }

    // ================= 快速部署:两个效果必须走真实系统路径 =================

    [Test]
    public void T01_快速部署_生产系统真实提速()
    {
        // 基线:无卡,20s/将
        var baselineSim = NewSim(11);
        var baselineBase = AttachProd(baselineSim, Side.Blue);
        int baseline = TicksUntilFirstGeneral(baselineSim, baselineBase);
        Assert.That(baseline, Is.InRange(195, 215), "基线应在 20s 附近,实际 " + baseline);

        // 有卡:同一基地、同一队列,激活快速部署后从真实抽取路径释放
        sim = PrepareHeldSearch(HexId.RapidDeploy, Side.Blue);
        var b = AttachProd(sim, Side.Blue);
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.RapidDeploy), "释放快速部署");
        int boosted = TicksUntilFirstGeneral(sim, b);

        // 20s → 20/1.4 ≈ 14.3s:至少快 30 tick(余量覆盖出生错峰),防"查询函数绿但系统没接线"回退
        Assert.LessOrEqual(boosted, baseline - 30,
            "快速部署应使 20s 生产缩短到 ≈14.3s(基线 " + baseline + ",实测 " + boosted + ")");
    }

    [Test]
    public void T02_快速部署_名额上限真实加成与到期恢复()
    {
        Assert.AreEqual(8, sim.PopulationCapOf(Side.Blue), "GDD:常规对局名将上限 8");
        sim = PrepareHeldSearch(HexId.RapidDeploy, Side.Blue);
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.RapidDeploy));
        Assert.AreEqual(10, sim.PopulationCapOf(Side.Blue),
            "快速部署期间名额上限应 8→10(+20%,四舍五入);生产/名额校验读的正是这个查询");
        AdvanceOn(sim, 460); // DurationSeconds=45s
        Assert.AreEqual(8, sim.PopulationCapOf(Side.Blue), "到期后应回落到 8");
    }

    // ================= 铁壁防线:重装军团增益(2026-09-16 制作人拍板,真实战斗路径) =================

    [Test]
    public void T03_铁壁防线_重装军团真实承伤降低三成()
    {
        // 同一攻击者、同一摆位,无卡/有卡各跑一场真实近战,比较镇岳主将的总掉血比例。
        float DropOf(SimCore s, bool withCard)
        {
            if (withCard)
            {
                s = PrepareHeldSearch(HexId.IronWall, Side.Blue);
                Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(s, Side.Blue, HexId.IronWall), "释放铁壁防线");
            }
            var atk = s.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 12));
            atk.Attack = 10f; atk.AttackIntervalSec = 1f; atk.Range = 2f; atk.MoveSpeed = 0f;
            var def = s.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 13)); // 距 1 米,贴脸
            def.Hero = HeroId.ZhenYue; def.Attack = 0f; def.MoveSpeed = 0f;
            def.Hp = def.MaxHp = 2000f;
            AdvanceOn(s, 100);   // 10 秒真实战斗
            return 2000f - def.Hp;
        }

        float plain = DropOf(NewSim(11), false);
        Assert.Greater(plain, 50f, "对照前提:无卡时镇岳应持续掉血(实测 " + plain.ToString("F1") + ")");
        float walled = DropOf(null, true);
        Assert.That(walled / plain, Is.InRange(0.60f, 0.80f),
            "铁壁防线期间重装军团承伤应 ×0.7(实测 " + walled.ToString("F1") + " vs " + plain.ToString("F1") + ")");
    }

    // ================= 临时阵地:放置校验(2026-09-16 制作人终审:三路+己方大本营+最前建筑视野) =================

    /// <summary>放置测试专用对局:蓝基地在真实位置(中路 (80,8),己方广场内),红塔在敌方纵深。</summary>
    private SimCore NewPlacementSim(ulong seed = 11)
    {
        var s = new SimCore(new SimConfig(), new SeededRng(seed)) { Game = _cfg };
        s.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(80, 8));    // 世界 (8.5,80.5):中路+己方广场
        s.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(80, 180));  // 敌方纵深的中路塔
        return s;
    }

    /// <summary>给放置测试注 EP,但资源点摆在**基地后方**(cols 1..n < 基地 8):
    /// 共用版 GiveEpOn 把资源点放到 (24..26,0..2),它们会变成"最前己方建筑"(视野仅 20)劫持判定。</summary>
    private static void GiveEpBehind(SimCore s, Side side, int amount)
    {
        for (int i = 0; i < amount; i++)
            s.SpawnBuilding(side, BuildingId.ResourcePoint, new GridPos(80, 1 + i)); // 世界 x=1.5..,均在基地(x=8.5)之后
        AdvanceOn(s, 905);
    }

    [Test]
    public void T04_临时阵地_水域不可放置()
    {
        sim = NewPlacementSim();
        GiveEpBehind(sim, Side.Blue, 1);
        var river = new GridPos(40, 135); // x=135.5 ∈ 河[130,140),y=40.5 不在三座桥(y≈10/80/150±7)上
        Assert.IsTrue(TerrainMap.IsWater(SimVec2.FromGrid(river)), "测试前提:该格是水域");
        float before = sim.EpOf(Side.Blue);
        string err;
        Assert.IsFalse(ToolsSystem.UseFort(sim, Side.Blue, river, out err), "水域不可放置临时阵地");
        StringAssert.Contains("水", err);
        Assert.AreEqual(before, sim.EpOf(Side.Blue), 0.001f, "非法落点不得扣 EP");
    }

    [Test]
    public void T05_临时阵地_路上不可超出最前己方建筑视野()
    {
        sim = NewPlacementSim();
        GiveEpBehind(sim, Side.Blue, 1);
        var cell = new GridPos(80, 60); // 世界 (60.5,80.5):在中路上,但距基地 (8.5,80.5) 52 > 视野 40
        float before = sim.EpOf(Side.Blue);
        string err;
        Assert.IsFalse(ToolsSystem.UseFort(sim, Side.Blue, cell, out err), "视野外的路段不可放置");
        StringAssert.Contains("三条路", err, "错误信息应说明放置范围");
        Assert.AreEqual(before, sim.EpOf(Side.Blue), 0.001f, "非法落点不得扣 EP");
    }

    [Test]
    public void T06_临时阵地_合法落点与战略前推()
    {
        sim = NewPlacementSim();
        GiveEpBehind(sim, Side.Blue, 4); // 三次成功放置 + 余量

        // ① 非路非广场(草坪纵深):不可放
        string err;
        Assert.IsFalse(ToolsSystem.UseFort(sim, Side.Blue, new GridPos(60, 60), out err), "草坪(非三路非广场)不可放");
        StringAssert.Contains("三条路", err);

        // ② 中路、己方建筑视野内:可放(基地视野 40,距 22)
        Assert.IsTrue(ToolsSystem.UseFort(sim, Side.Blue, new GridPos(80, 30), out err), "中路视野内应可放:" + err);
        // ③ 己方大本营区域(广场圆盘,不在任何路上):可放
        Assert.IsTrue(ToolsSystem.UseFort(sim, Side.Blue, new GridPos(65, 12), out err), "己方大本营区域应可放:" + err);

        // ④ 战略前推:占领来的前线箭塔成为"最前己方建筑"(视野 24),原先够不着的路段变为可放
        sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(80, 45)); // 世界 (45.5,80.5),X 超过既有堡垒
        Assert.IsTrue(ToolsSystem.UseFort(sim, Side.Blue, new GridPos(80, 60), out err),
            "最前己方建筑(占领塔)视野内的路段应可放(占领前线→阵地随视野前推):" + err);
    }

    // ================= 轻装机动(8 张卡中此前零测试引用) =================

    [Test]
    public void T07_轻装机动_控区内真实移速翻倍()
    {
        sim = PrepareHeldSearch(HexId.LightMobility, Side.Blue);
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Blue, HexId.LightMobility));

        var near = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 104)); // 塔 footprint([98.5,102.5])外 1.5 格,避免出生被推出污染位移
        near.MoveSpeed = 1f; near.Hp = near.MaxHp = 400f;
        var far = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(60, 101));
        far.MoveSpeed = 1f; far.Hp = far.MaxHp = 400f;
        sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(80, 100)); // near 距塔心 4 米 ≤ 控区 10

        var anchorNear = near.PosF; var anchorFar = far.PosF;
        sim.SetMoveTarget(near.Id, new GridPos(80, 118));
        sim.SetMoveTarget(far.Id, new GridPos(60, 78));
        AdvanceOn(sim, 25); // 2.5 秒:near 移 5 米后仍在控区内(距塔 9 米)

        float dNear = SimVec2.Distance(anchorNear, near.PosF);
        float dFar = SimVec2.Distance(anchorFar, far.PosF);
        Assert.That(dFar, Is.InRange(2.2f, 2.8f), "对照单位应匀速移动(远离任何己方建筑)");
        Assert.That(dNear / dFar, Is.InRange(1.85f, 2.15f),
            "控区内移速应 ×2(实测 " + dNear.ToString("F2") + " vs " + dFar.ToString("F2") + ")");
    }

    // ================= 曲径通幽(8 张卡中此前零测试引用) =================

    [Test]
    public void T08_曲径通幽_塔不锁定非交战单位()
    {
        // 对照(无卡):RubbledBuildingTests.T1 已证塔会打 3 米内 Idle 单位,这里自带上限对照
        var control = NewSim(11);
        var cTower = control.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(80, 100));
        var cHero = control.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 97));
        cHero.MoveSpeed = 0f; cHero.Hp = cHero.MaxHp = 400f; // 距塔 3 米:塔射程 5 内、近战自身够不着塔
        AdvanceOn(control, 100);
        Assert.Less(cHero.Hp, 400f, "对照前提:无卡时塔必须攻击射程内非交战单位");

        // 有卡:同一摆位,红方曲径通幽激活 → 塔不得锁定非交战单位
        sim = PrepareHeldSearch(HexId.HiddenPath, Side.Red);
        var tower = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(80, 100));
        var hero = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(80, 97));
        hero.MoveSpeed = 0f; hero.Hp = hero.MaxHp = 400f;
        Assert.AreEqual(HexUseError.None, HexSystem.TryUseHeld(sim, Side.Red, HexId.HiddenPath));
        float hp0 = hero.Hp;
        AdvanceOn(sim, 100); // 10 秒
        Assert.AreEqual(hp0, hero.Hp, 0.001f, "曲径通幽期间非交战单位不应被塔攻击");
    }
}
