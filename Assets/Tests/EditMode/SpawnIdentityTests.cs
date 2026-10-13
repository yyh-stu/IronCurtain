using System.Collections.Generic;
using IronCurtain.Sim;
using NUnit.Framework;

namespace IronCurtain.Tests
{
    /// <summary>
    /// 2026-09-14 ★回归锁定:实体出生事件发出时,**身份必须已经配好**。
    ///
    /// 背景(制作人两次报"什么将都出磐石"、第三次给出截图):
    /// 建视图的唯一入口就是 `EntitySpawned` 事件(`BoardView.OnSimEvent → CreateView → EntityView.Bind`),
    /// 而 `SimCore.SpawnEntity` 原来**先发事件再返回**,`RegimentSystem.SpawnGeneral/SpawnSoldier`
    /// 又是在事件之后才写 `Hero` / `IsSoldier` / `GeneralId`:
    /// - 主将:建视图时 `e.Hero == null` → `Bind` 走 `BuildUnit(hero ?? HeroId.PanShi)` → **一律磐石模型**;
    /// - 士兵:建视图时 `e.IsSoldier == false` → 走**主将分支**、用主将模型和主将尺寸 → "士兵和主将都是磐石、大小 1:1"。
    ///
    /// 前几轮探针没能抓到,是因为它们都是**手工建视图**(那时 Sim 早配好了),从没走过事件路径。
    /// 所以这条用例断言的不是"最终状态",而是**事件发出那一刻的快照**。
    /// </summary>
    public class SpawnIdentityTests
    {
        private SimCore sim;
        private static GameConfig _cfg;

        [SetUp]
        public void SetUp()
        {
            WallSystem.Reset();
            _cfg ??= GameConfigParser.FromJson(UnityEngine.Resources.Load<UnityEngine.TextAsset>("gameconfig").text);
            sim = new SimCore(new SimConfig(), new SeededRng(13));
            sim.Game = _cfg;
        }

        /// <summary>在事件回调里就取一次身份 —— 这是 View 建模型时能看到的同一份数据。</summary>
        private Dictionary<int, string> SnapshotOnSpawn()
        {
            var snap = new Dictionary<int, string>();
            sim.AddEventListener(ev =>
            {
                if (ev.Type != SimEventType.EntitySpawned) return;
                var e = sim.GetEntity(ev.SubjectId);
                if (e == null || e.Kind != EntityKind.Unit) return;
                snap[e.Id] = (e.IsSoldier ? "兵" : "将") + ":" + e.Hero;
            });
            return snap;
        }

        [Test]
        public void 出生事件发出时主将已带Hero且士兵已标记为士兵()
        {
            var snap = SnapshotOnSpawn();
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.XingLuo, new GridPos(80, 40));

            Assert.IsTrue(snap.ContainsKey(g.Id), "主将必须发过出生事件(否则永远没有视图)");
            Assert.AreEqual("将:XingLuo", snap[g.Id],
                "主将出生事件发出时必须已带 Hero —— 否则视图会退化成磐石模型");

            int soldiers = 0;
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (!e.IsSoldier || e.GeneralId != g.Id) continue;
                soldiers++;
                Assert.IsTrue(snap.ContainsKey(e.Id), "士兵 " + e.Id + " 必须发过出生事件");
                Assert.AreEqual("兵:XingLuo", snap[e.Id],
                    "士兵出生事件发出时必须已标记 IsSoldier 且带上主将 Hero —— "
                    + "否则视图会拿主将模型和主将尺寸去建士兵");
            }
            Assert.AreEqual(RegimentSystem.SoldiersPerGeneral, soldiers, "一个满编兵团 12 兵");
        }

        [Test]
        public void 六英雄每个出生事件都带各自Hero()
        {
            var snap = SnapshotOnSpawn();
            var heroes = new[] { HeroId.PanShi, HeroId.ZhenYue, HeroId.DuanFeng,
                                 HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
            var made = new Dictionary<int, HeroId>();
            for (int i = 0; i < heroes.Length; i++)
            {
                var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, heroes[i], new GridPos(60 + i * 6, 40));
                made[g.Id] = heroes[i];
            }
            foreach (var kv in made)
                Assert.AreEqual("将:" + kv.Value, snap[kv.Key], "主将 " + kv.Key + " 的出生事件必须带自己的 Hero");
        }
    }
}
