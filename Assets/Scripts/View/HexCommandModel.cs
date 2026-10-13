using System;
using System.Collections.Generic;
using System.Linq;
using IronCurtain.Sim;

namespace IronCurtain.View
{
    public readonly struct HexCommandState
    {
        public readonly bool Valid;
        public readonly float Ep;
        public readonly int DrawCost;
        public readonly bool CanDraw;
        /// <summary>是否正处在抽取窗口内(2026-09-14:面板要说清"为什么现在抽不了")。</summary>
        public readonly bool InWindow;
        /// <summary>当前窗口的分钟节点(不在窗口则 0)。</summary>
        public readonly int WindowMinute;
        /// <summary>下一个窗口的分钟(无则 0)。</summary>
        public readonly int NextDrawMinute;
        /// <summary>窗口长度(秒)与窗口分钟表,供面板展示。</summary>
        public readonly int WindowSeconds;
        public readonly int[] DrawNodes;
        /// <summary>训练场自由抽取(2026-09-17):不限时不限次,面板提示与按钮可用性按此口径。</summary>
        public readonly bool FreeDraw;
        public readonly IReadOnlyList<HexId> PendingChoices;
        public readonly IReadOnlyList<HexId> Hand;
        public readonly HexId? Active;
        public readonly float ActiveSeconds;

        public HexCommandState(bool valid, float ep, int drawCost, bool canDraw,
            IReadOnlyList<HexId> pendingChoices, IReadOnlyList<HexId> hand,
            HexId? active, float activeSeconds,
            bool inWindow, int windowMinute, int nextDrawMinute, int windowSeconds, int[] drawNodes,
            bool freeDraw = false)
        {
            Valid = valid;
            Ep = ep;
            DrawCost = drawCost;
            CanDraw = canDraw;
            PendingChoices = pendingChoices;
            Hand = hand;
            Active = active;
            ActiveSeconds = activeSeconds;
            InWindow = inWindow;
            WindowMinute = windowMinute;
            NextDrawMinute = nextDrawMinute;
            WindowSeconds = windowSeconds;
            DrawNodes = drawNodes;
            FreeDraw = freeDraw;
        }
    }

    public readonly struct HexCommandFeedback
    {
        public readonly bool Success;
        public readonly string Code;
        public readonly string Message;
        public readonly IReadOnlyList<HexId> Choices;

        public HexCommandFeedback(bool success, string code, string message, IReadOnlyList<HexId> choices = null)
        {
            Success = success;
            Code = code;
            Message = message;
            Choices = choices ?? Array.Empty<HexId>();
        }
    }

    /// <summary>海克斯 UI 的纯逻辑适配层；只调用 HexSystem 的原子公共契约。</summary>
    public sealed class HexCommandModel
    {
        private readonly SimCore _sim;
        private readonly Side _player;

        public HexCommandModel(SimCore sim, Side player)
        {
            _sim = sim;
            _player = player;
        }

        public HexCommandState Describe()
        {
            int cost = _sim?.Game?.Ep?.HexDrawCost ?? 0;
            if (!ValidSide() || !ValidState())
                return new HexCommandState(false, 0, cost, false, Array.Empty<HexId>(),
                    Array.Empty<HexId>(), null, 0, false, 0, 0, HexSystem.DrawWindowSeconds, new int[0]);
            return new HexCommandState(true, _sim.EpOf(_player), cost, HexSystem.CanDraw(_sim, _player),
                HexSystem.PendingChoicesOf(_sim, _player).ToArray(),
                HexSystem.HandOf(_sim, _player).ToArray(),
                HexSystem.ActiveOf(_sim, _player), HexSystem.ActiveRemainingOf(_sim, _player),
                HexSystem.DrawWindowMinuteOf(_sim) > 0, HexSystem.DrawWindowMinuteOf(_sim),
                HexSystem.NextDrawMinuteOf(_sim), HexSystem.DrawWindowSeconds, HexSystem.DrawNodesOf(_sim),
                _sim.HexFreeDraw);
        }

