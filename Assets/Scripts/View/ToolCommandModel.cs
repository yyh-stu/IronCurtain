using IronCurtain.Sim;

namespace IronCurtain.View
{
    public enum ToolCommandError : byte
    {
        None, InvalidState, InvalidSide, InvalidTarget, InsufficientEp, Rejected
    }

    public readonly struct ToolCommandState
    {
        public readonly bool Valid;
        public readonly float Ep;
        public readonly int RamCost;
        public readonly int FortCost;
        public readonly bool CanAffordRam;
        public readonly bool CanAffordFort;

        public ToolCommandState(bool valid, float ep, int ramCost, int fortCost)
        {
            Valid = valid;
            Ep = ep;
            RamCost = ramCost;
            FortCost = fortCost;
            CanAffordRam = valid && ep >= ramCost;
            CanAffordFort = valid && ep >= fortCost;
        }
    }

    public readonly struct ToolCommandFeedback
    {
        public readonly bool Success;
        public readonly ToolCommandError Error;
        public readonly string Message;

        public ToolCommandFeedback(bool success, ToolCommandError error, string message)
        {
            Success = success;
            Error = error;
            Message = message;
        }
    }

    /// <summary>攻城锥与临时阵地的纯 View 适配；状态和扣费仍由 Sim 权威拥有。</summary>
    public sealed class ToolCommandModel
    {
        private readonly SimCore _sim;
        private readonly Side _player;

        public ToolCommandModel(SimCore sim, Side player)
        {
            _sim = sim;
            _player = player;
        }

        public ToolCommandState Describe()
        {
            if (!ValidState()) return new ToolCommandState(false, 0, 0, 0);
            return new ToolCommandState(true, _sim.EpOf(_player),
                _sim.Game.Ep.RamCost, _sim.Game.Ep.FortCost);
        }

        public ToolCommandFeedback UseRam(int buildingId)
        {
            var guard = Guard();
            if (guard.Error != ToolCommandError.None) return guard;
            var target = _sim.GetEntity(buildingId);
            if (target == null || target.Kind != EntityKind.Building || target.Rubbled
                || target.State == EntityState.Dead
                || target.Side == Side.Neutral || target.Side == _player)
                return Fail(ToolCommandError.InvalidTarget, "攻城锥目标必须是敌方建筑");
            if (_sim.EpOf(_player) < _sim.Game.Ep.RamCost)
                return Fail(ToolCommandError.InsufficientEp, "EP 不足");
            return ToolsSystem.UseRam(_sim, _player, buildingId, out var error)
                ? Ok("攻城锥已造成 " + _sim.Game.Tools.RamDamage + " 点伤害")
                : MapFailure(error);
        }

        public ToolCommandFeedback UseFort(GridPos cell)
        {
            var guard = Guard();
            if (guard.Error != ToolCommandError.None) return guard;
            // 2026-09-16 三十七轮:预检(越界/占用/EP)已删 —— ToolsSystem.PlacementBlocked 是唯一校验口径,
            // 此处重复预检只会把具体原因(水域/视野外/…)压成笼统文案。
            return ToolsSystem.UseFort(_sim, _player, cell, out var error)
                ? Ok("临时阵地已部署")
                : MapFailure(error);
        }

        private ToolCommandFeedback Guard()
        {
            if (_player != Side.Blue && _player != Side.Red)
                return Fail(ToolCommandError.InvalidSide, "阵营无效");
            return ValidState()
                ? new ToolCommandFeedback(false, ToolCommandError.None, null)
                : Fail(ToolCommandError.InvalidState, "道具状态无效");
        }

        private bool ValidState()
        {
            if (_sim == null || _sim.Config == null || _sim.Config.TickRate <= 0
                || _sim.Game == null || _sim.Game.Ep == null || _sim.Game.Tools == null
                || (_player != Side.Blue && _player != Side.Red)) return false;
            return _sim.Game.Ep.RamCost == 1 && _sim.Game.Ep.FortCost == 1
                && _sim.Game.Tools.RamDamage > 0 && _sim.Game.Tools.FortHp > 0
                && _sim.Game.Tools.FortDurationSeconds > 0;
        }

        // 2026-09-16 三十七轮:InBounds/CellOccupied 预检助手已删(与 ToolsSystem.PlacementBlocked 重复,
        // 校验口径收敛到 Sim 侧唯一入口)。

        private static ToolCommandFeedback MapFailure(string error)
        {
            if (error != null && error.Contains("能量"))
                return Fail(ToolCommandError.InsufficientEp, "EP 不足");
            if (error != null && error.Contains("阵营"))
                return Fail(ToolCommandError.InvalidSide, "阵营无效");
            if (error != null && error.Contains("目标"))
                return Fail(ToolCommandError.InvalidTarget, error);
            return Fail(ToolCommandError.Rejected, error ?? "道具使用失败");
        }

        private static ToolCommandFeedback Ok(string message) =>
            new ToolCommandFeedback(true, ToolCommandError.None, message);

        private static ToolCommandFeedback Fail(ToolCommandError error, string message) =>
            new ToolCommandFeedback(false, error, message);
    }
}
