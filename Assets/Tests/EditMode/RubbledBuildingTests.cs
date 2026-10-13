using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IronCurtain.Sim;
using UnityEngine;

/// <summary>
/// 2026-09-15 制作人:"**被摧毁的箭塔还是会攻击,有点抽象**"。
///
/// 探针(已删)实测:塔被拆成废墟(`Rubbled=true`、驻军清 0、`Side=Neutral`)之后,5 秒仍然打掉红单位 **20.2 血**。
/// 真因:`TowerCombatSystem.Tick` 遍历箭塔时**完全不看 `Rubbled`**。
/// 更歪的是:废墟 `Side` 被置成 `Neutral`,而它找目标的过滤是 `if (e.Side == tower.Side) continue;`
/// —— 中立塔拦不住任何一方 → **废墟会对着敌我双方开火**。废墟就是一堆石头,不该有任何主动行为。
///
/// 本套把"废墟不做事、活建筑照常做事"钉成回归:攻击 / 视野 / 城池主动攻击三处。
/// </summary>
public class RubbledBuildingTests
{
    private static GameConfig _cfg;
    private SimCore sim;

    [SetUp]
    public void SetUp()
    {
        if (_cfg == null)
            _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
        sim = new SimCore(new SimConfig(), new SeededRng(11));
        sim.Game = _cfg;
    }

    private void Step(int n) { for (int i = 0; i < n; i++) sim.Step(); }

    private Entity SpawnHero(Side side, HeroId heroId, GridPos pos)
    {
        var c = _cfg.Heroes[(int)heroId];
        var u = sim.SpawnEntity(side, EntityKind.Unit, pos);
        u.Hero = heroId; u.MaxHp = 400f; u.Hp = 400f;
        u.Attack = c.Attack; u.AttackIntervalSec = (float)c.AttackIntervalSec;
        u.Range = c.Range; u.MoveSpeed = (float)c.MoveSpeed;
        return u;
    }

    private Entity SpawnTower(Side side, GridPos pos)
    {
        var b = sim.SpawnBuilding(side, BuildingId.Tower, pos);
        return b;
    }

    private static void Rubble(SimCore sim, Entity b)
    {
        b.Rubbled = true;
        b.Side = Side.Neutral;
        b.Hp = 0f;
        GarrisonSystem.OnBuildingRubbled(sim, b);
    }

    [Test]
    public void T1_活着的箭塔照常攻击_反向保护()
    {
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        var red = SpawnHero(Side.Red, HeroId.ZhenYue, new GridPos(80, 97));   // 距塔心 3.0 ≤ 射程 5
        Step(60);
        Assert.Less(red.Hp, 400f, "活着的箭塔必须照常攻击射程内的敌军(本用例防止把塔一并改哑)");
        Assert.IsNotNull(tower);
    }

    [Test]
    public void T2_废墟箭塔不再攻击任何单位()
    {
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        Rubble(sim, tower);
        Assert.AreEqual(Side.Neutral, tower.Side, "废墟无阵营");
        Assert.AreEqual(0, tower.GarrisonFormal.Count, "废墟无驻军");

        var red = SpawnHero(Side.Red, HeroId.ZhenYue, new GridPos(80, 97));
        var blue = SpawnHero(Side.Blue, HeroId.ZhenYue, new GridPos(80, 103));
        float r0 = red.Hp, b0 = blue.Hp;
        Step(100);   // 10 秒
        Assert.AreEqual(r0, red.Hp, 0.001f, "废墟不该再攻击(旧口径:5 秒打掉红单位 20.2 血)");
        Assert.AreEqual(b0, blue.Hp, 0.001f, "废墟是中立的,更不该打己方/任何一方(旧口径下 Neutral 拦不住任何阵营)");
    }

    [Test]
    public void T3_废墟不提供视野()
    {
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        Step(2);
        Assert.IsTrue(VisionSystem.IsFresh(sim, Side.Blue, new GridPos(80, 90)),
            "用例前提:活着的箭塔(视野 24)应点亮周边格子");

        Rubble(sim, tower);
        // `IsFresh` 是"最后一次被看到的时间戳在新鲜窗口内",所以**已经看过的格子不会立刻变黑** ——
        // 要推过 `VisionSystem.FreshTicks`(3 秒)才能验证"没有来源再点亮它"。
        // 现场蓝方唯一视野来源就是这座塔(没有任何蓝方单位),所以过期后必须是黑的。
        Step(VisionSystem.FreshTicks + 5);
        Assert.IsFalse(VisionSystem.IsFresh(sim, Side.Blue, new GridPos(80, 90)),
            "废墟不该提供视野:推过新鲜窗口后,原来靠这座塔点亮的格子必须重新变黑");
    }

    [Test]
    public void T4_废墟不做城池主动攻击()
    {
        // 行为锁:城里驻着射手主将时,建筑会**以该主将的名义**主动开火(5 伤 / 0.5 秒)。
        // 被拆成废墟之后必须彻底停手。
        // 度量方式:数**以该主将 id 为攻击者**的 `DamageDealt` 事件(`GarrisonSystem` 的城池主动攻击
        // 就是这么发事件的)—— 而不是看单位掉多少血:那一带还有他自带的 12 名士兵等别的伤害源,
        // 拿总掉血量做判据会把"别的东西在打"误算成"废墟在打"(第一版就是这么假红的)。
        var tower = SpawnTower(Side.Blue, new GridPos(80, 100));
        var archer = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhuiRi, new GridPos(80, 96));
        string err;
        Assert.IsTrue(GarrisonSystem.EnterFormal(sim, new List<int> { archer.Id }, tower.Id, out err),
            "射手主将应能驻军:" + err);

        // 目标放在 (80,116):距塔心 15.5 米 —— 在**城池视野 24** 内(城池主动攻击够得到),
        // 但**在坠日自身射程 8 之外**(拆成废墟后主将被放出来、站在原地也够不着),
        // 同时也避开他那 12 名士兵(在 (80,96) 一带)。这样"唯一能打到它的来源"就是城池主动攻击。
        var red = SpawnHero(Side.Red, HeroId.ZhenYue, new GridPos(80, 116));
        Assert.Greater(SimVec2.Distance(red.PosF, SimVec2.FromGrid(tower.Pos)), archer.Range,
            "用例前提:目标应在射手主将自己射程之外");

        int cityHits = 0;
        System.Action<SimEvent> tap = ev =>
        {
            if (ev.Type == SimEventType.DamageDealt && ev.AttackerId == archer.Id) cityHits++;
        };
        sim.AddEventListener(tap);
        Step(60);
        Assert.Greater(cityHits, 0, "用例前提:射手守城的城池主动攻击应生效");
        float hpAfterCityAttack = red.Hp;

        Rubble(sim, tower);
        cityHits = 0;
        Step(60);   // 再推 3 秒
        sim.RemoveEventListener(tap);
        Assert.AreEqual(0, tower.GarrisonFormal.Count, "废墟驻军应被清空");
        Assert.AreEqual(0, cityHits, "废墟不该再做城池主动攻击(旧口径:拆了照打)");
        Assert.AreEqual(hpAfterCityAttack, red.Hp, 0.001f, "废墟之后不该再有任何来自这座城的伤害");
    }
}
