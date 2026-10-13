using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>据点信息快照(冻结显示用,纯信息层)。</summary>
    public sealed class BuildingSnapshot
    {
        public float Hp;
        public float MaxHp;
        public Side Side;
        public bool Rubbled;
        public int GarrisonCount;
        /// <summary>2026-09-14:取这份快照的时刻(sim tick)。界面要标"信息获取时间"(制作人口径)。</summary>
        public long Tick;
    }

    /// <summary>双方对称的视野/战争迷雾信息层。纯信息,不影响战斗/移动/索敌。M8.1b:建筑视野=半径圆盘(无椭圆过滤)+占地格标记;单位/精灵保持椭圆过滤。M8.2b:士兵视野 远程6/近战3。</summary>
    public static class VisionSystem
    {
        public const int FreshTicks = 30;              // 信息新鲜 3 秒(30 tick)
        public const int SpriteIntervalTicks = 300;    // 精灵每 30 秒出发一次
        const float SpriteSpeedFriendly = 1.5f;  // 己方区段 15 米/s(每 tick 1.5 米)
        const float SpriteSpeedContested = 0.6f; // 后续区段 6 米/s(每 tick 0.6 米)
        const int Rows = LaneGrid.Rows;
        const int Cols = LaneGrid.Cols;

        sealed class SideState
        {
            public readonly Dictionary<long, long> LastSeen = new Dictionary<long, long>();
            public readonly Dictionary<int, BuildingSnapshot> Buildings = new Dictionary<int, BuildingSnapshot>();
            public long NextSpriteDeparture;
            public float SpriteTravel;   // 精灵已行进米数(浮点)
            public bool SpriteActive;    // 精灵在途
            public int SpriteCol = -1;   // 精灵当前列(渲染用,淡蓝/淡红发光)
        }

        static readonly SideState Blue = new SideState();
        static readonly SideState Red = new SideState();

        static SideState S(Side side) { return side == Side.Blue ? Blue : Red; }
        static long Key(GridPos p) { return p.Row * 1000L + p.Col; }

        public static void Reset()
        {
            Blue.LastSeen.Clear(); Blue.Buildings.Clear(); Blue.NextSpriteDeparture = 300; Blue.SpriteActive = false; Blue.SpriteTravel = 0f; Blue.SpriteCol = -1; // 首发第 30 秒
            Red.LastSeen.Clear(); Red.Buildings.Clear(); Red.NextSpriteDeparture = 300; Red.SpriteActive = false; Red.SpriteTravel = 0f; Red.SpriteCol = -1; // 首发第 30 秒
        }

        public static void Tick(SimCore sim, float dt)
        {
            if (sim.Game == null) return; // 无配置的裸模拟(旧测试)跳过
            TickSide(sim, Side.Blue);
            TickSide(sim, Side.Red);
        }

        static void TickSide(SimCore sim, Side side)
        {
            var st = S(side);

            // ---- 1) 野战单位来源(椭圆过滤保持) ----
            int forwardCol = side == Side.Blue ? int.MinValue : int.MaxValue;
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Unit || e.Side != side) continue;
                if (e.State == EntityState.Dead || e.GarrisonBuildingId != 0) continue;
                if (side == Side.Blue) { if (e.Pos.Col > forwardCol) forwardCol = e.Pos.Col; }
                else { if (e.Pos.Col < forwardCol) forwardCol = e.Pos.Col; }
            }
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Unit || e.Side != side) continue;
                if (e.State == EntityState.Dead || e.GarrisonBuildingId != 0) continue;
                MarkDisc(st, sim.Tick, e.Pos, UnitRadius(e, forwardCol), true);
            }

            // ---- 2) 建筑来源(M8.1b):半径圆盘(不做椭圆过滤)+占地格逐格标记 ----
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Building || e.Side != side || e.Rubbled) continue;
                MarkDisc(st, sim.Tick, e.Pos, BuildingVisionRadius(e), false);
                foreach (var cell in BuildingFootprint.CellsOccupied(e))
                    MarkDisc(st, sim.Tick, cell, 0, false);
            }

            // ---- 2.5) 城墙视野(2026-09-11 三路地图):完好段"墙外 10 米"条带 ----
            // 沿段弧线采样(每 2 米一点,半径 R+5 处向外 10 米条带),不做椭圆过滤(与建筑盘一致)。
            var wc = WallSystem.BaseCenter(side);
            for (int lane = 0; lane < 3; lane++)
            {
                if (WallSystem.IsBroken(side, lane)) continue;
                float a0 = WallSystem.LaneAngleOf(side, lane) - 26.6f / WallSystem.Radius;
                float a1 = WallSystem.LaneAngleOf(side, lane) + 26.6f / WallSystem.Radius;
                for (float a = a0; a <= a1; a += 2f / WallSystem.Radius)
                {
                    for (float rr = WallSystem.Radius - WallSystem.WallHalf; rr <= WallSystem.Radius + WallSystem.VisionRadius; rr += 4f)
                    {
                        int row = (int)(wc.Y + rr * System.Math.Sin(a));
                        int col = (int)(wc.X + rr * System.Math.Cos(a));
                        if (row < 0 || row >= Rows || col < 0 || col >= Cols) continue;
                        st.LastSeen[row * 1000L + col] = sim.Tick;
                    }
                }
            }

            // ---- 3) 视野精灵 ----
            TickSprite(sim, side, st);

            // ---- 4) 建筑快照:本 tick 建筑格被揭示则刷新 ----
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Building) continue;
                long t;
                if (st.LastSeen.TryGetValue(Key(e.Pos), out t) && t == sim.Tick)
                {
                    st.Buildings[e.Id] = new BuildingSnapshot
                    {
                        Hp = e.Hp,
                        MaxHp = e.MaxHp,
                        Side = e.Side,
                        Rubbled = e.Rubbled,
                        GarrisonCount = e.GarrisonFormal.Count + e.GarrisonTemp.Count,
                        Tick = sim.Tick
                    };
                }
            }
        }

        /// <summary>建筑视野半径(2026-09-06 用户拍板在原值上扩大一倍):大本营 40、箭塔 24、其余 20。
        /// 2026-09-16 三十七轮补记改 public:临时阵地放置规则(最前己方建筑视野内可放)与此处同一口径。</summary>
        public static int BuildingVisionRadius(Entity b)
        {
            if (b.Building == BuildingId.Base) return 40;
            if (b.Building == BuildingId.Tower) return 24;
            return 20; // Barracks / ResourcePoint / 堡垒
        }

        static int UnitRadius(Entity e, int forwardCol)
        {
            // 2026-09-14 制作人口径:**近战视野 8**;小兵视野 = 主将视野的一半;
            // 例外一:**射手小兵视野 = 射手主将视野**(射手 9)。射手射程同时收到 8(见 gameconfig),
            //   于是"射手在近战视野外开火"这条边界情况从数值上也不成立(8 ≤ 8)。
            // 例外二(2026-09-14 制作人澄清"断峰 10 是专属被动"):**断峰=鹰眼 10**,
            //   仅当它是己方最靠前的野战单位(不存在更靠敌方向者)时生效 —— 保留英雄特色。
            bool ranged = e.Hero == HeroId.ZhuiRi || e.Hero == HeroId.XingLuo;
            if (e.IsSoldier)
                return ranged ? 9 : 4;          // 射手兵 9(=主将) / 近战兵 4(=主将 8 的一半)
            if (e.Hero == HeroId.DuanFeng && e.Pos.Col == forwardCol) return 10;   // 鹰眼(断峰专属被动)
            if (e.Hero == null) return 8;       // 无英雄的裸单位按近战 8
            return ranged ? 9 : 8;              // 射手主将 9 / 近战主将 8
        }

        static void TickSprite(SimCore sim, Side side, SideState st)
        {
            // 追平出发时刻表(300/600…)
            while (sim.Tick - st.NextSpriteDeparture >= SpriteIntervalTicks)
                st.NextSpriteDeparture += SpriteIntervalTicks;
            if (sim.Tick < st.NextSpriteDeparture) { st.SpriteActive = false; return; }

            GridPos? basePos = null;
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind == EntityKind.Building && e.Side == side && e.Building == BuildingId.Base)
                {
                    basePos = e.Pos;
                    break;
                }
            }
            if (!basePos.HasValue) { st.SpriteActive = false; return; }

            if (!st.SpriteActive) // 出发瞬间:复位行程
            {
                st.SpriteActive = true;
                st.SpriteTravel = 0f;
            }
            int dir = side == Side.Blue ? 1 : -1;
            int baseCol = basePos.Value.Col;
            // 己方最靠前建筑列(无则=大本营自身)
            int frontCol = baseCol;
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Building || e.Side != side || e.Rubbled) continue;
                if (side == Side.Blue && e.Pos.Col > frontCol) frontCol = e.Pos.Col;
                if (side == Side.Red && e.Pos.Col < frontCol) frontCol = e.Pos.Col;
            }
            // 两段速度:己方区段(至最靠前己方建筑)15 米/s,之后 6 米/s
            float frontDist = System.Math.Abs(frontCol - baseCol);
            float speed = st.SpriteTravel < frontDist ? SpriteSpeedFriendly : SpriteSpeedContested;
            st.SpriteTravel += speed;

            int col = baseCol + dir * (int)st.SpriteTravel;
            if (col < 0 || col >= Cols) { st.SpriteActive = false; return; }
            st.SpriteCol = col;
            for (int row = 0; row < Rows; row++)
            {
                var p = new GridPos(row, col);
                if (!TerrainMap.IsInside(p)) continue; // 只揭示椭圆内
                st.LastSeen[row * 1000L + col] = sim.Tick;
            }
        }

        /// <summary>精灵当前位置(渲染发光用;-1=不在途)。</summary>
        public static int SpriteColOf(Side side) => S(side).SpriteCol;

        static void MarkDisc(SideState st, long tick, GridPos center, int radius, bool skipOutsideEllipse)
        {
            // 欧氏圆盘(米制视野):sqrt(dr²+dc²) ≤ radius。skipOutsideEllipse=单位调用(椭圆外跳过);建筑调用 false=视盘不做椭圆过滤。radius=0 即单格标记
            for (int dr = -radius; dr <= radius; dr++)
            {
                int row = center.Row + dr;
                if (row < 0 || row >= Rows) continue;
                int dcMax = (int)System.Math.Floor(System.Math.Sqrt((double)(radius * radius - dr * dr)));
                for (int dc = -dcMax; dc <= dcMax; dc++)
                {
                    int col = center.Col + dc;
                    if (col < 0 || col >= Cols) continue;
                    var p = new GridPos(row, col);
                    if (skipOutsideEllipse && !TerrainMap.IsInside(p)) continue;
                    st.LastSeen[row * 1000L + col] = tick;
                }
            }
        }

        public static bool IsFresh(SimCore sim, Side side, GridPos cell)
        {
            long t;
            return S(side).LastSeen.TryGetValue(Key(cell), out t) && sim.Tick - t <= FreshTicks;
        }

        public static bool IsUnitVisible(SimCore sim, Side side, int unitId)
        {
            Entity e;
            if (!TryGet(sim, unitId, out e) || e.State == EntityState.Dead) return false;
            var pos = e.Pos;
            if (e.GarrisonBuildingId != 0)
            {
                Entity b;
                if (TryGet(sim, e.GarrisonBuildingId, out b)) pos = b.Pos; // 驻军以建筑格判断
            }
            return IsFresh(sim, side, pos);
        }

        public static bool IsBuildingKnown(SimCore sim, Side side, int buildingId)
        {
            return S(side).Buildings.ContainsKey(buildingId); // 冻结快照也算已知
        }

        public static int? LastKnownGarrison(SimCore sim, Side side, int buildingId)
        {
            BuildingSnapshot s;
            return S(side).Buildings.TryGetValue(buildingId, out s) ? (int?)s.GarrisonCount : null;
        }

        public static float? LastKnownHp(SimCore sim, Side side, int buildingId)
        {
            BuildingSnapshot s;
            return S(side).Buildings.TryGetValue(buildingId, out s) ? (float?)s.Hp : null;
        }

        /// <summary>最后已知建筑快照(冻结显示用);未知返回 null。返回对象只读,调用方勿改。</summary>
        public static BuildingSnapshot LastKnownBuilding(SimCore sim, Side side, int buildingId)
        {
            BuildingSnapshot s;
            return S(side).Buildings.TryGetValue(buildingId, out s) ? s : null;
        }

        static bool TryGet(SimCore sim, int id, out Entity e)
        {
            e = null;
            if (id == 0) return false;
            Entity found;
            if (!sim.Entities.TryGetValue(id, out found)) return false;
            e = found;
            return true;
        }
    }
}
