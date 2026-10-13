using System.Collections.Generic;
using NUnit.Framework;
using IronCurtain.Sim;
using IronCurtain.Tests;
using UnityEngine;

/// <summary>
/// 2026-09-15 制作人:"**还是不攻击建筑,全在发呆**"(附图:近战主将站在敌方箭塔旁不动)。
///
/// 探针(已删)实测的两条真因,各配一组回归用例:
///   ① **近战打不到建筑**:主将集结点=`碰撞半径+5`(箭塔=7.4 米)、士兵前排落点=3.00 米,
///      而旧判定要"距塔心 ≤ 2.80 米" → 到位即 `State=Attacking` 然后**一点血都打不掉**
///      (近战完全拆不动建筑,只有射手能拆,所以一直没人发现)。另:旧判定是按**中心距**,
///      矩形对角线上贴到墙角反而是 2.83 米 → 也打不到。
///   ② **摧毁令被自己人顺手作废**:士兵是编队跟随、先贴上去的那批,他们此刻还没拿到攻城标记
///      (`TargetBuildingId == 0`),于是对一座**无守军**的敌方建筑正好符合"接触即占领" →
///      **被自己人翻成己方** → `Capture` 清空 Intent → 全军摧毁令一起作废 → 全体发呆。
///
/// 本套用例一律走**真实下令路径**(`TryIssueRegimentOrder`),不用"把单位摆到贴脸再原地不动"
/// 那种走法 —— 旧 `DestroyTests` 全是后者,所以整条"走过去再打"的链路从来没被覆盖到。
/// </summary>
public class BuildingDestroyReachTests
{
    private static GameConfig _cfg;
    private SimCore sim;

