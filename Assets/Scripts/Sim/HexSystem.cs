using System;
using System.Collections.Generic;
using System.Linq;

namespace IronCurtain.Sim
{
    public enum HexDrawError : byte
    {
        None, InvalidState, InvalidSide, MatchEnded, NotInDrawWindow,
        NodeConsumed, PendingChoiceExists, InsufficientEp
    }

    public enum HexChooseError : byte
    {
        None, InvalidState, InvalidSide, NoPendingChoice, ChoiceNotOffered
    }

    public enum HexUseError : byte
    {
        None, InvalidState, InvalidSide, CardNotHeld, InvalidTarget, NoValidPlacement
    }

    public readonly struct HexDrawResult
    {
        public bool Success => Error == HexDrawError.None;
        public HexDrawError Error { get; }
        public int NodeMinute { get; }
        public IReadOnlyList<HexId> Choices { get; }

        internal HexDrawResult(HexDrawError error, int nodeMinute = 0, HexId[] choices = null)
        {
            Error = error;
            NodeMinute = nodeMinute;
            Choices = choices ?? Array.Empty<HexId>();
        }
    }

    /// <summary>海克斯系统 + 道具。全部运行态归属 SimCore；抽取、选择、释放是三个原子阶段。</summary>
    public static class HexSystem
    {
        private static HexRuntimeState Rt(SimCore sim) => sim?.HexRuntime;

        private static HexRuntimeState.SideState St(SimCore sim, Side side) => Rt(sim).Of(side);
        private static int Ticks(SimCore sim, double seconds) =>
            Math.Max(1, (int)Math.Ceiling(seconds * sim.Config.TickRate));

        // ---------- 查询 ----------
        public static HexId? ActiveOf(SimCore sim, Side side) =>
            sim == null || side == Side.Neutral ? (HexId?)null : St(sim, side).Active;

        public static float ActiveRemainingOf(SimCore sim, Side side)
        {
            if (sim == null || sim.Config == null || side == Side.Neutral) return 0f;
            var st = St(sim, side);
            if (st.Active == null || st.ActiveExpiresTick == long.MaxValue) return 0f;
            return (float)Math.Max(0, st.ActiveExpiresTick - sim.Tick) / sim.Config.TickRate;
        }

        public static IReadOnlyList<HexId> PendingChoicesOf(SimCore sim, Side side)
        {
            if (sim == null || side == Side.Neutral) return Array.Empty<HexId>();
            return St(sim, side).PendingChoices.ToArray();
        }

        public static IReadOnlyList<HexId> HandOf(SimCore sim, Side side)
        {
            if (sim == null || side == Side.Neutral) return Array.Empty<HexId>();
            return St(sim, side).Hand.ToArray();
        }

        public static int PendingNodeMinuteOf(SimCore sim, Side side) =>
            sim == null || side == Side.Neutral ? 0 : St(sim, side).PendingNodeMinute;

        [Obsolete("Use ActiveOf(SimCore, Side). A match context is required for per-Sim state.")]
        public static HexId? ActiveOf(Side side) => null;

        [Obsolete("Use ActiveRemainingOf(SimCore, Side). A match context is required for per-Sim state.")]
        public static float ActiveRemainingOf(Side side) => 0f;

        public static void Reset(SimCore sim)
        {
            if (sim == null) return;
            var forts = sim.HexRuntime.Forts;
            for (int i = forts.Count - 1; i >= 0; i--)
            {
                var entity = sim.GetEntity(forts[i].EntityId);
                if (entity != null)
                {
                    ReleaseGarrison(sim, entity);
                    sim.RemoveEntity(entity.Id);
                }
            }
            sim.HexRuntime.Clear();
        }

        [Obsolete("Per-match state is initialized by SimCore. Use Reset(SimCore) only when reusing a SimCore.")]
        public static void Reset() { }

        // ---------- 扣费抽取 -> 三选一 -> 手牌 ----------
        public static bool CanDraw(SimCore sim, Side side)
        {
            int node;
            return ValidateDraw(sim, side, out node) == HexDrawError.None;
        }

