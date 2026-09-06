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

            var sim = new SimCore(new SimConfig(), new SeededRng(42));
            sim.Game = cfg;

            // 2026-09-05 用户拍板:椭圆形大地图(30×286 包围盒),9 座建筑沿长轴 Row=15、间距 30 格
            var baseBlue = Spawn(sim, Side.Blue, BuildingId.Base, new GridPos { Row = 15, Col = 15 });
            Spawn(sim, Side.Blue, BuildingId.Barracks, new GridPos { Row = 15, Col = 46 });
            Spawn(sim, Side.Blue, BuildingId.ResourcePoint, new GridPos { Row = 15, Col = 77 });
            Spawn(sim, Side.Blue, BuildingId.Tower, new GridPos { Row = 15, Col = 108 });

            Spawn(sim, Side.Neutral, BuildingId.ResourcePoint, new GridPos { Row = 15, Col = 143 }); // 河中央岛

            Spawn(sim, Side.Red, BuildingId.Tower, new GridPos { Row = 15, Col = 178 });
            Spawn(sim, Side.Red, BuildingId.ResourcePoint, new GridPos { Row = 15, Col = 209 });
            Spawn(sim, Side.Red, BuildingId.Barracks, new GridPos { Row = 15, Col = 240 });
            var baseRed = Spawn(sim, Side.Red, BuildingId.Base, new GridPos { Row = 15, Col = 271 });

            // 将兵制(M8.4b):将一律从主基地产出,兵营不再配生产集(保持 null)
            baseBlue.Prod = new ProductionSet();
            baseRed.Prod = new ProductionSet();

            // 开局 3 将(测试版:磐石/断峰/坠日,基地旁散开,椭圆内)
            RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 22));
            RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.DuanFeng, new GridPos(14, 24));
            RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhuiRi, new GridPos(16, 24));

            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 264));
            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.DuanFeng, new GridPos(14, 262));
            RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.ZhuiRi, new GridPos(16, 262));

            if (blueTier.HasValue) AiSystem.Configure(sim, Side.Blue, blueTier.Value);
            if (redTier.HasValue) AiSystem.Configure(sim, Side.Red, redTier.Value);

            return sim;
        }

        private static Entity Spawn(SimCore sim, Side side, BuildingId type, GridPos pos)
        {
            return sim.SpawnBuilding(side, type, pos);
        }

        public static void Reset()
        {
            AiSystem.Reset();
            VisionSystem.Reset();
            HexSystem.Reset();
            SiegeSystem.Reset();
        }
    }
}
