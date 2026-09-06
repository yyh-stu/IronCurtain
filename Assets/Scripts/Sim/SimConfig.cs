namespace IronCurtain.Sim
{
    public sealed class SimConfig
    {
        public int TickRate;
        public int LaneRows;
        public int LaneCols;
        public int MaxUnitsPerLane;
        public bool AutoBehavior; // M8.5b 兵团自主行为总开关(默认 false,测试隔离)

        public SimConfig(int tickRate = 10, int laneRows = 30, int laneCols = 286, int maxUnitsPerLane = 200)
        {
            TickRate = tickRate;
            LaneRows = laneRows;
            LaneCols = laneCols;
            MaxUnitsPerLane = maxUnitsPerLane;
        }
    }
}