        public static HexDrawResult TryDraw(SimCore sim, Side side)
        {
            int node;
            var error = ValidateDraw(sim, side, out node);
            if (error != HexDrawError.None) return new HexDrawResult(error, node);

            var st = St(sim, side);
            if (!sim.SpendEp(side, sim.Game.Ep.HexDrawCost))
                return new HexDrawResult(HexDrawError.InsufficientEp, node);

            // 校验已全部通过：从此处开始一次性提交节点、RNG 结果和待选择状态。
            var choices = BuildChoices(sim, st);
            st.ConsumedNodesMask |= 1 << node;
            st.PendingNodeMinute = node;
            st.PendingChoices.Clear();
            st.PendingChoices.AddRange(choices);
            return new HexDrawResult(HexDrawError.None, node, choices.ToArray());
        }

        public static HexChooseError TryChoose(SimCore sim, Side side, HexId choice)
        {
            if (sim == null || sim.Game == null) return HexChooseError.InvalidState;
            if (side == Side.Neutral) return HexChooseError.InvalidSide;
            var st = St(sim, side);
            if (st.PendingChoices.Count == 0) return HexChooseError.NoPendingChoice;
            if (!st.PendingChoices.Contains(choice)) return HexChooseError.ChoiceNotOffered;
            st.Hand.Add(choice); // List 保留重复卡语义。
            st.PendingChoices.Clear();
            st.PendingNodeMinute = 0;
            return HexChooseError.None;
        }

        private static HexDrawError ValidateDraw(SimCore sim, Side side, out int nodeMinute)
        {
            nodeMinute = 0;
            if (sim == null || sim.Game == null || sim.Game.Hexes == null || sim.Game.Hexes.Length < 3)
                return HexDrawError.InvalidState;
            var ids = new HashSet<HexId>();
            foreach (var hex in sim.Game.Hexes)
            {
                int id = (int)hex.Id;
                if (id < 0 || id >= 32 || !Enum.IsDefined(typeof(HexId), hex.Id) || !ids.Add(hex.Id))
                    return HexDrawError.InvalidState;
            }
            var nodes = sim.Game.Hexes[0].DrawNodes;
            if (nodes == null || nodes.Any(n => n <= 0 || n >= 31)) return HexDrawError.InvalidState;
            if (side == Side.Neutral) return HexDrawError.InvalidSide;
            if (sim.Phase == MatchPhase.Ended) return HexDrawError.MatchEnded;

            nodeMinute = CurrentDrawNode(sim);
            if (nodeMinute == 0) return HexDrawError.NotInDrawWindow;
            var st = St(sim, side);
            if ((st.ConsumedNodesMask & (1 << nodeMinute)) != 0) return HexDrawError.NodeConsumed;
            if (st.PendingChoices.Count > 0) return HexDrawError.PendingChoiceExists;
            if (sim.EpOf(side) < sim.Game.Ep.HexDrawCost) return HexDrawError.InsufficientEp;
            return HexDrawError.None;
        }

        private static int CurrentDrawNode(SimCore sim)
        {
            var first = sim.Game.Hexes[0];
            if (sim.Tick < (long)first.UnlockMinute * 60 * sim.Config.TickRate) return 0;
            foreach (var minute in first.DrawNodes)
            {
                long start = (long)minute * 60 * sim.Config.TickRate;
                long end = start + 10L * sim.Config.TickRate;
                if (sim.Tick >= start && sim.Tick < end) return minute;
            }
            return 0;
        }

