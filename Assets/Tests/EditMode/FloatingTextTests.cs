using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

public class FloatingTextTests
{
    private static GameConfig _cfg;
    private SimCore sim;
    private FloatingTextModel _model;

    [SetUp]
    public void SetUp()
    {
        VisionSystem.Reset();
        _cfg ??= GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(1));
        sim.Game = _cfg;
        _model = new FloatingTextModel();
    }

    private Entity SpawnUnit(Side side, int row, int col)
    {
        return sim.SpawnEntity(side, EntityKind.Unit, new GridPos { Row = row, Col = col });
    }

    [Test]
    public void T1_OwnUnit_TryAdd()
    {
        var u = SpawnUnit(Side.Blue, 15, 60);
        Assert.IsTrue(_model.TryAdd(sim, Side.Blue, u.Id, 12.4f));
        Assert.AreEqual(1, _model.Items.Count);
        Assert.AreEqual("-12", _model.Items[0].Text);
        Assert.AreEqual(0, _model.Items[0].ColorKind);
    }

    [Test]
    public void T2_Rounding()
    {
        var u = SpawnUnit(Side.Blue, 15, 60);
        Assert.IsTrue(_model.TryAdd(sim, Side.Blue, u.Id, 12.5f));
        Assert.AreEqual("-13", _model.Items[0].Text);
        _model.Items.Clear();
        Assert.IsTrue(_model.TryAdd(sim, Side.Blue, u.Id, 0.4f));
        Assert.AreEqual("-0", _model.Items[0].Text); // "-"+Round 规则一致
    }

    [Test]
    public void T3_EnemyInvisible_TryAddFalse()
    {
        var u = SpawnUnit(Side.Red, 15, 200); // 远离蓝视野
        Assert.IsFalse(_model.TryAdd(sim, Side.Blue, u.Id, 10f));
        Assert.AreEqual(0, _model.Items.Count);
    }

    [Test]
    public void T4_EnemyVisible_TryAddTrue()
    {
        SpawnUnit(Side.Blue, 15, 60);
        var r = SpawnUnit(Side.Red, 15, 62); // 2 米内
        VisionSystem.Tick(sim, 0.1f);
        Assert.IsTrue(_model.TryAdd(sim, Side.Blue, r.Id, 10f));
        Assert.AreEqual(1, _model.Items[0].ColorKind);
    }

    [Test]
    public void T5_BuildingFogGate()
    {
        var tower = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos { Row = 15, Col = 220 });
        Assert.IsFalse(_model.TryAdd(sim, Side.Blue, tower.Id, 10f)); // 未知建筑
        sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos { Row = 15, Col = 218 }); // 紧贴
        VisionSystem.Tick(sim, 0.1f);
        Assert.IsTrue(_model.TryAdd(sim, Side.Blue, tower.Id, 10f));
    }

    [Test]
    public void T6_Advance()
    {
        var u = SpawnUnit(Side.Blue, 15, 60);
        _model.TryAdd(sim, Side.Blue, u.Id, 10f);
        _model.Advance(0.7f);
        Assert.AreEqual(1, _model.Items.Count); // Age==Life 边界未超
        _model.Advance(0.31f);
        Assert.AreEqual(0, _model.Items.Count);
    }

    [Test]
    public void T7_MaxActive()
    {
        var u = SpawnUnit(Side.Blue, 15, 60);
        int added = 0;
        for (int i = 0; i < 45; i++)
            if (_model.TryAdd(sim, Side.Blue, u.Id, i)) added++;
        Assert.AreEqual(45, added);
        Assert.AreEqual(40, _model.Items.Count);
        Assert.AreEqual(u.Id, _model.Items[0].EntityId); // 最旧 5 条被挤掉,Items[0] 为第 6 次添加
    }

    [Test]
    public void T8_DeadEntity()
    {
        var u = SpawnUnit(Side.Blue, 15, 60);
        u.State = EntityState.Dead;
        Assert.IsFalse(_model.TryAdd(sim, Side.Blue, u.Id, 10f));
    }

    [Test]
    public void T9_ScaleAndRise()
    {
        Assert.AreEqual(1f, FloatingTextModel.ScaleOf(0f));
        Assert.AreEqual(0.5f, FloatingTextModel.ScaleOf(0.85f), 1e-4f);
        Assert.AreEqual(0f, FloatingTextModel.ScaleOf(1f), 1e-4f);
        Assert.AreEqual(0.6f, FloatingTextModel.RiseOffset(0.5f), 1e-4f);
    }
}
