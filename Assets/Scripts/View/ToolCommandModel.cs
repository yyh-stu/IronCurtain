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
            return HexSystem.UseRam(_sim, _player, buildingId, out var error)
                ? Ok("攻城锥已造成 " + _sim.Game.Tools.RamDamage + " 点伤害")
                : MapFailure(error);
        }

        public ToolCommandFeedback UseFort(GridPos cell)
        {
            var guard = Guard();
            if (guard.Error != ToolCommandError.None) return guard;
            if (!InBounds(cell) || CellOccupied(cell))
                return Fail(ToolCommandError.InvalidTarget, "临时阵地需要合法空格");
            if (_sim.EpOf(_player) < _sim.Game.Ep.FortCost)
                return Fail(ToolCommandError.InsufficientEp, "EP 不足");
            return HexSystem.UseFort(_sim, _player, cell, out var error)
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

        private static bool InBounds(GridPos cell) => cell.Row >= 0 && cell.Row < LaneGrid.Rows
            && cell.Col >= 0 && cell.Col < LaneGrid.Cols;

        private bool CellOccupied(GridPos cell)
        {
            foreach (var entity in _sim.Entities.Values)
            {
                if (entity.Pos.Row != cell.Row || entity.Pos.Col != cell.Col) continue;
                if (entity.Kind == EntityKind.Building) return true;
                if (entity.State != EntityState.Dead && entity.GarrisonBuildingId == 0) return true;
            }
            return false;
        }

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