        private static List<HexId> BuildChoices(SimCore sim, HexRuntimeState.SideState st)
        {
            var pool = sim.Game.Hexes.ToList();
            var picked = new List<HexId>(3);
            ulong randomBits = sim.Rng.NextUInt64(); // 一次抽取只推进全局 RNG 一次。
            while (picked.Count < 3 && pool.Count > 0)
            {
                double total = 0;
                var weights = new double[pool.Count];
                for (int i = 0; i < pool.Count; i++)
                {
                    bool used = (st.UsedCardsMask & (1 << (int)pool[i].Id)) != 0;
                    double configured = used ? pool[i].UsedWeightPenalty : 1.0;
                    weights[i] = configured > 0 && !double.IsNaN(configured) ? configured : 0;
                    total += weights[i];
                }
                if (total <= 0)
                {
                    for (int i = 0; i < weights.Length; i++) weights[i] = 1.0;
                    total = weights.Length;
                }
                int shift = picked.Count * 21;
                double unit = ((randomBits >> shift) & 0x1FFFFFUL) * (1.0 / 0x200000UL);
                double r = unit * total;
                int idx = pool.Count - 1;
                double acc = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    acc += weights[i];
                    if (r < acc) { idx = i; break; }
                }
                picked.Add(pool[idx].Id);
                pool.RemoveAt(idx);
            }
            return picked;
        }

        [Obsolete("Use TryDraw. This compatibility method now charges EP and persists the pending choices.")]
        public static List<HexId> DrawChoices(SimCore sim, Side side) => TryDraw(sim, side).Choices.ToList();

        // ---------- 从手牌释放 ----------
        public static HexUseError TryUseHeld(SimCore sim, Side side, HexId card, int targetUnitId = 0)
        {
            if (sim == null || sim.Game == null) return HexUseError.InvalidState;
            if (side == Side.Neutral) return HexUseError.InvalidSide;
            var st = St(sim, side);
            int handIndex = st.Hand.IndexOf(card);
            if (handIndex < 0) return HexUseError.CardNotHeld;

            Entity sandbagTarget = null;
            GridPos? fortCell = null;
            if (card == HexId.Sandbag)
            {
                sandbagTarget = sim.GetEntity(targetUnitId);
                if (sandbagTarget == null || sandbagTarget.Side != side || sandbagTarget.Kind != EntityKind.Unit
                    || sandbagTarget.State == EntityState.Dead)
                    return HexUseError.InvalidTarget;
            }
            if (card == HexId.IronWall)
            {
                fortCell = FindFreeCellNear(sim, side);
                if (!fortCell.HasValue) return HexUseError.NoValidPlacement;
            }

            var cfg = sim.Game.Hexes.FirstOrDefault(h => h.Id == card);
            if (cfg == null) return HexUseError.InvalidState;

            // 所有失败点已通过：只消耗一张手牌，再覆盖旧效果。
            st.Hand.RemoveAt(handIndex);
            Deactivate(sim, side);
            st.Active = card;
            st.ActivatedTick = sim.Tick;
            st.ActiveExpiresTick = cfg.DurationSeconds <= 0
                ? long.MaxValue
                : sim.Tick + Ticks(sim, cfg.DurationSeconds);
            st.UsedCardsMask |= 1 << (int)card;

            if (card == HexId.IronWall)
            {
                var f = sim.SpawnEntity(side, EntityKind.Building, fortCell.Value);
                f.IsFort = true;
                f.Building = null;
                f.Hp = f.MaxHp = sim.Game.Tools.FortHp;
                Rt(sim).Forts.Add(new HexRuntimeState.FortEntry
                {
                    EntityId = f.Id, Side = side, FromHexCard = true,
                    ExpireTick = sim.Tick + Ticks(sim, sim.Game.Tools.FortDurationSeconds),
                    NextPulseTick = sim.Tick + Ticks(sim, 3.0)
                });
            }
            if (card == HexId.Sandbag)
            {
                st.SandbagUnitId = sandbagTarget.Id;
                st.SandbagExpiresTick = sim.Tick + Ticks(sim, sim.Game.Tools.FortDurationSeconds);
            }
            return HexUseError.None;
        }

        [Obsolete("Use TryUseHeld. Releasing a hex no longer spends EP and requires the card in hand.")]
        public static bool UseHex(SimCore sim, Side side, HexId card, int targetUnitId, out string error)
        {
            var result = TryUseHeld(sim, side, card, targetUnitId);
            error = UseErrorText(result);
            return result == HexUseError.None;
        }

