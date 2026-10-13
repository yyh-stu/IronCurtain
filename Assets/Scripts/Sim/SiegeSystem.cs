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

    /// <summary>M3.1 建筑占领核心:意图/入城交换。2026-09-11 废除封城、2026-09-14 删除已废的 `Sealed` 字段。M8.3a:交换按交换力,主将不因交换阵亡。M8.1b:FrontCell 占地矩形敌侧缘外 1 格。</summary>
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
            // 2026-09-14 制作人口径「主将和他的兵团就是一个单位」:士兵打光的**孤将**同样可以下攻城令
            // (它自己进城,按交换力 10/12 结算)。原实现只要"展开出的士兵数为 0"就拒单,与 :359 的
            // 「主将一个人也是这个单位」、:452「孤将亦可独立攻占」自相矛盾。
            bool anyGeneral = false;
            if (selectedIds != null)
                foreach (int id in selectedIds)
                    if (sim.Entities.TryGetValue(id, out var sel) && IsEligibleGeneral(sel, issuer)) { anyGeneral = true; break; }
            if (soldiers.Count == 0 && !anyGeneral)
                return new SiegeOrderResult(SiegeOrderError.NoEligibleSoldiers);
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
                // 2026-09-15 制作人:"还是不攻击建筑,全在发呆"。**摧毁令**下主将也是打手,
                // 集结点必须落在**它的攻击距离内**;旧口径一律用"碰撞半径+5"(箭塔=7.4 米),
                // 而近战要贴到占地矩形边(≈2.5 米)→ 到了 7.4 米就停住,永远打不到 = 发呆。
                // 占领令(及默认)保持城外 5 米待命:那一档要的是"在城外等放兵/进城",不是贴墙。
                var staging = b.Intent == SiegeIntent.Destroy
                    ? AttackStandoff(b, g, bPos, dir)
                    : TerrainMap.ClampToEllipse(bPos + dir * (SimCore.BuildingCollisionRadius(b) + 5f));
                g.TargetBuildingId = b.Id; // 兵团攻城标记(到位放兵用;主将与兵团同一单位)
                sim.SetMoveTarget(g.Id, staging.ToGrid(), true);
            }
        }

        /// <summary>
        /// 摧毁令下的集结点:量到"该单位打得到"的距离。
        /// 近战 = 占地矩形边再外推 0.5 米(量化为格后距边 0.1~0.6 米,在 `MeleeBuildingReach` 内);
        /// 远程 = 射程内沿(留 1 米余量,且不超过旧的"碰撞半径+5"待命距离)。
        /// </summary>
        private static SimVec2 AttackStandoff(Entity b, Entity g, SimVec2 bPos, SimVec2 dir)
        {
            if (DamageCalculator.IsRanged(g) && g.Range > 1f)
            {
                float want = System.Math.Min(SimCore.BuildingCollisionRadius(b) + 5f,
                                             System.Math.Max(1.5f, g.Range - 1f));
                return TerrainMap.ClampToEllipse(bPos + dir * want);
            }
            // 近战:从建筑中心朝本单位一侧,取矩形最近边点再外推 0.5 米
            var from = bPos + dir * (SimCore.BuildingCollisionRadius(b) + 1f);
            var edge = BuildingFootprint.ClosestPoint(b, from);
            var outv = (edge - bPos);
            outv = outv.Magnitude > 0.01f ? outv.Normalized() : dir;
            return TerrainMap.ClampToEllipse(edge + outv * 0.5f);
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
            // 2026-09-11 制作人废除封城;2026-09-14 把已废的 `Sealed` 字段与最后两处读取一并删干净。
        }

        static void Capture(SimCore sim, Entity b)
        {
            b.Side = b.IntentIssuer;
            // 刚占领:3 秒内不被即时夺回(防"占下来又被翻回去"来回翻面)。
            // 2026-09-16 三十七轮:常量与即时占领路径共用(InstantCaptureSystem.FreshCaptureProtectTicks)
            b.CapturedUntilTick = sim.Tick + InstantCaptureSystem.FreshCaptureProtectTicks;
            if (b.Hp <= 0 && b.MaxHp <= 0)
            {
                var cfg = sim.BuildingConfigOf(b.Building.Value);
                b.MaxHp = cfg.MaxHp;
                b.Hp = cfg.MaxHp;
            }
            CityAttackers.Remove(b.Id);
            // 2026-09-14 制作人实机报障修复(一):目标达成 → 清意图(D23「目标完成→原地待命」,
            // 写法与 InstantCaptureSystem 的占领一致)。原来不清的后果很重:建筑已归我方、
            // Intent 却仍是 Capture,于是**后到的自家士兵**继续被第 1 步 `EnterCity` 拉进城里,
            // 和自己的自动驻军按 1:1 互换 —— 兵被自己的城一个一个吃掉。
            b.Intent = SiegeIntent.None;
            b.IntentIssuer = Side.Neutral;
            GarrisonSystem.OnCityCaptured(sim, b); // 旧主驻军处置(士兵只清登记不溃逃)

            // 2026-09-14 制作人实机报障修复(二):【全量复位】挂在被占领建筑上的单位。
            // 现象:「占领对方箭塔后把军队派出,三个将只剩一个,剩的那个也只有几个兵」。
            // 根因:复位原来只覆盖 `survivors`(交换结束后还留在 atk 队列里的人),而 `EnterCity`
            // 把每一个进城单位都置为 `Exchanging`;该状态被**全系统**跳过(移动/兵团跟随/索敌/
            // 被塔攻击/阵型/AI 一律 continue),外加 `FreeCell` 已离格,于是这些人既不能指挥也
            // 不能再战,变成永久僵死的幽灵——玩家看到的就是"将不见了、不跟着走"。
            // 正确写法沿用 DestroySystem(建筑被拆/中立化时按 TargetBuildingId 统一复位)。
            var returnees = new List<int>();
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Unit || e.TargetBuildingId != b.Id) continue;
                e.TargetBuildingId = 0;
                // 2026-09-15 制作人:"断峰我下令攻击下路的箭塔,他和他的兵全部在箭塔那里发呆" ——
                // 真因:那座塔**没有守军**,攻方一贴上去就按「无人建筑接触即占领」易主,而这里原来把
                // 移动目标与状态一起清掉 → 全军**原地停住**不动 = 看起来"发呆"。
                // 现在:只摘攻城标记,**保留移动目标并保持 Moving** → 部队继续走到他们被派去的建筑那儿待命。
                e.State = e.HasMoveTarget ? EntityState.Moving : EntityState.Idle;
                returnees.Add(e.Id);
            }

            // 2026-09-14 制作人实机报障修复(三)+二次口径:自动驻军按**整团**留。
            AutoGarrisonWholeRegiment(sim, b, returnees);
        }

        /// <summary>
        /// 占领后的自动驻军(2026-09-14 制作人二次口径):
        /// 「每个军团**要么全留,要么全出**,不要主将出去兵还在城里」。
        ///
        /// 所以自动驻军的单位是**整个兵团**,不是散兵:
        /// - 只有当一个兵团的**主将和它的全部存活士兵都在攻方队列里**(没有任何一个兵被留在别处)时,
        ///   这个团才整团留城(主将一起进城守城);只要有一个兵不在场,该团整团出城。
        /// - 最多留**一个**完整兵团守城(其余随军出城),保证攻下城池不会把整支军队留在城里。
        /// - 驻军**不限量**(2026-09-11 制作人已废除容量判定),因此这里不做名额裁剪——
        ///   裁剪必然产生"半个团",而那正是本轮要消灭的状态。
        ///
        /// 由此"主将在城外、兵被关在城里"成为**不可达状态**:自动驻军要么是一整团(将+兵),
        /// 要么一个都不留。玩家要驻军走面板 [驻军] → `EnterFormal`(同样是"主将+其士兵"整批原子入驻)。
        /// </summary>
        static void AutoGarrisonWholeRegiment(SimCore sim, Entity b, List<int> returnees)
        {
            if (b.IsFort) return;   // 堡垒只可摧毁、不可驻军(2026-09-11 口径)
            // 2026-09-14 制作人口径:「在驻军、交换这种情况下,**就计算在场的人**」——
            // `returnees` 含"还挂着标记但仍在路上(被建筑卡住)的单位";这里只认**已到场**的
            // (`!HasMoveTarget`,与到达判定同源)。有兵没到场 → 这团不算完整 → 整团出城,不许"隔空入城"。
            // "是否到场"改用**距离**判定(不依赖 `HasMoveTarget` 这类移动记账):在建筑碰撞半径 +3 米内
            // 就算到场。这样"占领后保留移动目标(让部队继续走到目标建筑、不半路发呆)"与"整团口径"不再互相打架。
            var inTown = new HashSet<int>();
            float arriveRad = SimCore.BuildingCollisionRadius(b) + 3f;
            foreach (var id in returnees)
            {
                var u = sim.GetEntity(id);
                if (u != null && SimVec2.Distance(u.PosF, b.PosF) <= arriveRad) inTown.Add(id);
            }
            var sorted = new List<int>(returnees);
            sorted.Sort();          // 按 Id 升序:同样输入 → 同样留城的团(测试可复现)
            foreach (var id in sorted)
            {
                var g = sim.GetEntity(id);
                if (g == null || g.IsSoldier) continue;      // 决策单位是主将,士兵随其主将整体进出
                var regiment = new List<int> { g.Id };
                bool whole = true;
                foreach (var kv in sim.Entities)
                {
                    var s = kv.Value;
                    if (s == null || !s.IsSoldier || s.GeneralId != g.Id) continue;
                    if (!inTown.Contains(s.Id)) { whole = false; break; }   // 有兵在别处 → 半团 → 整团出城
                    regiment.Add(s.Id);
                }
                if (!whole) continue;
                // 2026-09-14 口径删除:不再排除"兵打光只剩主将"的一团 ——
                // 「主将和他的兵团就是一个单位」,主将一个人也是这个单位,照样整团留城。
                foreach (var rid in regiment)
                {
                    b.GarrisonFormal.Add(rid);
                    var u = sim.GetEntity(rid);
                    if (u != null) u.GarrisonBuildingId = b.Id;
                }
                return;
            }
        }

        /// <summary>
        /// 交换力(按单位类型,攻守同表 —— GDD 2.5.1):主将 10 / 镇岳主将 12 / 士兵 1 / 镇岳士兵 2。
        /// 2026-09-14 修正:本函数原来只认士兵(主将落到兜底 1),于是**守城的主将只值 1 点**——
        /// 与"主将有兵力值、主将和兵团是一个单位"的口径自相矛盾(一个攻方小兵 1 点就能换掉守城主将)。
        /// 现按同一张表取值,攻方的 10/12 点攻入城里时,守方主将也是 10/12 点。
        /// </summary>
        static int ExchangeWorth(Entity u)
        {
            if (u == null) return 1;
            bool zhenYue = u.Hero == HeroId.ZhenYue;
            if (!u.IsSoldier) return zhenYue ? 12 : 10;   // 主将
            return zhenYue ? 2 : 1;                       // 士兵
        }

        static void KillDefenders(SimCore sim, Entity b, int power, Entity killer)
        {
            // 交换只消耗正式驻军,临时驻军受保护(用户拍板,覆盖 GDD 2.4.1)
            // 2026-09-11:按交换力扣——普通守兵 1 力/镇岳守兵 2 力,攻方一轮 power 点
            int killed = 0;
            while (power > 0 && b.GarrisonFormal.Count > 0)
            {
                var id = b.GarrisonFormal[0];
                b.GarrisonFormal.RemoveAt(0);
                var victim = sim.GetEntity(id);   // 先取后删:按受害者的交换力扣(镇岳兵 2/守将 10~12)
                sim.RemoveEntity(id);
                killed++;
                power -= ExchangeWorth(victim);
            }
            if (killed > 0 && killer != null)
            {
                var hero = killer.Hero ?? HeroId.PanShi;
                MatchStats.AddAlleyKill(killer.Side, hero, killed);   // 巷战杀敌归进城方主将
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
                    CommitOrders(sim, gb.Id, gplan); // 主将带兵团到位后放兵入场;增援到位可再补
            }

            // 0b) 2026-09-14 补漏(实机 `IC_ACCEPT=all` 抓到:占领后仍有 8 个单位挂着该塔为目标):
            //     `Capture` 的全量复位只能扫到"那一刻已挂着标记"的单位;而**占领之后**才被放行入场的士兵
            //     (第 0 步放兵)仍会带上该建筑的目标标记 —— 此时建筑的意图已清空,谁都清不掉它 ✗。
            //     这里每 tick 兜一道:**意图为空(或建筑没了/成废墟)的建筑,其目标单位一律摘标记**。
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Unit || e.TargetBuildingId == 0) continue;
                if (!TryGetBuilding(sim, e.TargetBuildingId, out var tb) || tb.Rubbled || tb.Intent == SiegeIntent.None)
                {
                    e.TargetBuildingId = 0;
                    if (e.State != EntityState.Exchanging) e.State = EntityState.Idle;
                    e.HasMoveTarget = false;
                }
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
                if (b.Intent == SiegeIntent.Capture)
                {
                    if (b.GarrisonFormal.Count + b.GarrisonTemp.Count > 0) EnterCity(sim, b, u);
                    else Capture(sim, b);
                }
                else if (b.Intent == SiegeIntent.Destroy)
                {
                    u.State = EntityState.Attacking; // 拆塔结算在 M3.4
                }
            }

            // 2) 交换结算:每 10 tick 一轮。2026-09-11 制作人新口径——按**交换力**:
            //    进城将领=10(镇岳 12,自己不死,一轮换掉等值守军);镇岳军团士兵=2(1:2),普通士兵=1。
            //    2026-09-11 制作人废除封城:有攻方城内单位即结算。
            if (sim.Tick % 10 != 0) return;
            var battleBuildings = new List<Entity>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Building && AttackersOf(e.Id).Count > 0)
                    battleBuildings.Add(e);   // 2026-09-15:守军为空也要进循环 → 立刻走 Capture(否则攻方停在 Exchanging 卡死)
            foreach (var b in battleBuildings)
            {
                var atk = AttackersOf(b.Id);
                while (atk.Count > 0 && b.GarrisonFormal.Count > 0)
                {
                    var aid = atk[0];
                    atk.RemoveAt(0); // 该单位结束城内待命(无论生死都离开交换队列)
                    var a = sim.GetEntity(aid);
                    if (a != null && !a.IsSoldier)
                    {
                        // 主将进城:一轮换掉 10(镇岳 12)点守军,主将本体不因交换阵亡。
                        // 2026-09-14 制作人:「**主将和他的兵团就是一个单位**」——主将完成这一轮后**留在城内**,
                        // 不做任何"轮完就赶出城"的处理(此前那句"主将不入城、城外待命"的口径已按此删除)。
                        // 攻城结束时统一收口:占领→`Capture` 全场复位并按整团决定留城/出城;
                        // 攻城失败→下方 `atk.Count == 0` 分支把城内攻方放回城外(否则会停在 Exchanging 僵死)。
                        KillDefenders(sim, b, ExchangeWorth(a), a);
                    }
                    else
                    {
                        KillDefenders(sim, b, ExchangeWorth(a), a); // 镇岳兵 1:2
                        KillAttacker(sim, atk, aid); // 攻方只耗士兵,主将免疫(存量语义)
                    }
                }
                if (b.GarrisonFormal.Count == 0)
                {
                    // 正式守军被肃清 → 城池易主,临时驻军按新规处置(士兵不溃逃)。
                    // 攻方复位(含交换中被消耗、仍停在 Exchanging 的人)统一由 Capture 全场扫。
                    Capture(sim, b);
                }
                else if (atk.Count == 0)
                {
                    // 攻方被肃清:意图保留,剩余守军留城
                    CityAttackers.Remove(b.Id);
                    // 2026-09-14:「主将和他的兵团是一个单位、主将可入城」→ 主将完成一轮后留在城内(见上),
                    // 于是它可能已不在 atk 队列里、State 仍是 `Exchanging`。交换**只从 atk 出人**,
                    // 队列一空就再也不会有人被结算 → 留在城里的必须收兵出城(该状态被全系统跳过,
                    // 不收就是永久僵死幽灵)。还在路上的后续部队到场后会自己进城继续结算,不受此影响。
                    foreach (var kv in sim.Entities)
                    {
                        var x = kv.Value;
                        if (x == null || x.Kind != EntityKind.Unit || x.TargetBuildingId != b.Id) continue;
                        if (x.State != EntityState.Exchanging) continue;
                        x.TargetBuildingId = 0;
                        x.State = EntityState.Idle;
                        x.HasMoveTarget = false;
                    }
                }
            }
        }
    }
}
