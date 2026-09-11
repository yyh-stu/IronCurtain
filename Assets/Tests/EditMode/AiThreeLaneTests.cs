using System.Collections.Generic;
using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>2026-09-11 AI 适配三路:①选将随机化(制作人拍板"避免全屏磐石");
    /// ②波次目标按路轮转(上/中/下分推,不再全挤一条线);③草坪箭塔改占领意图(不打掉,占过来)。</summary>
    public class AiThreeLaneTests
    {
        private static GameConfig _cfg;

        [SetUp]
        public void SetUp()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            AiSystem.Reset();
        }

        [TearDown]
        public void TearDown() => AiSystem.Reset();

        [Test]
        public void 产将随机化_普通档六英雄混编含远程()
        {
            var sim = new SimCore(new SimConfig(), new SeededRng(7));
            sim.Game = _cfg;
            var seen = new HashSet<HeroId>();
            for (int i = 0; i < 30; i++) seen.Add(AiSystem.PickProductionHero(sim, AiTier.Hard));
            Assert.GreaterOrEqual(seen.Count, 4, "30 次加权抽取应出现至少 4 种英雄(混编,不再固定磐石/断峰)");
            Assert.IsTrue(seen.Contains(HeroId.ZhuiRi) || seen.Contains(HeroId.XingLuo) || seen.Contains(HeroId.ZhuFeng),
                "应包含至少一种远程英雄(旧配比永不产远程)");
        }

        [Test]
        public void 产将随机化_简单档三近战随机不再恒磐石()
        {
            var sim = new SimCore(new SimConfig(), new SeededRng(11));
            sim.Game = _cfg;
            var seen = new HashSet<HeroId>();
            for (int i = 0; i < 15; i++) seen.Add(AiSystem.PickProductionHero(sim, AiTier.Simple));
            Assert.GreaterOrEqual(seen.Count, 2, "简单档 15 次抽取应出现至少 2 种英雄(不再全屏磐石)");
            foreach (var h in seen)
                Assert.IsTrue(h == HeroId.ZhenYue || h == HeroId.PanShi || h == HeroId.DuanFeng,
                    "简单档只从三近战里挑:" + h);
        }

        [Test]
        public void 波次按路轮转_红方推进至少覆盖两路()
        {
            var sim = MatchSetup.Build(_cfg, null, AiTier.Hard);   // 蓝方无人(玩家位),红方普通 AI
            // 红方加 4 座资源点催 EP(加速补充兵源;都放在红方墙内广场,不扰战场)
            foreach (var p in new[] { new GridPos(75, 250), new GridPos(85, 250), new GridPos(75, 256), new GridPos(85, 256) })
                sim.SpawnBuilding(Side.Red, BuildingId.ResourcePoint, p);

            var lanes = new HashSet<int>();
            for (int i = 0; i < 6000; i++)   // 600 秒
            {
                sim.Step();
                if (i % 25 != 0) continue;
                foreach (var e in sim.Entities.Values)
                {
                    if (e == null || e.Kind != EntityKind.Unit || e.Side != Side.Red || e.IsSoldier) continue;
                    if (e.State == EntityState.Dead) continue;
                    if (e.PosF.X > 180f) continue;                   // 只看已推进过自家前线的
                    lanes.Add(LaneIndexOf(e.PosF));
                }
            }
            Assert.GreaterOrEqual(lanes.Count, 2,
                "红方 600 秒推进应覆盖至少两条路(轮转分推),实测 lane 集合:" + string.Join(",", lanes));
        }

        /// <summary>点最贴近哪条路(0上/1中/2下)——与 AiSystem 私有实现独立复算,防同源假绿。</summary>
        private static int LaneIndexOf(SimVec2 p)
        {
            int best = 1; float bd = float.MaxValue;
            for (int l = 0; l < 3; l++)
            {
                float d = System.Math.Abs(p.Y - TerrainMap.LaneY(p.X, l));
                if (d < bd) { bd = d; best = l; }
            }
            return best;
        }
    }
}