        private static string UseErrorText(HexUseError error)
        {
            switch (error)
            {
                case HexUseError.None: return null;
                case HexUseError.CardNotHeld: return "未持有该海克斯";
                case HexUseError.InvalidTarget: return "目标无效";
                case HexUseError.NoValidPlacement: return "无可用放置位置";
                case HexUseError.InvalidSide: return "阵营无效";
                default: return "海克斯状态无效";
            }
        }

        private static void Deactivate(SimCore sim, Side side)
        {
            var st = St(sim, side);
            var active = st.Active;
            st.Active = null;
            st.ActiveExpiresTick = 0;
            st.VengeanceExpiryTicks.Clear();
            st.SandbagUnitId = 0;
            st.SandbagExpiresTick = 0;

            if (active != HexId.IronWall) return;
            var forts = Rt(sim).Forts;
            for (int i = forts.Count - 1; i >= 0; i--)
            {
                var f = forts[i];
                if (!f.FromHexCard || f.Side != side) continue;
                var entity = sim.GetEntity(f.EntityId);
                if (entity != null)
                {
                    ReleaseGarrison(sim, entity);
                    sim.RemoveEntity(entity.Id);
                }
                forts.RemoveAt(i);
            }
        }

        // ---------- 计时 ----------
        public static void Tick(SimCore sim, float dt)
        {
            if (sim == null || sim.Game == null) return;
            Rt(sim);
            foreach (var side in new[] { Side.Blue, Side.Red })
            {
                var st = St(sim, side);
                st.VengeanceExpiryTicks.RemoveAll(t => t <= sim.Tick);
                if (st.Active != null && st.ActiveExpiresTick != long.MaxValue && sim.Tick >= st.ActiveExpiresTick)
                    Deactivate(sim, side);
                if (st.SandbagUnitId != 0 && sim.Tick >= st.SandbagExpiresTick)
                    st.SandbagUnitId = 0;
            }

            var forts = Rt(sim).Forts;
            for (int i = forts.Count - 1; i >= 0; i--)
            {
                var f = forts[i];
                var b = sim.GetEntity(f.EntityId);
                if (b == null || b.Rubbled) { forts.RemoveAt(i); continue; }
                if (sim.Tick >= f.NextPulseTick)
                {
                    f.NextPulseTick += Ticks(sim, 3.0);
                    foreach (var e in sim.Entities.Values)
                    {
                        if (e.Side == b.Side || e.Kind != EntityKind.Unit || e.State == EntityState.Dead) continue;
                        if (SimVec2.Distance(e.PosF, SimVec2.FromGrid(b.Pos)) <= 2f)
                        {
                            e.Hp -= (float)sim.Game.Tools.RamDamage;
                            if (e.Hp <= 0) { e.Hp = 0; e.State = EntityState.Dead; }
                        }
                    }
                }
                if (sim.Tick >= f.ExpireTick)
                {
                    ReleaseGarrison(sim, b);
                    sim.RemoveEntity(b.Id);
                    forts.RemoveAt(i);
                }
            }
        }

        private static void ReleaseGarrison(SimCore sim, Entity fort)
        {
            foreach (var id in fort.GarrisonFormal.Concat(fort.GarrisonTemp).ToList())
            {
                var u = sim.GetEntity(id);
                if (u == null) continue;
                u.GarrisonBuildingId = 0;
                u.TempGarrisonId = 0;
                u.State = EntityState.Idle;
                var free = FindFreeCellNear(sim, fort.Pos);
                if (free.HasValue)
                {
                    u.Pos = free.Value;
                    u.PosF = SimVec2.FromGrid(free.Value);
                }
            }
            fort.GarrisonFormal.Clear();
            fort.GarrisonTemp.Clear();
        }

