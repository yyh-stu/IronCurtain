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
                MarkDisc(st, sim.Tick, e.Pos, UnitRadius(sim, side, e, forwardCol), true);
            }

            // ---- 2) 建筑来源(M8.1b):半径圆盘(不做椭圆过滤)+占地格逐格标记 ----
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Building || e.Side != side || e.Rubbled) continue;
                MarkDisc(st, sim.Tick, e.Pos, BuildingVisionRadius(e), false);
                foreach (var cell in BuildingFootprint.CellsOccupied(e))
                    MarkDisc(st, sim.Tick, cell, 0, false);
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
                        GarrisonCount = e.GarrisonFormal.Count + e.GarrisonTemp.Count
                    };
                }
            }
        }

        /// <summary>建筑视野半径(M5.0 权威):大本营 20、箭塔 12、其余 10。</summary>
        static int BuildingVisionRadius(Entity b)
        {
            if (b.Building == BuildingId.Base) return 20;
            if (b.Building == BuildingId.Tower) return 12;
            return 10; // Barracks / ResourcePoint / 堡垒
        }

        static int UnitRadius(SimCore sim, Side side, Entity e, int forwardCol)
        {
            // 士兵(M8.2b):远程 6 / 近战 3(远程判定与 DamageCalculator.IsRanged 同语义)
            if (e.IsSoldier)
                return (e.Hero == HeroId.ZhuiRi || e.Hero == HeroId.XingLuo) ? 6 : 3;
            // 鹰眼:断峰是己方最靠前的野战单位(不存在更靠敌方向者)→ 10
            if (e.Hero == HeroId.DuanFeng && e.Pos.Col == forwardCol) return 10;
            if (e.Hero == null) return 6; // 无英雄的裸单位按近战 6 格
            return (e.Hero == HeroId.ZhuiRi || e.Hero == HeroId.XingLuo) ? 9 : 6; // 射手 9 / 近战(含断峰)6
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