    [SetUp]
    public void SetUp()
    {
        if (_cfg == null)
            _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(7));
        sim.Game = _cfg;
    }

    private void Step(int n) { for (int i = 0; i < n; i++) sim.Step(); }

    private Entity SpawnTower(Side side, GridPos pos)
    {
        var b = sim.SpawnBuilding(side, BuildingId.Tower, pos);
        b.MaxHp = 200f; b.Hp = 200f;
        return b;
    }

    private static int SoldierCount(SimCore sim, int generalId)
    {
        int n = 0;
        foreach (var e in sim.Entities.Values) if (e.IsSoldier && e.GeneralId == generalId) n++;
        return n;
    }

    [Test]
    public void T1_近战主将从远处领摧毁令_必须走近并真的拆掉()
    {
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        var g = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 88));
        var res = SiegeSystem.TryIssueRegimentOrder(sim, new List<int> { g.Id }, tower.Id, Side.Red, SiegeIntent.Destroy);
        Assert.IsTrue(res.Success, "摧毁令应下达成功");

        Step(150);   // 15 秒:够走到墙脚并打掉几轮
        Assert.Less(tower.Hp, 200f, "近战主将必须真的对建筑造成伤害(旧口径:停在 7 米外一点血不掉)");
        Assert.IsTrue(TestUtil.RunUntil(sim, () => tower.Rubbled, 3000), "近战主将应能把箭塔拆成废墟");
        Assert.AreEqual(Side.Neutral, tower.Side, "废墟无阵营");
    }

    [Test]
    public void T2_近战士兵也必须能拆建筑_不能只有射手能拆()
    {
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        var g = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 88));
        Assert.Greater(SoldierCount(sim, g.Id), 0, "兵团应有士兵");
        var res = SiegeSystem.TryIssueRegimentOrder(sim, new List<int> { g.Id }, tower.Id, Side.Red, SiegeIntent.Destroy);
        Assert.IsTrue(res.Success, "摧毁令应下达成功");

        Step(200);   // 20 秒
        Assert.Less(tower.Hp, 200f, "近战士兵必须真的对建筑造成伤害(旧口径:12 个兵站一圈全发呆)");
        Assert.IsTrue(TestUtil.RunUntil(sim, () => tower.Rubbled, 3000), "整团应能把箭塔拆成废墟");
    }

    [Test]
    public void T3_射手从远处领摧毁令_射程内拆完()
    {
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        var g = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.ZhuiRi, new GridPos(80, 86));
        g.Hp = 500f; g.MaxHp = 500f;   // 扛塔与溅射
        var res = SiegeSystem.TryIssueRegimentOrder(sim, new List<int> { g.Id }, tower.Id, Side.Red, SiegeIntent.Destroy);
        Assert.IsTrue(res.Success, "摧毁令应下达成功");
        Assert.IsTrue(TestUtil.RunUntil(sim, () => tower.Rubbled, 3000), "射手应能在射程外把箭塔拆成废墟");
    }

    [Test]
    public void T4_摧毁令不会被自己人顺手占领作废()
    {
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        var g = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 92));
        var res = SiegeSystem.TryIssueRegimentOrder(sim, new List<int> { g.Id }, tower.Id, Side.Red, SiegeIntent.Destroy);
        Assert.IsTrue(res.Success, "摧毁令应下达成功");

        // 前 6 秒:兵团刚贴上去、士兵还没拿到自己的标记 —— 这正是"被自己人翻面"的窗口
        for (int i = 0; i < 60; i++)
        {
            sim.Step();
            if (tower.Rubbled) break;
            Assert.AreEqual(Side.Blue, tower.Side, "tick=" + sim.Tick + ":摧毁令生效期间,自己的单位不许把这座无守军建筑翻成己方");
            Assert.AreEqual(SiegeIntent.Destroy, tower.Intent, "tick=" + sim.Tick + ":摧毁意图不得被顺手占领清掉");
        }
        Assert.Less(tower.Hp, 200f, "窗口期内应已开始掉血");
    }

    [Test]
    public void T5_近战从矩形对角线墙角也要打得到()
    {
        // 旧判定按**中心距 ≤ 2.80** 而箭塔 2×2 的对角 ≈ 2.83 → 站在对角贴墙反而打不到。
        // 现在按**占地矩形距**,贴着外沿任意一侧都应掉血。
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        var g = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(78, 98));   // 对角方向
        var c = _cfg.Heroes[(int)HeroId.PanShi];
        g.Hero = HeroId.PanShi; g.MaxHp = c.MaxHp; g.Hp = c.MaxHp;
        g.Attack = c.Attack; g.AttackIntervalSec = (float)c.AttackIntervalSec;
        g.Range = c.Range; g.MoveSpeed = (float)c.MoveSpeed;
        if (c.Shield > 0) { g.Shield = c.Shield; g.ShieldMax = c.Shield; }
        g.TargetBuildingId = tower.Id;
        tower.Intent = SiegeIntent.Destroy;
        tower.IntentIssuer = Side.Red;
        sim.SetMoveTarget(g.Id, g.Pos);

        var edge = BuildingFootprint.ClosestPoint(tower, g.PosF);
        Assert.LessOrEqual(SimVec2.Distance(g.PosF, edge), SimCore.MeleeBuildingReach, "用例前提:该点应贴着占地矩形外沿");
        Step(30);
        Assert.Less(tower.Hp, 200f, "贴着矩形外沿(哪怕在对角)就应该打得到 —— 旧的中心距判定在这里会漏");
    }

    [Test]
    public void T6_废墟不回血()
    {
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        tower.Rubbled = true;
        tower.Side = Side.Neutral;
        tower.Hp = 0f;
        Step(200);   // 20 秒脱战
        Assert.AreEqual(0f, tower.Hp, 0.001f, "废墟不该按 4HP/s 回血(旧口径:0 → 80)");
    }

    [Test]
    public void T7_占领令不受本修正影响_仍然接触即占领()
    {
        // 反向保护:本轮只挡"**摧毁**令下令方"的顺手占领,不带摧毁令的常规接触占领照旧
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        var g = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 94));
        var res = SiegeSystem.TryIssueRegimentOrder(sim, new List<int> { g.Id }, tower.Id, Side.Red, SiegeIntent.Capture);
        Assert.IsTrue(res.Success, "占领令应下达成功");
        Assert.IsTrue(TestUtil.RunUntil(sim, () => tower.Side == Side.Red, 3000), "无守军建筑应被占下");
        Assert.AreEqual(Side.Neutral, tower.IntentIssuer, "占领完成后意图应清空");
    }

    [Test]
    public void T8_占领令的集结点仍在城外待命_只有摧毁令才贴墙()
    {
        // 口径边界:两种意图的集结点**故意不同** ——
        // 占领 = 城外"碰撞半径+5"待命(到位后放兵/进城),摧毁 = 贴到打得到的位置。
        // 这条是原来 `BuildingCommandModelTests` 里那条"集结点应贴近建筑外缘(碰撞半径+5)"的断言 —
        // 那句话本身没错,错的是它被套在了**摧毁令**上;这里把它挪到唯一正确的意图上,覆盖不丢。
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        var g = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(80, 88));
        var res = SiegeSystem.TryIssueRegimentOrder(sim, new List<int> { g.Id }, tower.Id, Side.Red, SiegeIntent.Capture);
        Assert.IsTrue(res.Success, "占领令应下达成功");

        var st = SimVec2.FromGrid(g.MoveTarget);
        float d = SimVec2.Distance(st, SimVec2.FromGrid(tower.Pos));
        float want = SimCore.BuildingCollisionRadius(tower) + 5f;
        Assert.LessOrEqual(Mathf.Abs(d - want), 1.5f, "占领令集结点应在城外待命(期望 ≈" + want.ToString("F1") + " 米,实际 " + d.ToString("F2") + ")");
        Assert.Greater(d, SimCore.MeleeBuildingReach + SimCore.BuildingCollisionRadius(tower),
            "占领令不该把主将直接送到墙脚(那是摧毁令的行为)");
    }
}