        // ---------- 效果查询 ----------
        public static float MoveMultiplierOf(SimCore sim, Entity e)
        {
            float m = 1f;
            if (sim == null || e == null) return m;
            var st = St(sim, e.Side);
            if (st.Active == HexId.SteelTide && e.Hp >= 100) m *= 1.5f;
            if (st.Active == HexId.LightMobility && NearOwnBuilding(sim, e, 10)) m *= 2f;
            if (st.Active == HexId.HoldFast && NearOwnBuilding(sim, e, 10)) m *= 1.1f;
            if (st.Active == HexId.Vengeance) m *= 1f + 0.05f * st.VengeanceExpiryTicks.Count;
            return m;
        }

        public static float OutputBonusOf(SimCore sim, Entity e)
        {
            float b = 0f;
            if (sim == null || e == null) return b;
            var st = St(sim, e.Side);
            if (st.Active == HexId.Vengeance) b += 0.05f * st.VengeanceExpiryTicks.Count;
            if (st.Active == HexId.HoldFast && NearOwnBuilding(sim, e, 10)) b += 0.10f;
            if (st.SandbagUnitId == e.Id) b -= 0.5f;
            return b;
        }

        public static float DamageTakenBonusOf(SimCore sim, Entity e)
        {
            float b = 0f;
            if (sim == null || e == null) return b;
            var st = St(sim, e.Side);
            if (st.Active == HexId.Vengeance) b -= 0.05f * st.VengeanceExpiryTicks.Count;
            if (st.Active == HexId.HoldFast && NearOwnBuilding(sim, e, 10)) b -= 0.10f;
            return b;
        }

        public static float DamageReductionOf(SimCore sim, Entity e)
        {
            float r = 1f;
            if (sim == null || sim.Config == null || e == null) return r;
            var st = St(sim, e.Side);
            if (st.Active == HexId.SteelTide && sim.Tick - st.ActivatedTick < Ticks(sim, 3.0)) r *= 0.5f;
            if (st.SandbagUnitId == e.Id) r *= 0.5f;
            return r;
        }

        public static double ProductionSecondsMultiplier(SimCore sim, Side side) =>
            sim != null && side != Side.Neutral && St(sim, side).Active == HexId.RapidDeploy ? 1.0 / 1.4 : 1.0;

        public static int PopCapBonusOf(SimCore sim, Side side)
        {
            if (sim == null || sim.Game == null || side == Side.Neutral) return 0;
            if (St(sim, side).Active != HexId.RapidDeploy) return 0;
            int baseCap = (int)sim.Game.Production.BasePopulation
                        + sim.Game.Production.ResourcePointPopulation * sim.OwnedResourcePointsOf(side);
            return (int)Math.Round(baseCap * 0.20);
        }

        public static bool TowerExempt(SimCore sim, Side side) =>
            sim != null && side != Side.Neutral && St(sim, side).Active == HexId.HiddenPath;
        public static bool DebuffImmune(SimCore sim, Entity e) =>
            sim != null && e != null && St(sim, e.Side).Active == HexId.SteelTide;

        public static void OnUnitDied(SimCore sim, Side side)
        {
            if (sim == null || sim.Config == null || side == Side.Neutral) return;
            var st = St(sim, side);
            if (st.Active == HexId.Vengeance)
                st.VengeanceExpiryTicks.Add(sim.Tick + Ticks(sim, 15.0));
        }

        // ---------- 道具 ----------
        public static bool UseRam(SimCore sim, Side side, int buildingId, out string error)
        {
            error = null;
            if (sim == null || sim.Game == null) { error = "海克斯状态无效"; return false; }
            if (side == Side.Neutral) { error = "阵营无效"; return false; }
            if (!sim.Entities.TryGetValue(buildingId, out var b) || b.Kind != EntityKind.Building)
            { error = "目标无效"; return false; }
            if (b.Rubbled || b.Side == Side.Neutral || b.Side == side)
            { error = "目标必须是敌方建筑"; return false; }
            if (!sim.SpendEp(side, sim.Game.Ep.RamCost)) { error = "能量不足"; return false; }
            DestroySystem.ApplyBuildingDamage(sim, b, (float)sim.Game.Tools.RamDamage, side);
            return true;
        }

