using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>M3.1 建筑占领核心:意图/入城交换/封城/自动驻军。M8.3a:交换 1:1 只耗兵,主将免疫。M8.1b:FrontCell 占地矩形敌侧缘外 1 格。</summary>
    public static class SiegeSystem
    {
        // 城内交换列表:建筑id → 攻方城内单位id
        static readonly Dictionary<int, List<int>> CityAttackers = new Dictionary<int, List<int>>();

        /// <summary>测试/重开局用:清空内部状态。</summary>
        public static void Reset() => CityAttackers.Clear();

        static bool TryGetBuilding(SimCore sim, int id, out Entity b)
        {
            b = null;
            return sim.Entities.TryGetValue(id, out b) && b.Kind == EntityKind.Building;
        }

        public static SiegeIntent GetIntent(SimCore sim, int buildingId)
        {
            return TryGetBuilding(sim, buildingId, out var b) ? b.Intent : SiegeIntent.None;
        }

        public static bool SetIntent(SimCore sim, int buildingId, Side issuer, SiegeIntent intent, out string error)
        {
            error = null;
            if (!TryGetBuilding(sim, buildingId, out var b)) { error = "目标不是建筑"; return false; }
            if (b.Rubbled) { error = "废墟需先重建"; return false; }
            if (b.Sealed) { error = "封城中不可切换意图"; return false; }
            if (b.Side == Side.Neutral && intent == SiegeIntent.Destroy) { error = "中立建筑不可摧毁"; return false; }
            if (b.Building == BuildingId.Base && intent == SiegeIntent.Capture) { error = "大本营不可占领"; return false; }
            b.Intent = intent;
            b.IntentIssuer = issuer;
            return true;
        }

        static bool Occupied(SimCore sim, GridPos p, int ignoreId)
        {
            var c = SimVec2.FromGrid(p);
            foreach (var e in sim.Entities.Values)
            {
                if (e.Id == ignoreId) continue;
                if (e.GarrisonBuildingId != 0) continue; // 驻军/城内单位不占位
                if (e.Kind == EntityKind.Building && !e.Rubbled && e.Pos == p) return true;
                if (e.Kind == EntityKind.Unit && SimVec2.Distance(e.PosF, c) < 1.0f) return true;
            }
            return false;
        }

        /// <summary>攻城前排(M8.1b):占地矩形敌侧缘外 1 格(ceil(HalfW)+1),行集合 {0,-1,1,-2,2} 相对建筑行;过滤椭圆/水域/其他建筑占地格。</summary>
        static GridPos? FrontCell(SimCore sim, Entity unit, Entity b)
        {
            int dir = unit.PosF.X <= SimVec2.FromGrid(b.Pos).X ? -1 : 1; // 攻方朝建筑方向,只在本侧找位
            int col = b.Pos.Col + dir * ((int)System.Math.Ceiling(BuildingFootprint.HalfW(b)) + 1);
            // 其他建筑占地格集合(同 M8.1a 出生点过滤)
            var blocked = new HashSet<long>();
            foreach (var o in sim.Entities.Values)
                if (o.Kind == EntityKind.Building && !o.Rubbled && o.Id != b.Id)
                    foreach (var c in BuildingFootprint.CellsOccupied(o)) blocked.Add(c.Row * 1000L + c.Col);
            int[] drs = { 0, -1, 1, -2, 2 };
            foreach (int dr in drs)
            {
                var p = new GridPos(b.Pos.Row + dr, col);
                if (p.Row < 0 || p.Row >= LaneGrid.Rows || p.Col < 0 || p.Col >= LaneGrid.Cols) continue;
                if (!TerrainMap.IsInside(p)) continue;
                if (TerrainMap.IsWater(SimVec2.FromGrid(p))) continue;
                if (blocked.Contains(p.Row * 1000L + p.Col)) continue;
                if (!Occupied(sim, p, unit.Id)) return p;
            }
            return null;
        }

        public static bool IssueOrder(SimCore sim, IReadOnlyList<int> unitIds, int buildingId, out string error)
        {
            error = null;
            if (!TryGetBuilding(sim, buildingId, out var b)) { error = "目标不是建筑"; return false; }
            foreach (var id in unitIds)
            {
                if (!sim.Entities.TryGetValue(id, out var u) || u.Kind != EntityKind.Unit || u.Hp <= 0) continue;
                if (u.State == EntityState.Routing) continue; // 溃逃期间不可调遣
                var cell = FrontCell(sim, u, b);
                if (cell == null) continue; // 无空位则跳过该单位
                u.TargetBuildingId = buildingId;
                sim.SetMoveTarget(id, cell.Value);
            }
            return true;
        }

        static List<int> AttackersOf(int buildingId)
        {
            if (!CityAttackers.TryGetValue(buildingId, out var l))
            {
                l = new List<int>();
                CityAttackers[buildingId] = l;
            }
            return l;
        }

        static void EnterCity(SimCore sim, Entity b, Entity u)
        {
            var list = AttackersOf(b.Id);
            if (list.Contains(u.Id)) return; // 防重复入城
            list.Add(u.Id);
            sim.FreeCell(u.Pos);           // 离格:释放格占用,不堵后续部队
            u.State = EntityState.Exchanging;
            b.Sealed = true;
        }

        static void Capture(SimCore sim, Entity b, List<int> survivors)
        {
            b.Side = b.IntentIssuer;
            if (b.Hp <= 0 && b.MaxHp <= 0)
            {
                var cfg = sim.BuildingConfigOf(b.Building.Value);
                b.MaxHp = cfg.MaxHp;
                b.Hp = cfg.MaxHp;
            }
            b.Sealed = false;
            CityAttackers.Remove(b.Id);
            GarrisonSystem.OnCityCaptured(sim, b); // 旧主驻军处置(士兵只清登记不溃逃)
            int cap = b.IsFort ? 3 : (b.Building == null ? 0 : sim.BuildingConfigOf(b.Building.Value).GarrisonFormal);
            foreach (var id in survivors)
            {
                if (!sim.Entities.TryGetValue(id, out var e)) continue;
                e.TargetBuildingId = 0;
                e.State = EntityState.Idle;
                if (e.IsSoldier && b.GarrisonFormal.Count < cap)
                {
                    b.GarrisonFormal.Add(id);
                    e.GarrisonBuildingId = b.Id;
                }
                // 非士兵(主将)不入城,城外待命
            }
        }

        static void KillDefenders(SimCore sim, Entity b, int n)
        {
            // 交换只消耗正式驻军,临时驻军受保护(用户拍板,覆盖 GDD 2.4.1)
            for (int i = 0; i < n && b.GarrisonFormal.Count > 0; i++)
            {
                var id = b.GarrisonFormal[0];
                b.GarrisonFormal.RemoveAt(0);
                sim.RemoveEntity(id);
            }
        }

        /// <summary>攻方交换损失(M8.3a):只从城内士兵中选 1 名阵亡,主将(非士兵)永不因交换阵亡;无士兵在场则攻方无损失。</summary>
        static void KillAttacker(SimCore sim, List<int> atk, int aid)
        {
            var a = sim.GetEntity(aid);
            int victim = 0;
            if (a != null && a.IsSoldier) victim = aid;
            else
            {
                for (int i = 0; i < atk.Count; i++)
                {
                    var s = sim.GetEntity(atk[i]);
                    if (s != null && s.IsSoldier) { victim = atk[i]; atk.RemoveAt(i); break; }
                }
            }
            if (victim != 0) sim.RemoveEntity(victim); // 无士兵在场 → 攻方无损失
        }

        public static void Tick(SimCore sim, float dt)
        {
            // 1) 到达判定
            var arrived = new List<Entity>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Unit && e.TargetBuildingId != 0 && !e.HasMoveTarget
                    && e.GarrisonBuildingId == 0 && e.State != EntityState.Exchanging)
                    arrived.Add(e);
            foreach (var u in arrived)
            {
                if (!TryGetBuilding(sim, u.TargetBuildingId, out var b)) { u.TargetBuildingId = 0; continue; }
                if (b.Sealed)
                {
                    if (u.Side == b.IntentIssuer) EnterCity(sim, b, u); // 封城期间攻方增援进城
                    continue;
                }
                if (b.Intent == SiegeIntent.Capture)
                {
                    if (b.GarrisonFormal.Count + b.GarrisonTemp.Count > 0) EnterCity(sim, b, u);
                    else Capture(sim, b, new List<int> { u.Id });
                }
                else if (b.Intent == SiegeIntent.Destroy)
                {
                    u.State = EntityState.Attacking; // 拆塔结算在 M3.4
                }
            }

            // 2) 交换结算:每 10 tick 一轮;1:1 兵/次,主将免疫
            if (sim.Tick % 10 != 0) return;
            var sealedBuildings = new List<Entity>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Building && e.Sealed) sealedBuildings.Add(e);
            foreach (var b in sealedBuildings)
            {
                var atk = AttackersOf(b.Id);
                while (atk.Count > 0 && b.GarrisonFormal.Count > 0)
                {
                    var aid = atk[0];
                    atk.RemoveAt(0); // 该单位结束城内待命(无论生死都离开交换队列)
                    KillDefenders(sim, b, 1); // 1:1 兵/次(制作人拍板)
                    KillAttacker(sim, atk, aid); // 攻方只耗士兵,主将免疫
                }
                if (b.GarrisonFormal.Count == 0)
                {
                    // 正式守军被肃清 → 城池易主,临时驻军按新规处置(士兵不溃逃)
                    var survivors = new List<int>(atk);
                    foreach (var id in survivors)
                        if (sim.Entities.TryGetValue(id, out var e2)) e2.State = EntityState.Idle;
                    Capture(sim, b, survivors);
                }
                else if (atk.Count == 0)
                {
                    // 攻方被肃清:解封,意图保留,剩余守军留城
                    b.Sealed = false;
                    CityAttackers.Remove(b.Id);
                }
            }
        }
    }
}
