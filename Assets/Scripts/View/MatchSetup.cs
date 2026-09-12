using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public static class MatchSetup
    {
        public static SimCore Build(GameConfig cfg, AiTier? blueTier, AiTier? redTier)
        {
            AiSystem.Reset();
            VisionSystem.Reset();
            HexSystem.Reset();
            SiegeSystem.Reset();
            WallSystem.Reset();

            var sim = new SimCore(new SimConfig { AutoBehavior = true }, new SeededRng(42)); // M8.5b 开局打开自主行为
            sim.Game = cfg;

            // 2026-09-11 制作人拍板:三路大地图(规格 MAP-3LANE-layout-spec.md v4)。
            // 大本营在基地中心(蓝(20,80)/红(250,80) 米),每线 3 栋:X=40/70/100(间距 30),
            // Y=TerrainMap.LaneY(X, lane)(上路 Row 小)。最前线(X=100)距河心 35。
            // 每线序列:兵营(40) → 资源点(70) → 箭塔(100,前线)。
            var baseBlue = SpawnAt(sim, Side.Blue, BuildingId.Base, TerrainMap.BaseCenterX(Side.Blue), 80f);
            for (int lane = 0; lane < 3; lane++)
            {
                SpawnLane(sim, Side.Blue, lane);
                SpawnLane(sim, Side.Red, lane);
            }
            var baseRed = SpawnAt(sim, Side.Red, BuildingId.Base, TerrainMap.BaseCenterX(Side.Red), 80f);

            // 四大草坪各一座箭塔(制作人:非中立,天然归属本方,可被占领)。
            // 塔位=草坪中心(上下路与中路的中点,X=半场中点 67.5/202.5)。
            SpawnAt(sim, Side.Blue, BuildingId.Tower, 67.5f, 51.9f);
            SpawnAt(sim, Side.Blue, BuildingId.Tower, 67.5f, 108.1f);
            SpawnAt(sim, Side.Red, BuildingId.Tower, 202.5f, 51.9f);
            SpawnAt(sim, Side.Red, BuildingId.Tower, 202.5f, 108.1f);

            // 将兵制(M8.4b):将一律从主基地产出,兵营不再配生产集(保持 null)
            baseBlue.Prod = new ProductionSet();
            baseRed.Prod = new ProductionSet();

            // 开局 3 将:中路大本营前一字排开(GridPos(row,col),row=Y=80±2、col=X 26-30),
            // 在主城广场内、城墙门洞后;三路地图的出生语义后续由城墙玩法接管再调整
            // 三将站在主城石路上(基地前 14 米),蓝在基地右侧、红在左侧
            // 2026-09-13 BP:走过 BP 选将时用选将结果(双方可用同一英雄);否则默认 磐石/断峰/坠日
            int blueCol = (int)TerrainMap.BaseCenterX(Side.Blue) + 14;
            int redCol = (int)TerrainMap.BaseCenterX(Side.Red) - 14;
            var blueHeroes = AppFlow.BpBluePicks != null && AppFlow.BpBluePicks.Count == 3
                ? AppFlow.BpBluePicks : new System.Collections.Generic.List<HeroId> { HeroId.PanShi, HeroId.DuanFeng, HeroId.ZhuiRi };
            var redHeroes = AppFlow.BpRedPicks != null && AppFlow.BpRedPicks.Count == 3
                ? AppFlow.BpRedPicks : blueHeroes;
            RegimentSystem.SpawnGeneral(sim, Side.Blue, blueHeroes[0], new GridPos(78, blueCol));
            RegimentSystem.SpawnGeneral(sim, Side.Blue, blueHeroes[1], new GridPos(80, blueCol));
            RegimentSystem.SpawnGeneral(sim, Side.Blue, blueHeroes[2], new GridPos(82, blueCol));

            RegimentSystem.SpawnGeneral(sim, Side.Red, redHeroes[0], new GridPos(78, redCol));
            RegimentSystem.SpawnGeneral(sim, Side.Red, redHeroes[1], new GridPos(80, redCol));
            RegimentSystem.SpawnGeneral(sim, Side.Red, redHeroes[2], new GridPos(82, redCol));

            if (blueTier.HasValue) AiSystem.Configure(sim, Side.Blue, blueTier.Value);
            if (redTier.HasValue) AiSystem.Configure(sim, Side.Red, redTier.Value);

            return sim;
        }

        private static Entity Spawn(SimCore sim, Side side, BuildingId type, GridPos pos)
        {
            return sim.SpawnBuilding(side, type, pos);
        }

        /// <summary>米制落位(三路地图用):建筑格 = 米坐标取整。</summary>
        private static Entity SpawnAt(SimCore sim, Side side, BuildingId type, float x, float y)
        {
            return Spawn(sim, side, type, new GridPos((int)y, (int)x));
        }

        /// <summary>一条路 3 栋(蓝正向/红镜像):兵营 X=40 → 资源点 70 → 箭塔 100(前线)。</summary>
        private static void SpawnLane(SimCore sim, Side side, int lane)
        {
            float dir = side == Side.Blue ? 1f : -1f;
            float bx = TerrainMap.BaseCenterX(side);
            SpawnAt(sim, side, BuildingId.Barracks, bx + dir * 40f, TerrainMap.LaneY(bx + dir * 40f, lane));
            SpawnAt(sim, side, BuildingId.ResourcePoint, bx + dir * 70f, TerrainMap.LaneY(bx + dir * 70f, lane));
            SpawnAt(sim, side, BuildingId.Tower, bx + dir * 100f, TerrainMap.LaneY(bx + dir * 100f, lane));
        }

        public static void Reset()
        {
            MatchStats.Reset();   // 2026-09-11 结算战绩表随局清空
            AiSystem.Reset();
            VisionSystem.Reset();
            HexSystem.Reset();
            SiegeSystem.Reset();
            WallSystem.Reset();
        }
    }
}