        public static bool UseFort(SimCore sim, Side side, GridPos cell, out string error)
        {
            error = null;
            if (sim == null || sim.Game == null) { error = "海克斯状态无效"; return false; }
            if (side == Side.Neutral) { error = "阵营无效"; return false; }
            if (cell.Row < 0 || cell.Row >= LaneGrid.Rows || cell.Col < 0 || cell.Col >= LaneGrid.Cols)
            { error = "目标无效:格子越界"; return false; }
            if (CellOccupied(sim, cell)) { error = "目标无效:格子被占用"; return false; }
            if (!sim.SpendEp(side, sim.Game.Ep.FortCost)) { error = "能量不足"; return false; }
            var f = sim.SpawnEntity(side, EntityKind.Building, cell);
            f.IsFort = true;
            f.Building = null;
            f.Hp = f.MaxHp = sim.Game.Tools.FortHp;
            Rt(sim).Forts.Add(new HexRuntimeState.FortEntry
            {
                EntityId = f.Id, Side = side, FromHexCard = false,
                ExpireTick = sim.Tick + Ticks(sim, sim.Game.Tools.FortDurationSeconds),
                NextPulseTick = sim.Tick + Ticks(sim, 3.0)
            });
            return true;
        }

        // ---------- 工具 ----------
        private static bool NearOwnBuilding(SimCore sim, Entity e, int range)
        {
            foreach (var b in sim.Entities.Values)
                if (b.Kind == EntityKind.Building && b.Side == e.Side && !b.Rubbled
                    && SimVec2.Distance(e.PosF, SimVec2.FromGrid(b.Pos)) <= range)
                    return true;
            return false;
        }

        private static bool CellOccupied(SimCore sim, GridPos cell)
        {
            foreach (var e in sim.Entities.Values)
            {
                if (e.Pos.Row != cell.Row || e.Pos.Col != cell.Col) continue;
                // SpawnEntity keeps a building's grid reservation while it is rubble.
                // Match that authority here so a failed fort spawn cannot occur after EP spend.
                if (e.Kind == EntityKind.Building) return true;
                if (e.State != EntityState.Dead && e.GarrisonBuildingId == 0) return true;
            }
            return false;
        }

        private static GridPos? FindFreeCellNear(SimCore sim, GridPos anchor)
        {
            for (int d = 0; d <= 10; d++)
            {
                for (int dr = 0; dr < LaneGrid.Rows; dr++)
                {
                    var c = new GridPos(dr, anchor.Col + d);
                    if (c.Col > LaneGrid.Cols - 1) break;
                    if (!CellOccupied(sim, c)) return c;
                    var c2 = new GridPos(dr, anchor.Col - d);
                    if (c2.Col >= 0 && !CellOccupied(sim, c2)) return c2;
                }
            }
            return null;
        }

        private static GridPos? FindFreeCellNear(SimCore sim, Side side)
        {
            var home = sim.Entities.Values
                .Where(e => e.Side == side && e.Kind == EntityKind.Building
                            && e.Building == BuildingId.Base && !e.Rubbled)
                .OrderBy(e => e.Id)
                .FirstOrDefault();
            GridPos basePos = home != null
                ? home.Pos
                : (side == Side.Blue ? new GridPos(3, 3) : new GridPos(3, LaneGrid.Cols - 4));
            for (int d = 1; d <= LaneGrid.Cols - 2; d++)
            {
                int col = basePos.Col + (side == Side.Blue ? d : -d);
                if (col < 0 || col > LaneGrid.Cols - 1) break;
                for (int rowDistance = 0; rowDistance < LaneGrid.Rows; rowDistance++)
                {
                    int upper = basePos.Row + rowDistance;
                    if (upper < LaneGrid.Rows)
                    {
                        var c = new GridPos(upper, col);
                        if (!CellOccupied(sim, c)) return c;
                    }
                    if (rowDistance == 0) continue;
                    int lower = basePos.Row - rowDistance;
                    if (lower >= 0)
                    {
                        var c = new GridPos(lower, col);
                        if (!CellOccupied(sim, c)) return c;
                    }
                }
            }
            return null;
        }
    }
}
