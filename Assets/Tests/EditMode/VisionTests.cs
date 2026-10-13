using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

public class VisionTests
{
    static GameConfig _cfg;
    SimCore sim;

    [SetUp]
    public void SetUp()
    {
        if (_cfg == null)
            _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(1));
        sim.Game = _cfg;
        VisionSystem.Reset();
    }

    Entity MakeUnit(Side side, HeroId hero, int row, int col)
    {
        var c = sim.Game.Heroes[(int)hero];
        var e = sim.SpawnEntity(side, EntityKind.Unit, new GridPos(row, col));
        e.Hero = hero; e.MaxHp = c.MaxHp; e.Hp = c.MaxHp; e.Attack = c.Attack;
        e.AttackIntervalSec = (float)c.AttackIntervalSec; e.Range = c.Range; e.MoveSpeed = (float)c.MoveSpeed;
        if (c.Shield > 0) { e.Shield = c.Shield; e.ShieldMax = c.Shield; }
        sim.Step();
        return e;
    }

    Entity MakeBuilding(Side side, BuildingId kind, int row, int col)
    {
        var e = sim.SpawnBuilding(side, kind, new GridPos(row, col));
        sim.Step();
        return e;
    }

    void Run(int ticks) { for (int i = 0; i < ticks; i++) sim.Step(); }

    [Test]
    public void 单位视野半径_近战8格_射手9格()
    {
        // 2026-09-14 制作人口径:近战视野 8(原 6)、射手 9 不变,射手射程收到 8(见 gameconfig)。
        var blueMelee = MakeUnit(Side.Blue, HeroId.PanShi, 15, 113);
        var red = MakeUnit(Side.Red, HeroId.DuanFeng, 15, 121);
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "欧氏 8 格应在近战视野内");
        red.Pos = new GridPos(15, 122); sim.Step();
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "欧氏 9 格超出近战视野");

        // 射手主将 9:把近战单位挪远(先腾格,否则出生点过近),只留射手判定
        blueMelee.PosF = SimVec2.FromGrid(new GridPos(10, 10));
        blueMelee.Pos = new GridPos(10, 10);
        sim.Step();
        var blueShooter = MakeUnit(Side.Blue, HeroId.ZhuiRi, 15, 113);
        red.Pos = new GridPos(15, 122); sim.Step();
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "欧氏 9 格应在射手视野内");
        red.Pos = new GridPos(15, 123); sim.Step();
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "欧氏 10 格超出射手视野");
    }

    [Test]
    public void 建筑视野_塔24格()
    {
        // 2026-09-06 用户拍板:建筑视野在原值上扩大一倍(塔 12→24)
        MakeBuilding(Side.Blue, BuildingId.Tower, 15, 114);
        var inR = MakeUnit(Side.Red, HeroId.PanShi, 15, 138);   // 距塔 24,应可见
        var outR = MakeUnit(Side.Red, HeroId.PanShi, 16, 139);  // 欧氏距 25.02,应不可见
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, inR.Id), "距塔 24 格应被塔视野覆盖");
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, outR.Id), "距塔 25 格超出塔视野");
    }

    [Test]
    public void 建筑视野_大本营40格()
    {
        // 2026-09-06 用户拍板:大本营 20→40
        MakeBuilding(Side.Blue, BuildingId.Base, 15, 108);
        var inR = MakeUnit(Side.Red, HeroId.PanShi, 15, 148);   // 距 40,应可见
        var outR = MakeUnit(Side.Red, HeroId.PanShi, 16, 149);  // 欧氏距 41.01,应不可见
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, inR.Id), "距大本营 40 格应被覆盖");
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, outR.Id), "距大本营 41 格超出视野");
    }

    [Test]
    public void 建筑视野_兵营20格()
    {
        // 2026-09-06 用户拍板:其余建筑 10→20
        MakeBuilding(Side.Blue, BuildingId.Barracks, 15, 114);
        var inR = MakeUnit(Side.Red, HeroId.PanShi, 15, 134);   // 距 20,应可见
        var outR = MakeUnit(Side.Red, HeroId.PanShi, 16, 135);  // 欧氏距 21.02,应不可见
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, inR.Id), "距兵营 20 格应被覆盖");
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, outR.Id), "距兵营 21 格超出视野");
    }

    [Test]
    public void 鹰眼_断峰为己方最靠前野战单位时视野10格()
    {
        // 2026-09-14 制作人澄清:「断峰 10 是**专属被动**」→ 保留(原按"近战统一 8"删掉过,已还原)。
        // 判据分两段,规避"谁在最前"的歧义:①只有断峰在场(它就是最靠前)→ 视野 10;
        // ②在它前方补一名磐石 → 断峰不再是"己方最靠前野战单位" → 视野回落到近战通用值 8。
        var duanFeng = MakeUnit(Side.Blue, HeroId.DuanFeng, 15, 118);
        var red = MakeUnit(Side.Red, HeroId.PanShi, 15, 128);   // 距断峰 10(欧氏)
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id),
            "断峰为最靠前野战单位 → 鹰眼 10:10 格应可见");

        var panShi = MakeUnit(Side.Blue, HeroId.PanShi, 15, 119);   // 站到断峰前方
        red.PosF = SimVec2.FromGrid(new GridPos(20, 128));           // 换行,避免旧格标记
        red.Pos = new GridPos(20, 128);
        for (int i = 0; i < 35; i++) sim.Step();                     // 视野保鲜 3 秒过期
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id),
            "磐石在最前 → 断峰视野回落 8,10 格外不可见(磐石 8 也够不到)");
        Assert.IsNotNull(panShi);
    }

    [Test]
    public void 精灵扫掠_从大本营出发揭示整列含驻军数()
    {
        MakeBuilding(Side.Blue, BuildingId.Base, 15, 108);
        var tower = MakeBuilding(Side.Red, BuildingId.Tower, 15, 133);
        for (int i = 0; i < 3; i++)
        {
            var c = sim.Game.Heroes[(int)HeroId.PanShi];
            var u = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(i, 24));
            u.Hero = HeroId.PanShi; u.Hp = u.MaxHp = c.MaxHp;
            u.GarrisonBuildingId = tower.Id;
            tower.GarrisonFormal.Add(u.Id);
        }
        VisionSystem.Reset();
        Run(300); // 首发精灵在第 30 秒出发
        Run(50); // 两段速度(己方区 15 格/s,后续 6 格/s):50 tick ≈ 6.7 格 + 30 格后段…不足,补足行程
        Run(30); // 至 380 tick:后段 6 格/s × 80 tick = 48 格 ≥ 25 列
        Assert.IsTrue(VisionSystem.IsBuildingKnown(sim, Side.Blue, tower.Id), "精灵应扫过红塔所在列");
        Assert.AreEqual(3, VisionSystem.LastKnownGarrison(sim, Side.Blue, tower.Id).Value, "只知驻军人数(3),不知构成");
    }

    [Test]
    public void 信息3秒过期_露天军队走出视野后消失()
    {
        MakeUnit(Side.Blue, HeroId.PanShi, 15, 113);
        var red = MakeUnit(Side.Red, HeroId.PanShi, 15, 116);
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "相邻(3 格)应在蓝近战视野内");
        sim.SetMoveTarget(red.Id, new GridPos(15, 136));
        Run(40);
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "走出视野且信息超过 30 tick 过期,露天军队直接消失");
    }

    [Test]
    public void 据点信息冻结_离开视野后快照不再变化()
    {
        var tower = MakeBuilding(Side.Blue, BuildingId.Tower, 15, 114);
        tower.Hp = 50f;
        var red = MakeUnit(Side.Red, HeroId.PanShi, 15, 116);
        sim.Step();
        // 2026-09-11 制作人裁决:敌军贴建筑即时占领 → 红方贴塔 2 格,塔已易主为红方
        Assert.AreEqual(Side.Red, tower.Side, "贴身即占领(新规则)");
        Assert.IsTrue(VisionSystem.IsBuildingKnown(sim, Side.Red, tower.Id), "红方对(已占领的)塔持续已知");

        sim.SetMoveTarget(red.Id, new GridPos(15, 136));
        Run(50);
        Assert.IsTrue(VisionSystem.IsBuildingKnown(sim, Side.Red, tower.Id), "过期后据点冻结,仍为已知");

        tower.Hp = 40f;
        sim.Step();
        // 塔已是红方自家:自家建筑始终已知,快照跟随实际值(即时占领后的语义)
        Assert.AreEqual(40f, VisionSystem.LastKnownHp(sim, Side.Red, tower.Id).Value, 0.01f, "自家建筑快照随实值");
    }

    [Test]
    public void 对称性_双方视野独立且同源()
    {
        var blue = MakeUnit(Side.Blue, HeroId.PanShi, 15, 113);
        var red = MakeUnit(Side.Red, HeroId.PanShi, 15, 123);
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Red, blue.Id), "相距 10 格,红看蓝不可见");
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "相距 10 格,蓝看红不可见");
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, blue.Id), "蓝看自己(距离 0)可见");
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Red, red.Id), "红看自己(距离 0)可见");
    }

    [Test]
    public void 露天军队无快照_消失即无信息()
    {
        MakeUnit(Side.Blue, HeroId.PanShi, 15, 113);
        var red = MakeUnit(Side.Red, HeroId.PanShi, 15, 116);
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "进入蓝视野时可见");
        red.Pos = new GridPos(15, 136); sim.Step();
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "离开视野且过期后消失");
        Assert.IsNull(VisionSystem.LastKnownHp(sim, Side.Blue, red.Id), "单位无快照机制,LastKnownHp 对单位 id 返回 null");
        Assert.IsNull(VisionSystem.LastKnownGarrison(sim, Side.Blue, red.Id), "单位无快照机制,LastKnownGarrison 对单位 id 返回 null");
        Assert.IsFalse(VisionSystem.IsBuildingKnown(sim, Side.Blue, red.Id), "单位 id 不是已知据点");
    }
}
