using System.Collections.Generic;
using IronCurtain.Sim;
using IronCurtain.Tests;
using IronCurtain.View;
using NUnit.Framework;

/// <summary>M6.4a 对局时钟+加时+积分结算 EditMode 测试。</summary>
public class MatchClockTests
{
    private SimCore sim;
    private GameConfig _cfg;
    private List<SimEvent> _events;

    [SetUp]
    public void SetUp()
    {
        VisionSystem.Reset();
        var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>("gameconfig");
        _cfg = GameConfigParser.FromJson(ta.text);
        sim = new SimCore(new SimConfig(), new SeededRng(1));
        sim.Game = _cfg;
        _events = new List<SimEvent>();
        sim.AddEventListener(ev => _events.Add(ev));
    }

    [TearDown]
    public void TearDown()
    {
        sim.ClearEventListeners();
    }

    private int CountOf(SimEventType t)
    {
        int n = 0;
        foreach (var ev in _events) if (ev.Type == t) n++;
        return n;
    }

    [Test]
    public void T1_InitialState()
    {
        Assert.AreEqual(MatchPhase.Normal, sim.Phase);
        Assert.AreEqual(8, sim.PopulationCapOf(Side.Blue));   // 2026-09-13 上限 8
        Assert.AreEqual("10:00", TopBarModel.FormatCountdown(sim));
        Assert.AreEqual("对战中", TopBarModel.PhaseText(sim));
    }

    [Test]
    public void T2_OvertimeStartsAt600s()
    {
        Assert.IsTrue(TestUtil.RunUntil(sim, () => sim.Phase == MatchPhase.Overtime, 6050));
        Assert.AreEqual(1, CountOf(SimEventType.OvertimeStarted));
        float el = MatchClockSystem.ElapsedSeconds(sim);
        Assert.GreaterOrEqual(el, 600f);
        Assert.Less(el, 605f);
        Assert.AreEqual("02:00", TopBarModel.FormatCountdown(sim));
        Assert.AreEqual(9, sim.PopulationCapOf(Side.Blue));   // 加时 8+1
    }

    [Test]
    public void T3_OvertimeEpInterval60s()
    {
        sim.SpawnBuilding(Side.Blue, BuildingId.ResourcePoint, new GridPos(15, 100));
        Assert.IsTrue(TestUtil.RunUntil(sim, () => sim.Phase == MatchPhase.Overtime, 6050));
        sim.SpendEp(Side.Blue, sim.EpOf(Side.Blue)); // 清空 EP
        long start = sim.Tick;
        Assert.IsTrue(TestUtil.RunUntil(sim, () => sim.EpOf(Side.Blue) >= 1f, 650));
        Assert.LessOrEqual(sim.Tick - start, 615); // 60s+边界;常规 90s 不会在此窗内回满
    }

    [Test]
    public void T4_ReinforceFasterInOvertime()
    {
        Assert.IsTrue(TestUtil.RunUntil(sim, () => sim.Phase == MatchPhase.Overtime, 6050));
        sim.ReinforceAccum[Side.Blue] = 0f;
        sim.ReinforcePool[Side.Blue] = 0;
        Assert.IsTrue(TestUtil.RunUntil(sim, () => ReinforceSystem.PoolOf(sim, Side.Blue) >= 1, out int usedOt, 25));
        Assert.LessOrEqual(usedOt, 19); // 2/1.1≈1.82s≈18.2tick

        // 对照:Normal 期(独立新 sim;原 sim 已过 600s,时钟会把手动设回的 Normal 立即推回 Overtime)
        var sim2 = new SimCore(new SimConfig(), new SeededRng(2));
        sim2.Game = _cfg;
        sim2.ReinforceAccum[Side.Blue] = 0f;
        sim2.ReinforcePool[Side.Blue] = 0;
        Assert.IsTrue(TestUtil.RunUntil(sim2, () => ReinforceSystem.PoolOf(sim2, Side.Blue) >= 1, out int usedNormal, 25));
        Assert.AreEqual(20, usedNormal);
    }

