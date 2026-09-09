using System;
using System.Collections.Generic;
using System.Linq;

namespace IronCurtain.Sim
{
    public enum SiegeOrderError : byte
    {
        None, InvalidState, InvalidTarget, InvalidIssuer, InvalidIntent,
        IntentRejected, NoEligibleSoldiers, NoValidPlacement
    }

    public readonly struct SiegeOrderResult
    {
        public bool Success => Error == SiegeOrderError.None;
        public SiegeOrderError Error { get; }
        public IReadOnlyList<int> SoldierIds { get; }

        internal SiegeOrderResult(SiegeOrderError error, int[] soldierIds = null)
        {
            Error = error;
            SoldierIds = soldierIds ?? Array.Empty<int>();
        }
    }

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
            if (!ValidateIntent(sim, buildingId, issuer, intent, out var b, out error)) return false;
            b.Intent = intent;
            b.IntentIssuer = issuer;
            return true;
        }

        static bool ValidateIntent(
            SimCore sim, int buildingId, Side issuer, SiegeIntent intent, out Entity b, out string error)
        {
            b = null;
            error = null;
            if (sim == null || !TryGetBuilding(sim, buildingId, out b)) { error = "目标不是建筑"; return false; }
            if (issuer == Side.Neutral) { error = "发令阵营无效"; return false; }
            if (b.Rubbled) { error = "废墟需先重建"; return false; }
            if (b.Sealed) { error = "封城中不可切换意图"; return false; }
            if (intent != SiegeIntent.None && b.Side != Side.Neutral && b.Side == issuer)
            { error = "不可对己方建筑下达占领或摧毁命令"; return false; }
            if (b.Side == Side.Neutral && intent == SiegeIntent.Destroy) { error = "中立建筑不可摧毁"; return false; }
            if (b.Building == BuildingId.Base && intent == SiegeIntent.Capture) { error = "大本营不可占领"; return false; }
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

        /// <summary>攻城前排(M8.1b):从敌侧缘外 1 格起，按纵向距离再向外分层确定性找位。</summary>
        static GridPos? FrontCell(SimCore sim, Entity unit, Entity b, HashSet<long> reserved = null)
        {
            int dir = unit.PosF.X <= SimVec2.FromGrid(b.Pos).X ? -1 : 1; // 攻方朝建筑方向,只在本侧找位
            int frontCol = b.Pos.Col + dir * ((int)System.Math.Ceiling(BuildingFootprint.HalfW(b)) + 1);
            // 其他建筑占地格集合(同 M8.1a 出生点过滤)
            var blocked = new HashSet<long>();
            foreach (var o in sim.Entities.Values)
                if (o.Kind == EntityKind.Building && !o.Rubbled && o.Id != b.Id)
                    foreach (var c in BuildingFootprint.CellsOccupied(o)) blocked.Add(c.Row * 1000L + c.Col);
            for (int depth = 0; depth < 10; depth++)
            {
                int col = frontCol + dir * depth;
                if (col < 0 || col >= LaneGrid.Cols) break;
                for (int rowDistance = 0; rowDistance < LaneGrid.Rows; rowDistance++)
                {
                    int upper = b.Pos.Row + rowDistance;
                    if (TryFrontCandidate(upper, col, out var found)) return found;
                    if (rowDistance > 0)
                    {
                        int lower = b.Pos.Row - rowDistance;
                        if (TryFrontCandidate(lower, col, out found)) return found;
                    }
                }
            }
            return null;

            bool TryFrontCandidate(int row, int col, out GridPos value)
            {
                value = new GridPos(row, col);
                if (row < 0 || row >= LaneGrid.Rows) return false;
                long key = row * 1000L + col;
                if (!TerrainMap.IsInside(value)) return false;
                if (TerrainMap.IsWater(SimVec2.FromGrid(value))) return false;
                if (blocked.Contains(key) || (reserved != null && reserved.Contains(key))) return false;
                return !Occupied(sim, value, unit.Id);
            }
        }

        /// <summary>
        /// 将兵制入口：接受主将选择（也容忍既有士兵 ID），确定性展开为当前可下令士兵。
        /// 验证、落点规划全部通过后才同时提交意图和移动，失败时零副作用。
        /// </summary>
        public static SiegeOrderResult TryIssueRegimentOrder(
            SimCore sim, IReadOnlyList<int> selectedIds, int buildingId, Side issuer, SiegeIntent intent)
        {
            if (sim == null) return new SiegeOrderResult(SiegeOrderError.InvalidState);
            if (issuer == Side.Neutral) return new SiegeOrderResult(SiegeOrderError.InvalidIssuer);
            if (intent == SiegeIntent.None) return new SiegeOrderResult(SiegeOrderError.InvalidIntent);
            if (!ValidateIntent(sim, buildingId, issuer, intent, out var b, out _))
                return new SiegeOrderResult(TryGetBuilding(sim, buildingId, out _)
                    ? SiegeOrderError.IntentRejected : SiegeOrderError.InvalidTarget);

            var soldiers = ResolveSelectedSoldiers(sim, selectedIds, issuer, true);
            if (soldiers.Count == 0) return new SiegeOrderResult(SiegeOrderError.NoEligibleSoldiers);
            if (!TryPlanOrders(sim, soldiers, b, out var plan))
                return new SiegeOrderResult(SiegeOrderError.NoValidPlacement);

            // Commit boundary: no mutation occurs before every selected soldier has a valid plan.
            b.Intent = intent;
            b.IntentIssuer = issuer;
            // 2026-09-09 制作人裁决(二次修正):兵团原子 = 兵随主将。攻城令下给主将——
            // 主将行军至城外集结点并标记;士兵此刻不受令,靠编队跟随随行;
            // 主将到位后由 Tick 放本兵团士兵入交换。此前两版(直接派士兵/主将随军追兵)均废弃。
            OrderGeneralMarch(sim, selectedIds, b, issuer);
            return new SiegeOrderResult(SiegeOrderError.None, soldiers.Select(s => s.Id).ToArray());
        }

        /// <summary>攻城令=主将令:标记 g.TargetBuildingId 并行军至建筑城外集结点(碰撞半径+5,朝主将方向,椭圆钳制)。</summary>
        private static void OrderGeneralMarch(SimCore sim, IReadOnlyList<int> selectedIds, Entity b, Side issuer)
        {
            var bPos = new SimVec2(b.Pos.Col + 0.5f, b.Pos.Row + 0.5f);
            foreach (int id in selectedIds)
            {
                if (!sim.Entities.TryGetValue(id, out var g) || g == null || g.IsSoldier
                    || g.Kind != EntityKind.Unit || g.Side != issuer
                    || g.State == EntityState.Dead || g.State == EntityState.Routing) continue;
                float len = SimVec2.Distance(g.PosF, bPos);
                if (len < 0.01f) continue;
                var dir = new SimVec2((g.PosF.X - bPos.X) / len, (g.PosF.Y - bPos.Y) / len);
                var staging = TerrainMap.ClampToEllipse(bPos + dir * (SimCore.BuildingCollisionRadius(b) + 5f));
                g.TargetBuildingId = b.Id; // 兵团攻城标记(到位放兵用;主将不参与交换)
                sim.SetMoveTarget(g.Id, staging.ToGrid(), true);
            }
        }

        /// <summary>
        /// 旧士兵级入口。保留合法士兵调用兼容性，但不再把主将当作可移动攻城单位。
        /// </summary>
        public static bool IssueOrder(SimCore sim, IReadOnlyList<int> unitIds, int buildingId, out string error)
        {
            error = null;
            if (sim == null || !TryGetBuilding(sim, buildingId, out var b))
            {
                error = "目标不是建筑";
                return false;
            }

            var direct = ResolveSelectedSoldiers(sim, unitIds, Side.Neutral, false);
            if (direct.Count == 0) { error = OrderErrorText(SiegeOrderError.NoEligibleSoldiers); return false; }
            Side issuer = b.Intent != SiegeIntent.None ? b.IntentIssuer : direct[0].Side;
            direct = direct.Where(s => IsEligibleSoldier(s, issuer)).ToList();
            if (direct.Count == 0) { error = OrderErrorText(SiegeOrderError.NoEligibleSoldiers); return false; }
            if (!TryPlanOrders(sim, direct, b, out var plan))
            {
                error = OrderErrorText(SiegeOrderError.NoValidPlacement);
                return false;
            }
            CommitOrders(sim, buildingId, plan);
            return true;
        }

        static List<Entity> ResolveSelectedSoldiers(
            SimCore sim, IReadOnlyList<int> selectedIds, Side issuer, bool expandGenerals)
        {
            var resolved = new SortedDictionary<int, Entity>();
            if (selectedIds == null) return resolved.Values.ToList();
            foreach (int id in new SortedSet<int>(selectedIds))
            {
                if (!sim.Entities.TryGetValue(id, out var selected) || selected.Kind != EntityKind.Unit) continue;
                if (selected.IsSoldier)
                {
                    if ((issuer == Side.Neutral || selected.Side == issuer)
                        && IsEligibleSoldier(selected, selected.Side))
                        resolved[selected.Id] = selected;
                    continue;
                }
                if (!expandGenerals || !IsEligibleGeneral(selected, issuer)) continue;
                foreach (var soldier in sim.Entities.Values
                    .Where(e => e.IsSoldier && e.GeneralId == selected.Id)
                    .OrderBy(e => e.Id))
                    if (IsEligibleSoldier(soldier, issuer)) resolved[soldier.Id] = soldier;
            }
            return resolved.Values.ToList();
        }

        static bool IsEligibleGeneral(Entity e, Side issuer) =>
            e != null && e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == issuer
            && e.Hp > 0 && e.State != EntityState.Dead && e.State != EntityState.Routing
            && e.State != EntityState.Exchanging && e.GarrisonBuildingId == 0;

        static bool IsEligibleSoldier(Entity e, Side issuer) =>
            issuer != Side.Neutral && e != null && e.Kind == EntityKind.Unit && e.IsSoldier && e.Side == issuer
            && e.Hp > 0 && e.State != EntityState.Dead && e.State != EntityState.Routing
            && e.State != EntityState.Exchanging && e.GarrisonBuildingId == 0;

        static bool TryPlanOrders(
            SimCore sim, IReadOnlyList<Entity> soldiers, Entity building,
            out List<KeyValuePair<int, GridPos>> plan)
        {
            plan = new List<KeyValuePair<int, GridPos>>(soldiers.Count);
            var reserved = new HashSet<long>();
            foreach (var soldier in soldiers)
            {
                var cell = FrontCell(sim, soldier, building, reserved);
                if (!cell.HasValue) { plan.Clear(); return false; }
                plan.Add(new KeyValuePair<int, GridPos>(soldier.Id, cell.Value));
                reserved.Add(cell.Value.Row * 1000L + cell.Value.Col);
            }
            return true;
        }

        static void CommitOrders(SimCore sim, int buildingId, List<KeyValuePair<int, GridPos>> plan)
        {
            foreach (var item in plan)
            {
                var soldier = sim.GetEntity(item.Key);
                soldier.TargetBuildingId = buildingId;
                sim.SetMoveTarget(item.Key, item.Value, true);
            }
        }

        static string OrderErrorText(SiegeOrderError error)
        {
            switch (error)
            {
                case SiegeOrderError.NoEligibleSoldiers: return "没有可下令士兵";
                case SiegeOrderError.NoValidPlacement: return "没有合法攻城前排位置";
                default: return "攻城命令无效";
            }
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
            // 0) 兵随主将(2026-09-09 制作人裁决):带攻城标记的主将抵达城外(无行军目标且在
            // 碰撞半径+8 内)→ 放本兵团尚未受令的士兵入场;建筑失效/意图清空则清标记。
            var marching = new List<Entity>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Unit && !e.IsSoldier && e.TargetBuildingId != 0)
                    marching.Add(e);
            foreach (var g in marching)
            {
                if (!TryGetBuilding(sim, g.TargetBuildingId, out var gb)
                    || gb.Rubbled || gb.Intent == SiegeIntent.None)
                {
                    g.TargetBuildingId = 0; // 建筑没了/意图撤销 → 撤销兵团攻城标记
                    continue;
                }
                if (g.HasMoveTarget || g.GarrisonBuildingId != 0) continue;
                var gpos = new SimVec2(gb.Pos.Col + 0.5f, gb.Pos.Row + 0.5f);
                if (SimVec2.Distance(g.PosF, gpos) > SimCore.BuildingCollisionRadius(gb) + 8f) continue; // 未到位
                var pending = new List<Entity>();
                foreach (var x in sim.Entities.Values)
                    if (x.IsSoldier && x.GeneralId == g.Id && x.TargetBuildingId == 0
                        && x.State != EntityState.Dead && x.State != EntityState.Routing
                        && x.State != EntityState.Exchanging && x.GarrisonBuildingId == 0)
                        pending.Add(x);
                if (pending.Count > 0 && TryPlanOrders(sim, pending, gb, out var gplan))
                    CommitOrders(sim, gb.Id, gplan); // 主将城外督战,士兵入场;增援到位可再补
            }

            // 1) 到达判定(保留原全单位语义:孤将亦可独立攻/占;士兵由第 0 步放行后同样到达)
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
