using System.Collections.Generic;
using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TopBarTests
{
    private static GameConfig _cfg;
    private SimCore sim;

    [SetUp]
    public void SetUp()
    {
        VisionSystem.Reset(); // 静态快照跨测试污染,既有坑
        _cfg ??= GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(1));
        sim.Game = _cfg;
    }

    private Entity SpawnBuilding(Side side, BuildingId type, int row, int col)
    {
        return sim.SpawnBuilding(side, type, new GridPos { Row = row, Col = col });
    }

    [Test]
    public void T1_FormatClock()
    {
        Assert.AreEqual("00:00", TopBarModel.FormatClock(0, 10));
        Assert.AreEqual("01:05", TopBarModel.FormatClock(10L * 65, 10));
        Assert.AreEqual("59:59", TopBarModel.FormatClock(10L * 3599, 10));
        Assert.AreEqual("00:00", TopBarModel.FormatClock(15, 0)); // tickRate 0 防除零
    }

    [Test]
    public void T2_FindBase()
    {
        var blueBase = SpawnBuilding(Side.Blue, BuildingId.Base, 15, 20);
        SpawnBuilding(Side.Blue, BuildingId.Tower, 15, 60);
        SpawnBuilding(Side.Blue, BuildingId.Barracks, 18, 30);
        var redBase = SpawnBuilding(Side.Red, BuildingId.Base, 15, 260);
        Assert.AreEqual(blueBase.Id, TopBarModel.FindBase(sim, Side.Blue).Id);
        Assert.AreEqual(redBase.Id, TopBarModel.FindBase(sim, Side.Red).Id);
        // 无中立 Base
        Assert.IsNull(TopBarModel.FindBase(sim, Side.Neutral));
    }

    [Test]
    public void T2b_FindBase_Missing_ReturnsNull()
    {
        SpawnBuilding(Side.Blue, BuildingId.Base, 15, 20);
        Assert.IsNull(TopBarModel.FindBase(sim, Side.Red)); // 无红 Base
    }

    [Test]
    public void T3_PopText()
    {
        Assert.AreEqual("将 0/" + sim.PopulationCapOf(Side.Blue), TopBarModel.PopText(sim, Side.Blue));
        sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos { Row = 15, Col = 40 });
        sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos { Row = 16, Col = 42 });
        Assert.AreEqual("将 2/" + sim.PopulationCapOf(Side.Blue), TopBarModel.PopText(sim, Side.Blue));
    }

    [Test]
    public void T4_HpText()
    {
        Assert.AreEqual("1234/2000", TopBarModel.HpText(1234.4f, 2000));
        Assert.AreEqual("0/2000", TopBarModel.HpText(0, 2000));
    }

    [Test]
    public void T5_EnemyBaseSnapshot_Unknown()
    {
        SpawnBuilding(Side.Blue, BuildingId.Base, 15, 20);
        SpawnBuilding(Side.Red, BuildingId.Base, 15, 260); // 远离蓝视野
        Assert.IsNull(TopBarModel.EnemyBaseSnapshot(sim, Side.Blue, Side.Red));
    }

    [Test]
    public void T5b_EnemyBaseSnapshot_Known()
    {
        SpawnBuilding(Side.Blue, BuildingId.Base, 15, 20);
        var redBase = SpawnBuilding(Side.Red, BuildingId.Base, 15, 260);
        redBase.Hp = 777f;
        sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos { Row = 15, Col = 258 }); // 紧贴红基地
        VisionSystem.Tick(sim, 0.1f);
        var s = TopBarModel.EnemyBaseSnapshot(sim, Side.Blue, Side.Red);
        Assert.IsNotNull(s);
        Assert.AreEqual(777f, s.Hp, 1f);
    }
}