    [Test]
    public void T5_ScoreFormula()
    {
        var bBase = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 20));
        bBase.Hp = 100;
        sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 30));
        sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 31));
        sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 60));
        var rBase = sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 260));
        rBase.Hp = 103;

        MatchClockSystem.ScoreOf(sim, Side.Blue, out int bBasePt, out int bHeroPt, out int bEpPt, out int bBldPt, out int bTotal);
        Assert.AreEqual(500, bBasePt);
        Assert.AreEqual(6, bHeroPt);
        Assert.AreEqual(2, bBldPt);
        Assert.AreEqual(508 + bEpPt, bTotal); // 500+6+2(契约原稿 506 为编排者算术笔误)
        Assert.AreEqual((int)sim.EpOf(Side.Blue) * 2, bEpPt);

        MatchClockSystem.ScoreOf(sim, Side.Red, out int rBasePt, out int rHeroPt, out int rEpPt, out int rBldPt, out int rTotal);
        Assert.AreEqual(515, rBasePt);
        Assert.AreEqual(0, rHeroPt);
        Assert.AreEqual(1, rBldPt);
    }

    [Test]
    public void T6_ScoreWinnerByBase()
    {
        var bBase = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 20));
        bBase.Hp = 100;
        sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 30));
        sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 31));
        sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 60));
        var rBase = sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 260));
        rBase.Hp = 103;

        Assert.IsTrue(TestUtil.RunUntil(sim, () => sim.Phase == MatchPhase.Ended, 7250));
        Assert.AreEqual(Side.Red, sim.Winner);
        Assert.AreEqual(1, CountOf(SimEventType.MatchEnded));
        Assert.AreEqual((int)Side.Red, _events.Find(ev => ev.Type == SimEventType.MatchEnded).SubjectId);
        Assert.AreEqual("战斗结束", TopBarModel.PhaseText(sim));
    }

    [Test]
    public void T7_TieBreakByPopulation()
    {
        // 真打平:蓝 103 血基地+0 将 = 515+0+1 = 516;红 100 血基地+5 将 = 500+15+1 = 516(总分同,比人口)
        var bBase = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 20));
        bBase.Hp = 103;
        var rBase = sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 260));
        rBase.Hp = 100;
        for (int i = 0; i < 5; i++) sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(14, 250 + i));

        Assert.IsTrue(TestUtil.RunUntil(sim, () => sim.Phase == MatchPhase.Ended, 7250));
        Assert.AreEqual(Side.Red, sim.Winner); // 人口 5>0
    }

    [Test]
    public void T8_TieBreakByLostGenerals()
    {
        var bBase = sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 20));
        bBase.Hp = 100;
        sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 30));
        var rBase = sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 260));
        rBase.Hp = 100;
        sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(14, 250));
        var deadRed = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(13, 250));
        deadRed.State = EntityState.Dead; // 留场死,验证扫描计数路径

        sim.Step();
        Assert.AreEqual(1, sim.LostGenerals[Side.Red]);

        Assert.IsTrue(TestUtil.RunUntil(sim, () => sim.Phase == MatchPhase.Ended, 7250));
        Assert.AreEqual(Side.Blue, sim.Winner); // 阵亡少者胜(人口均 1、总分同)
    }

    [Test]
    public void T9_DestroyWinEndsImmediately()
    {
        sim.SetWinner(Side.Blue);
        sim.Step();
        Assert.AreEqual(MatchPhase.Ended, sim.Phase);
        Assert.AreEqual(1, CountOf(SimEventType.MatchEnded));
        Assert.AreEqual(0, _events.Find(ev => ev.Type == SimEventType.MatchEnded).SubjectId);
        for (int i = 0; i < 6100; i++) sim.Step();
        Assert.AreEqual(MatchPhase.Ended, sim.Phase);
        Assert.AreEqual(0, CountOf(SimEventType.OvertimeStarted));
    }

    [Test]
    public void T10_FullDraw()
    {
        sim.SpawnBuilding(Side.Blue, BuildingId.Base, new GridPos(15, 20));
        sim.SpawnBuilding(Side.Red, BuildingId.Base, new GridPos(15, 260));
        sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(14, 30));
        sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(14, 250));

        Assert.IsTrue(TestUtil.RunUntil(sim, () => sim.Phase == MatchPhase.Ended, 7250));
        Assert.IsFalse(sim.Winner.HasValue);
        Assert.AreEqual(-1, _events.Find(ev => ev.Type == SimEventType.MatchEnded).SubjectId);
        Assert.AreEqual("平局", TopBarModel.PhaseText(sim));
        Assert.AreEqual("00:00", TopBarModel.FormatCountdown(sim));
    }

    [Test]
    public void T11_ManualPhaseTexts()
    {
        sim.Phase = MatchPhase.Overtime;
        Assert.AreEqual("加时赛", TopBarModel.PhaseText(sim));
        Assert.AreEqual("12:00", TopBarModel.FormatCountdown(sim));
        sim.Phase = MatchPhase.Ended;
        Assert.AreEqual("00:00", TopBarModel.FormatCountdown(sim));
    }
}
