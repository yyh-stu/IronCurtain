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

    /// <summary>海克斯系统。全部运行态归属 SimCore；抽取、选择、释放是三个原子阶段。
    /// 2026-09-16 三十七轮:道具(攻城锥/临时阵地)与堡垒运行态拆出到 ToolsSystem,此处只留卡牌逻辑。</summary>
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

        /// <summary>激活戳(2026-09-17 契约 3R 公开):本次激活发生的 tick。
        /// Q08 修复(2026-09-18):无激活卡时返回 **-1**(旧版返回 0 与"tick0 激活"不可区分);
        /// 有激活卡时返回 ActivatedTick。消费者用"值变化"检测激活/重放/覆盖,-1→N 即首次。
        /// ⚠ 非稳定标识:同卡重复释放会刷新该值;消费者不得将其当作跨激活的持久缓存键。</summary>
        public static long ActivationStampOf(SimCore sim, Side side) =>
            sim == null || side == Side.Neutral || St(sim, side).Active == null
                ? -1 : St(sim, side).ActivatedTick;

        [Obsolete("Use ActiveOf(SimCore, Side). A match context is required for per-Sim state.")]
        public static HexId? ActiveOf(Side side) => null;

        [Obsolete("Use ActiveRemainingOf(SimCore, Side). A match context is required for per-Sim state.")]
        public static float ActiveRemainingOf(Side side) => 0f;

        public static void Reset(SimCore sim)
        {
            if (sim == null) return;
            ToolsSystem.RemoveFortsOf(sim, null); // 全清(道具堡垒,双方)
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
            if (!sim.HexFreeDraw)
                st.ConsumedNodesMask |= 1 << node;   // 训练场自由抽取:节点不记消耗(不限次数)
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
            var st = St(sim, side);
            // 2026-09-17 制作人:"训练场海克斯抽卡不限时间不限次数" —— HexFreeDraw 跳过窗口/节点两道闸
            if (!sim.HexFreeDraw)
            {
                if (nodeMinute == 0) return HexDrawError.NotInDrawWindow;
                if ((st.ConsumedNodesMask & (1 << nodeMinute)) != 0) return HexDrawError.NodeConsumed;
            }
            if (st.PendingChoices.Count > 0) return HexDrawError.PendingChoiceExists;
            if (sim.EpOf(side) < sim.Game.Ep.HexDrawCost) return HexDrawError.InsufficientEp;
            return HexDrawError.None;
        }

        /// <summary>抽取窗口长度(秒)。判定与 UI 说明共用同一常量 —— 此前是散在判定里的字面量 10。</summary>
        public const int DrawWindowSeconds = 10;

        private static int CurrentDrawNode(SimCore sim)
        {
            var first = sim.Game.Hexes[0];
            if (sim.Tick < (long)first.UnlockMinute * 60 * sim.Config.TickRate) return 0;
            foreach (var minute in first.DrawNodes)
            {
                long start = (long)minute * 60 * sim.Config.TickRate;
                long end = start + DrawWindowSeconds * sim.Config.TickRate;
                if (sim.Tick >= start && sim.Tick < end) return minute;
            }
            return 0;
        }

        /// <summary>当前是否在抽取窗口(0=不在,>0=窗口分钟节点)。UI 与判定同一来源,避免两处口径漂移。</summary>
        public static int DrawWindowMinuteOf(SimCore sim)
        {
            if (sim == null || sim.Game == null || sim.Game.Hexes == null || sim.Game.Hexes.Length == 0
                || sim.Config == null) return 0;
            return CurrentDrawNode(sim);
        }

        /// <summary>下一个抽取窗口的分钟(0=没有下一个)。2026-09-14 制作人"能看不能抽":
        /// 面板此前**从不告诉玩家有窗口这回事**,窗口外点抽取只得到一句"当前不在抽取窗口"。</summary>
        public static int NextDrawMinuteOf(SimCore sim)
        {
            if (sim == null || sim.Game == null || sim.Game.Hexes == null || sim.Game.Hexes.Length == 0
                || sim.Config == null) return 0;
            var first = sim.Game.Hexes[0];
            long tick = sim.Tick;
            if (tick < (long)first.UnlockMinute * 60 * sim.Config.TickRate) return first.UnlockMinute;
            int best = 0;
            foreach (var m in first.DrawNodes)
            {
                if ((long)m * 60 * sim.Config.TickRate + DrawWindowSeconds * sim.Config.TickRate <= tick) continue;
                if (best == 0 || m < best) best = m;
            }
            return best;
        }

        /// <summary>抽取窗口已用掉的分钟表(UI 展示"第 X/Y/Z 分钟")。</summary>
        public static int[] DrawNodesOf(SimCore sim)
        {
            if (sim == null || sim.Game == null || sim.Game.Hexes == null || sim.Game.Hexes.Length == 0)
                return new int[0];
            return sim.Game.Hexes[0].DrawNodes ?? new int[0];
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
            if (card == HexId.Sandbag)
            {
                sandbagTarget = sim.GetEntity(targetUnitId);
                if (sandbagTarget == null || sandbagTarget.Side != side || sandbagTarget.Kind != EntityKind.Unit
                    || sandbagTarget.State == EntityState.Dead)
                    return HexUseError.InvalidTarget;
            }
            // 2026-09-16 制作人拍板:铁壁防线不再生成堡垒,改为对重装军团生效的增益卡
            // (效果查询见 DamageTakenBonusOf;堡垒生成/找位逻辑已全部移除)。

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
            st.Active = null;
            st.ActiveExpiresTick = 0;
            st.VengeanceExpiryTicks.Clear();
            st.SandbagUnitId = 0;
            st.SandbagExpiresTick = 0;
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
            // 堡垒脉冲/到期已随道具拆分移至 ToolsSystem.Tick(SimCore.Step 在本系统之后逐 tick 调用)
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
            // 2026-09-16 制作人拍板:铁壁防线改为对重装军团生效 —— 承伤 -30%(数值为检测方按卡族
            // 定位的默认值:介于 以逸待劳(±10%/30s) 与 沙包(-50%/单体带输出惩罚) 之间,待制作人终审)。
            if (st.Active == HexId.IronWall && IsHeavyRegiment(sim, e)) b -= 0.30f;
            return b;
        }

        /// <summary>重装军团资格(2026-09-17 契约 3R 公开,铁壁防线表现用):
        /// 主将 Tag 以「重装」开头(磐石·重装塔盾 / 镇岳·重装攻城);军团口径 = 主将 + 其士兵整团。
        /// 纯函数:只读 Tag 与 Hero,无副作用。</summary>
        public static bool IsHeavyRegimentOf(SimCore sim, Entity e) => IsHeavyRegiment(sim, e);

        /// <summary>重装军团:主将为重装系英雄(配置 Tag 以「重装」开头:磐石·重装塔盾 / 镇岳·重装攻城)。
        /// 军团口径 = 主将 + 其士兵整团生效(士兵随主将)。</summary>
        private static bool IsHeavyRegiment(SimCore sim, Entity e)
        {
            if (e == null || sim.Game == null) return false;
            var gen = e.IsSoldier ? sim.GetEntity(e.GeneralId) : e;
            if (gen == null || gen.Hero == null) return false;
            var heroes = sim.Game.Heroes;
            int idx = (int)gen.Hero.Value;
            if (heroes == null || idx < 0 || idx >= heroes.Length) return false;
            var tag = heroes[idx].Tag;
            return tag != null && tag.StartsWith("重装", System.StringComparison.Ordinal);
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
            if (sim == null || side == Side.Neutral) return 0;
            if (St(sim, side).Active != HexId.RapidDeploy) return 0;
            // 2026-09-16 三十七轮:旧公式(BasePopulation+资源点×5)是 v1.6 单英雄口径遗留,
            // 与现行名额制(GeneralCap=8,GDD「常规对局名将上限 8(加时 +1)」)脱节;
            // 现与 PopulationCapOf 同基,并由其真正接线 —— 此前本函数无人调用,效果从未生效。
            int baseCap = sim.PopulationCapOverride ?? SimCore.GeneralCap;
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

        // ---------- 工具 ----------
        // 2026-09-16 三十七轮:道具(UseRam/UseFort)、堡垒运行态(脉冲/到期/移除)、
        // 放置与空格工具(CellOccupied/FindFreeCellNear)已整体拆至 ToolsSystem。
        private static bool NearOwnBuilding(SimCore sim, Entity e, int range)
        {
            foreach (var b in sim.Entities.Values)
                if (b.Kind == EntityKind.Building && b.Side == e.Side && !b.Rubbled
                    && SimVec2.Distance(e.PosF, SimVec2.FromGrid(b.Pos)) <= range)
                    return true;
            return false;
        }
    }
}
