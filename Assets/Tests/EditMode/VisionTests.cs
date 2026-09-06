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
    public void 单位视野半径_近战6格_射手9格()
    {
        var blueMelee = MakeUnit(Side.Blue, HeroId.PanShi, 15, 113);
        var red = MakeUnit(Side.Red, HeroId.DuanFeng, 15, 119);
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "曼哈顿 6 格应在近战视野内");
        red.Pos = new GridPos(15, 120); sim.Step();
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "曼哈顿 7 格超出近战视野");

        var blueShooter = MakeUnit(Side.Blue, HeroId.ZhuiRi, 16, 113);
        red.Pos = new GridPos(15, 121); sim.Step();
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "曼哈顿 9 格应在射手视野内");
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
        var panShi = MakeUnit(Side.Blue, HeroId.PanShi, 15, 120); // 先放磐石(更靠前)
        var duanFeng = MakeUnit(Side.Blue, HeroId.DuanFeng, 15, 118); // 后放断峰 → 从未独处,鹰眼不闪光
        var red = MakeUnit(Side.Red, HeroId.PanShi, 15, 127);
        Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id),
            "磐石(Col12)在前,断峰视野被压到 6;磐石覆盖到 Col18,故 Col19 不可见");

        panShi.State = EntityState.Dead; sim.Step(); // 移除磐石 → 鹰眼激活
        Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, red.Id), "鹰眼激活后断峰视野 10,Col19 可见");
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
        Assert.IsTrue(VisionSystem.IsBuildingKnown(sim, Side.Red, tower.Id), "红近战距塔 2 格,应揭示蓝塔");
        Assert.AreEqual(50f, VisionSystem.LastKnownHp(sim, Side.Red, tower.Id).Value, 0.01f, "应记录当前血量 50");

        sim.SetMoveTarget(red.Id, new GridPos(15, 136));
        Run(50);
        Assert.IsTrue(VisionSystem.IsBuildingKnown(sim, Side.Red, tower.Id), "过期后据点冻结,仍为已知");
        Assert.AreEqual(50f, VisionSystem.LastKnownHp(sim, Side.Red, tower.Id).Value, 0.01f, "冻结在最后时刻血量");

        tower.Hp = 40f;
        sim.Step();
        Assert.AreEqual(50f, VisionSystem.LastKnownHp(sim, Side.Red, tower.Id).Value, 0.01f, "无红方视野,冻结值不随实际血量变化");
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