        public HexCommandFeedback Draw()
        {
            if (!ValidSide()) return Fail(HexDrawError.InvalidSide.ToString(), "阵营无效");
            if (!ValidState()) return Fail(HexDrawError.InvalidState.ToString(), "海克斯状态无效");
            var result = HexSystem.TryDraw(_sim, _player);
            if (!result.Success)
                return Fail(result.Error.ToString(), DrawErrorText(result.Error));
            return new HexCommandFeedback(true, HexDrawError.None.ToString(),
                "已抽取，请三选一", result.Choices.ToArray());
        }

        public HexCommandFeedback Choose(HexId card)
        {
            if (!ValidSide()) return Fail(HexChooseError.InvalidSide.ToString(), "阵营无效");
            if (!ValidState()) return Fail(HexChooseError.InvalidState.ToString(), "海克斯状态无效");
            var error = HexSystem.TryChoose(_sim, _player, card);
            return error == HexChooseError.None
                ? Ok("已选择「" + NameOf(card) + "」，已加入手牌")
                : Fail(error.ToString(), ChooseErrorText(error));
        }

        public HexCommandFeedback Use(HexId card, IReadOnlyList<int> selectedIds)
        {
            if (!ValidSide()) return Fail(HexUseError.InvalidSide.ToString(), "阵营无效");
            if (!ValidState()) return Fail(HexUseError.InvalidState.ToString(), "海克斯状态无效");
            int targetId = 0;
            if (card == HexId.Sandbag)
            {
                if (selectedIds == null || selectedIds.Count != 1)
                    return Fail(HexUseError.InvalidTarget.ToString(), "沙包需要单选己方单位");
                targetId = selectedIds[0];
            }
            var error = HexSystem.TryUseHeld(_sim, _player, card, targetId);
            return error == HexUseError.None
                ? Ok("已释放「" + NameOf(card) + "」")
                : Fail(error.ToString(), UseErrorText(error));
        }

        public string NameOf(HexId card)
        {
            var cfg = _sim?.Game?.Hexes?.FirstOrDefault(h => h != null && h.Id == card);
            return cfg?.Name ?? card.ToString();
        }

        private bool ValidSide() => _player == Side.Blue || _player == Side.Red;

        private bool ValidState()
        {
            if (_sim == null || _sim.Config == null || _sim.Config.TickRate <= 0 || _sim.Rng == null
                || _sim.Game == null || _sim.Game.Ep == null || _sim.Game.Tools == null
                || _sim.Game.Ep.HexDrawCost <= 0 || _sim.Game.Tools.FortDurationSeconds <= 0
                || _sim.Game.Hexes == null || _sim.Game.Hexes.Length < 3)
                return false;
            var ids = new HashSet<HexId>();
            return _sim.Game.Hexes.All(h => h != null && h.DrawNodes != null
                && Enum.IsDefined(typeof(HexId), h.Id) && ids.Add(h.Id));
        }

        private static HexCommandFeedback Ok(string message) =>
            new HexCommandFeedback(true, "None", message);

        private static HexCommandFeedback Fail(string code, string message) =>
            new HexCommandFeedback(false, code, message);

        private static string DrawErrorText(HexDrawError error)
        {
            switch (error)
            {
                case HexDrawError.MatchEnded: return "对局已结束";
                case HexDrawError.NotInDrawWindow: return "当前不在海克斯抽取窗口";
                case HexDrawError.NodeConsumed: return "本节点已抽取";
                case HexDrawError.PendingChoiceExists: return "请先完成当前三选一";
                case HexDrawError.InsufficientEp: return "EP 不足";
                case HexDrawError.InvalidSide: return "阵营无效";
                default: return "海克斯状态无效";
            }
        }

        private static string ChooseErrorText(HexChooseError error)
        {
            switch (error)
            {
                case HexChooseError.NoPendingChoice: return "当前没有待选择卡牌";
                case HexChooseError.ChoiceNotOffered: return "该卡不在本次候选中";
                case HexChooseError.InvalidSide: return "阵营无效";
                default: return "海克斯状态无效";
            }
        }

        private static string UseErrorText(HexUseError error)
        {
            switch (error)
            {
                case HexUseError.CardNotHeld: return "未持有该海克斯";
                case HexUseError.InvalidTarget: return "目标无效";
                case HexUseError.NoValidPlacement: return "没有合法放置位置";
                case HexUseError.InvalidSide: return "阵营无效";
                default: return "海克斯状态无效";
            }
        }
    }
}
