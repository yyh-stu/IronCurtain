using NUnit.Framework;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEngine;

public class MinimapFogTests
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

    [TearDown]
    public void TearDown()
    {
        AiSystem.Reset(); VisionSystem.Reset(); HexSystem.Reset(); SiegeSystem.Reset();
    }

    Entity MakeUnit(Side side, HeroId hero, int row, int col)
    {
        var e = sim.SpawnEntity(side, EntityKind.Unit, new GridPos(row, col));
        e.Hero = hero;
        e.MaxHp = 100f; e.Hp = 100f;
        e.Attack = 10f; e.AttackIntervalSec = 1f; e.Range = 10f; e.MoveSpeed = 5f;
        sim.Step();
        return e;
    }

    Entity MakeBuilding(Side side, BuildingId kind, int row, int col)
    {
        var b = sim.SpawnBuilding(side, kind, new GridPos(row, col));
        sim.Step();
        return b;
    }

    void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++) sim.Step();
    }

    [Test]
    public void 镜头焦点钳制_边界与内部()
    {
        Assert.AreEqual(6f, MinimapLayout.ClampFocusX(-5f));
        Assert.AreEqual(280f, MinimapLayout.ClampFocusX(999f));
        Assert.AreEqual(150f, MinimapLayout.ClampFocusX(150f));
        Assert.AreEqual(6f, MinimapLayout.ClampFocusY(-5f));
        Assert.AreEqual(24f, MinimapLayout.ClampFocusY(999f));
        Assert.AreEqual(15f, MinimapLayout.ClampFocusY(15f));
    }

    [Test]
    public void 小地图映射_往返一致()
    {
        float w, h;
        MinimapLayout.MiniSize(440f, 90f, out w, out h);
        var pts = new[] { new float[] { 0f, 0f }, new float[] { 286f, 30f }, new float[] { 143f, 15f }, new float[] { 40f, 22f } };
        foreach (var p in pts)
        {
            float mx, my, cx, cy;
            MinimapLayout.WorldToMini(p[0], p[1], w, h, out mx, out my);
            MinimapLayout.MiniToWorld(mx, my, w, h, out cx, out cy);
            Assert.Less(Mathf.Abs(cx - p[0]), 0.01f);
            Assert.Less(Mathf.Abs(cy - p[1]), 0.01f);
        }
    }

    [Test]
    public void 小地图点击_转世界坐标边界()
    {
        float w, h;
        MinimapLayout.MiniSize(440f, 90f, out w, out h);
        float cx, cy;
        MinimapLayout.MiniToWorld(0f, 0f, w, h, out cx, out cy);
        Assert.Less(Mathf.Abs(cx), 0.01f);
        Assert.Less(Mathf.Abs(cy), 0.01f);
        MinimapLayout.MiniToWorld(w, h, w, h, out cx, out cy);
        Assert.Less(Mathf.Abs(cx - LaneGrid.Cols), 0.01f);
        Assert.Less(Mathf.Abs(cy - LaneGrid.Rows), 0.01f);
    }

    [Test]
    public void 雾规则_己方单位恒可见()
    {
        var u = MakeUnit(Side.Blue, HeroId.PanShi, 15, 250); // 远离一切视野来源,己方仍恒可见
        Assert.IsTrue(FogRules.UnitVisible(sim, Side.Blue, u));
    }

    [Test]
    public void 雾规则_敌单位进出视野()
    {
        MakeUnit(Side.Blue, HeroId.PanShi, 15, 113);
        var red = MakeUnit(Side.Red, HeroId.PanShi, 15, 119);
        Assert.IsTrue(FogRules.UnitVisible(sim, Side.Blue, red));
        red.Pos = new GridPos(15, 127);
        red.PosF = new SimVec2(127f, 15f);
        sim.Step();
        Assert.IsFalse(FogRules.UnitVisible(sim, Side.Blue, red));
    }

    [Test]
    public void 雾规则_敌建筑从未见过_Unknown()
    {
        MakeBuilding(Side.Blue, BuildingId.Tower, 15, 114);
        var barracks = MakeBuilding(Side.Red, BuildingId.Barracks, 15, 200);
        Assert.AreEqual(BuildingFogState.Unknown, FogRules.BuildingState(sim, Side.Blue, barracks));
    }

    [Test]
    public void 雾规则_敌建筑失视后_Stale且快照冻结()
    {
        var blue = MakeUnit(Side.Blue, HeroId.PanShi, 15, 113);
        var barracks = MakeBuilding(Side.Red, BuildingId.Barracks, 15, 119);
        Assert.AreEqual(BuildingFogState.Fresh, FogRules.BuildingState(sim, Side.Blue, barracks));
        blue.Pos = new GridPos(15, 20);
        blue.PosF = new SimVec2(20f, 15f);
        Run(35);
        Assert.AreEqual(BuildingFogState.Stale, FogRules.BuildingState(sim, Side.Blue, barracks));
        var snap = VisionSystem.LastKnownBuilding(sim, Side.Blue, barracks.Id);
        Assert.IsNotNull(snap);
        Assert.AreEqual(barracks.MaxHp, snap.Hp);
        Assert.AreEqual(0, snap.GarrisonCount);
    }

    [Test]
    public void 快照API_未知建筑返回null()
    {
        var red = MakeBuilding(Side.Red, BuildingId.Barracks, 15, 200);
        Assert.IsNull(VisionSystem.LastKnownBuilding(sim, Side.Blue, red.Id));
    }

    [Test]
    public void 雾Alpha_己方视野内0_远处160()
    {
        MakeBuilding(Side.Blue, BuildingId.Base, 15, 108);
        sim.Step();
        Assert.AreEqual(0, FogRules.FogAlpha(sim, Side.Blue, 15, 110));
        Assert.AreEqual(FogRules.FogAlphaValue, FogRules.FogAlpha(sim, Side.Blue, 15, 260));
    }
}
