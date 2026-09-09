using System;
using System.Collections.Generic;
using System.Linq;
using IronCurtain.Sim;

namespace IronCurtain.View
{
    public enum UpgradeCommandError : byte
    {
        None, InvalidState, InvalidHero, AlreadyUpgraded, InsufficientEp, Rejected
    }

    public readonly struct UpgradeCommandEntry
    {
        public readonly HeroId Hero;
        public readonly string Name;
        public readonly string Description;
        public readonly int Cost;
        public readonly bool Upgraded;
        public readonly bool Affordable;

        public UpgradeCommandEntry(HeroId hero, string name, string description, int cost,
            bool upgraded, bool affordable)
        {
            Hero = hero;
            Name = name;
            Description = description;
            Cost = cost;
            Upgraded = upgraded;
            Affordable = affordable;
        }
    }

    public readonly struct UpgradeCommandState
    {
        public readonly float Ep;
        public readonly IReadOnlyList<UpgradeCommandEntry> Entries;

        public UpgradeCommandState(float ep, IReadOnlyList<UpgradeCommandEntry> entries)
        {
            Ep = ep;
            Entries = entries;
        }
    }

    public readonly struct UpgradeCommandFeedback
    {
        public readonly bool Success;
        public readonly UpgradeCommandError Error;
        public readonly string Message;

        public UpgradeCommandFeedback(bool success, UpgradeCommandError error, string message)
        {
            Success = success;
            Error = error;
            Message = message;
        }
    }

    /// <summary>英雄升级 UI 的纯逻辑适配层；不拥有或复制 Sim 升级状态。</summary>
    public sealed class UpgradeCommandModel
    {
        private readonly SimCore _sim;
        private readonly Side _player;

        public UpgradeCommandModel(SimCore sim, Side player)
        {
            _sim = sim;
            _player = player;
        }

        public UpgradeCommandState Describe()
        {
            if (!ValidState()) return new UpgradeCommandState(0, Array.Empty<UpgradeCommandEntry>());
            float ep = _sim.EpOf(_player);
            var entries = _sim.Game.Upgrades.OrderBy(u => (int)u.Hero)
                .Select(u => new UpgradeCommandEntry(u.Hero, u.Name, u.Description, u.Cost,
                    _sim.IsUpgraded(_player, u.Hero), ep >= u.Cost && !_sim.IsUpgraded(_player, u.Hero)))
                .ToArray();
            return new UpgradeCommandState(ep, entries);
        }

        public UpgradeCommandFeedback Buy(HeroId hero)
        {
            if (!ValidState()) return Fail(UpgradeCommandError.InvalidState, "升级状态无效");
            var cfg = _sim.Game.Upgrades.FirstOrDefault(u => u.Hero == hero);
            if (cfg == null) return Fail(UpgradeCommandError.InvalidHero, "英雄升级不存在");
            if (_sim.IsUpgraded(_player, hero))
                return Fail(UpgradeCommandError.AlreadyUpgraded, "该英雄已升级");
            if (_sim.EpOf(_player) < cfg.Cost)
                return Fail(UpgradeCommandError.InsufficientEp, "EP 不足");

            if (_sim.BuyUpgrade(_player, hero, out var error))
                return new UpgradeCommandFeedback(true, UpgradeCommandError.None,
                    "已升级「" + cfg.Name + "」");
            return Fail(UpgradeCommandError.Rejected, error ?? "升级失败");
        }

        private bool ValidState()
        {
            if (_sim == null || _sim.Game == null || _sim.Game.Ep == null
                || _sim.Game.Ep.UpgradeCost != 2 || _sim.Game.Upgrades == null
                || _sim.Game.Upgrades.Length != 6 || (_player != Side.Blue && _player != Side.Red))
                return false;
            var heroes = new HashSet<HeroId>();
            return _sim.Game.Upgrades.All(u => u != null && u.Cost == 2
                && Enum.IsDefined(typeof(HeroId), u.Hero) && heroes.Add(u.Hero))
                && heroes.Count == 6;
        }

        private static UpgradeCommandFeedback Fail(UpgradeCommandError error, string message) =>
            new UpgradeCommandFeedback(false, error, message);
    }
}
