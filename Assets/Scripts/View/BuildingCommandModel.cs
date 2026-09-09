using System;
using System.Collections.Generic;
using System.Linq;
using IronCurtain.Sim;

namespace IronCurtain.View
{
    public enum BuildingCommandAction : byte { Capture, Destroy, Rebuild }
    public enum BuildingWorldClickDecision : byte { SelectGeneral, TargetBuilding, CloseTargetOnly, ClearSelection }

    public readonly struct BuildingCommandInfo
    {
        public readonly bool Exists;
        public readonly bool Fresh;
        public readonly int BuildingId;
        public readonly string Title;
        public readonly string Status;
        public readonly int SelectedGeneralCount;
        public readonly bool CanCapture;
        public readonly bool CanDestroy;
        public readonly bool CanRebuild;
        public readonly int RebuildCost;

        public BuildingCommandInfo(bool exists, bool fresh, int buildingId, string title, string status,
            int selectedGeneralCount, bool canCapture, bool canDestroy, bool canRebuild, int rebuildCost)
        {
            Exists = exists;
            Fresh = fresh;
            BuildingId = buildingId;
            Title = title;
            Status = status;
            SelectedGeneralCount = selectedGeneralCount;
            CanCapture = canCapture;
            CanDestroy = canDestroy;
            CanRebuild = canRebuild;
            RebuildCost = rebuildCost;
        }
    }

    public readonly struct BuildingCommandExecution
    {
        public readonly bool Success;
        public readonly SiegeOrderError Error;
        public readonly int SoldierCount;
        public readonly string Message;

        public BuildingCommandExecution(bool success, SiegeOrderError error, int soldierCount, string message)
        {
            Success = success;
            Error = error;
            SoldierCount = soldierCount;
            Message = message;
        }
    }

    /// <summary>建筑目标、合法动作和 Sim 调用适配。纯逻辑，无 UnityEngine 依赖。</summary>
    public sealed class BuildingCommandModel
    {
        private readonly SimCore _sim;
        private readonly Side _player;
        public SimCore Sim => _sim;   // 2026-09-10 悬停偏好 UI(面板)只读访问
        public Side Player => _player;

        public BuildingCommandModel(SimCore sim, Side player)
        {
            _sim = sim;
            _player = player;
        }

        public BuildingCommandInfo Describe(int buildingId, IReadOnlyList<int> selectedIds)
        {
            var b = _sim?.GetEntity(buildingId);
            if (b == null || b.Kind != EntityKind.Building)
                return new BuildingCommandInfo(false, false, buildingId, "目标已失效", "建筑不存在",
                    0, false, false, false, 0);

            int generals = EligibleGeneralCount(selectedIds);
            bool fresh = b.Side == _player || FogRules.BuildingState(_sim, _player, b) == BuildingFogState.Fresh;
            string title = RelationName(b.Side) + " " + BuildingName(b) + " #" + b.Id;
            if (!fresh)
                return new BuildingCommandInfo(true, false, b.Id, title, "目标不在当前视野",
                    generals, false, false, false, 0);
            if (b.Rubbled)
            {
                int cost = _sim.Game?.Ep?.RebuildCost ?? 0;
                bool canRebuild = b.Building.HasValue && cost > 0;
                string status = canRebuild ? "废墟可重建（" + cost + " EP）" : "该废墟不可重建";
                return new BuildingCommandInfo(true, true, b.Id, title, status,
                    generals, false, false, canRebuild, cost);
            }
            if (b.Sealed)
                return new BuildingCommandInfo(true, true, b.Id, title, "封城中不可切换命令",
                    generals, false, false, false, 0);
            if (b.Side == _player)
                return new BuildingCommandInfo(true, true, b.Id, title, "己方建筑（本阶段无可用操作）",
                    generals, false, false, false, 0);
            if (generals == 0)
                return new BuildingCommandInfo(true, true, b.Id, title, "请先选择己方主将",
                    0, false, false, false, 0);

            bool neutral = b.Side == Side.Neutral;
            bool isBase = b.Building == BuildingId.Base;
            bool capture = !isBase;
            bool destroy = !neutral;
            if (!capture && !destroy)
                return new BuildingCommandInfo(true, true, b.Id, title,
                    "当前建筑没有可用命令", generals, false, false, false, 0);
            return new BuildingCommandInfo(true, true, b.Id, title,
                "已选 " + generals + " 个兵团", generals, capture, destroy, false, 0);
        }

        /// <summary>2026-09-10 制作人:偏好=据点可修改属性。只设偏好不动军队;有选中兵团时同步下令。</summary>
        public BuildingCommandExecution SetPreference(int buildingId, BuildingCommandAction action, IReadOnlyList<int> selectedIds)
        {
            var info = Describe(buildingId, selectedIds);
            if (!info.Exists) return Failure(SiegeOrderError.InvalidTarget, "目标建筑已失效");
            bool allowed = action == BuildingCommandAction.Capture ? info.CanCapture : info.CanDestroy;
            if (!allowed) return Failure(SiegeOrderError.IntentRejected, info.Status);
            var intent = action == BuildingCommandAction.Capture ? SiegeIntent.Capture : SiegeIntent.Destroy;
            if (!SiegeSystem.SetIntent(_sim, buildingId, _player, intent, out var error))
                return Failure(SiegeOrderError.IntentRejected, error ?? "偏好设置失败");
            // 有选中兵团 → 选好偏好即按该偏好进攻(制作人第 3 条)
            if (info.SelectedGeneralCount > 0)
            {
                var r = SiegeSystem.TryIssueRegimentOrder(_sim, selectedIds, buildingId, _player, intent);
                if (!r.Success) return new BuildingCommandExecution(true, SiegeOrderError.None, 0,
                    "偏好已设;下令失败:" + ErrorText(r.Error));
                return new BuildingCommandExecution(true, SiegeOrderError.None, r.SoldierIds.Count,
                    "偏好=占领/摧毁已记录,兵团已按偏好进攻");
            }
            return new BuildingCommandExecution(true, SiegeOrderError.None, 0, "进攻偏好已更新");
        }

        /// <summary>2026-09-09 制作人裁决:选中兵团+点城=按现有偏好直接下令(无偏好则默认:可占领→占领,否则摧毁)。</summary>
        public BuildingCommandExecution OrderPreferred(int buildingId, IReadOnlyList<int> selectedIds)
        {
            var info = Describe(buildingId, selectedIds);
            if (!info.Exists) return Failure(SiegeOrderError.InvalidTarget, "目标建筑已失效");
            if (info.SelectedGeneralCount == 0) return Failure(SiegeOrderError.NoEligibleSoldiers, "未选中兵团");
            var b = _sim.GetEntity(buildingId);
            if (b == null || b.Intent == SiegeIntent.None)
                return Failure(SiegeOrderError.IntentRejected, "请先设置该据点进攻偏好"); // 2026-09-10 制作人:无偏好不默认,提示设置
            var intent = b.Intent;
            var result = SiegeSystem.TryIssueRegimentOrder(_sim, selectedIds, buildingId, _player, intent);
            if (!result.Success) return Failure(result.Error, ErrorText(result.Error));
            return new BuildingCommandExecution(true, SiegeOrderError.None, result.SoldierIds.Count,
                "已下令兵团进攻(兵随主将行军)");
        }

        public BuildingCommandExecution Execute(
            int buildingId, IReadOnlyList<int> selectedIds, BuildingCommandAction action)
        {
            var info = Describe(buildingId, selectedIds);
            if (!info.Exists) return Failure(SiegeOrderError.InvalidTarget, "目标建筑已失效");
            if (!Enum.IsDefined(typeof(BuildingCommandAction), action))
                return Failure(SiegeOrderError.InvalidIntent, "建筑命令无效");
            if (action == BuildingCommandAction.Rebuild)
            {
                if (!info.CanRebuild) return Failure(SiegeOrderError.IntentRejected, info.Status);
                if (!DestroySystem.Rebuild(_sim, buildingId, _player, out var rebuildError))
                    return Failure(SiegeOrderError.IntentRejected, rebuildError ?? "重建失败");
                return new BuildingCommandExecution(true, SiegeOrderError.None, 0,
                    "已消耗 " + info.RebuildCost + " EP 重建建筑");
            }
            if (info.SelectedGeneralCount == 0)
            {
                // 2026-09-09 制作人裁决:偏好与下令解耦——无选中兵团时只记录进攻偏好,不动任何军队
                var pref = action == BuildingCommandAction.Capture ? SiegeIntent.Capture : SiegeIntent.Destroy;
                bool prefAllowed = action == BuildingCommandAction.Capture ? info.CanCapture : info.CanDestroy;
                if (!prefAllowed) return Failure(SiegeOrderError.IntentRejected, info.Status);
                if (!SiegeSystem.SetIntent(_sim, buildingId, _player, pref, out var intentError))
                    return Failure(SiegeOrderError.IntentRejected, intentError ?? "偏好设置失败");
                return new BuildingCommandExecution(true, SiegeOrderError.None, 0,
                    "已记录进攻偏好(未选中兵团,不下令)");
            }
            bool allowed = action == BuildingCommandAction.Capture ? info.CanCapture : info.CanDestroy;
            if (!allowed) return Failure(SiegeOrderError.IntentRejected, info.Status);

            var intent = action == BuildingCommandAction.Capture ? SiegeIntent.Capture : SiegeIntent.Destroy;
            var result = SiegeSystem.TryIssueRegimentOrder(_sim, selectedIds, buildingId, _player, intent);
            if (result.Success)
                return new BuildingCommandExecution(true, SiegeOrderError.None, result.SoldierIds.Count,
                    "已下令 " + result.SoldierIds.Count + " 名士兵");
            return Failure(result.Error, ErrorText(result.Error));
        }

        public static int PickVisibleBuilding(SimCore sim, Side player, SimVec2 point)
        {
            if (sim == null) return 0;
            Entity best = null;
            float bestDistance = float.MaxValue;
            foreach (var b in sim.Entities.Values.OrderBy(e => e.Id))
            {
                if (b.Kind != EntityKind.Building || !BuildingFootprint.Contains(b, point)) continue;
                if (b.Side != player && FogRules.BuildingState(sim, player, b) != BuildingFogState.Fresh) continue;
                float distance = SimVec2.Distance(point, SimVec2.FromGrid(b.Pos));
                if (distance < bestDistance)
                {
                    best = b;
                    bestDistance = distance;
                }
            }
            return best?.Id ?? 0;
        }

        public static BuildingWorldClickDecision DecideWorldClick(bool targetOpen, int generalId, int buildingId)
        {
            if (generalId != 0) return BuildingWorldClickDecision.SelectGeneral;
            if (buildingId != 0) return BuildingWorldClickDecision.TargetBuilding;
            return targetOpen ? BuildingWorldClickDecision.CloseTargetOnly : BuildingWorldClickDecision.ClearSelection;
        }

        private int EligibleGeneralCount(IReadOnlyList<int> selectedIds)
        {
            if (selectedIds == null) return 0;
            var unique = new HashSet<int>();
            foreach (int id in selectedIds)
            {
                var g = _sim.GetEntity(id);
                if (g == null || g.Kind != EntityKind.Unit || g.IsSoldier || g.Side != _player || g.Hp <= 0) continue;
                if (g.State == EntityState.Dead || g.State == EntityState.Routing || g.State == EntityState.Exchanging) continue;
                if (g.GarrisonBuildingId != 0) continue;
                unique.Add(id);
            }
            return unique.Count;
        }

        private string RelationName(Side side) => side == _player ? "己方" : side == Side.Neutral ? "中立" : "敌方";

        private static string BuildingName(Entity b)
        {
            if (b.IsFort) return "阵地";
            switch (b.Building)
            {
                case BuildingId.Base: return "大本营";
                case BuildingId.Barracks: return "兵营";
                case BuildingId.ResourcePoint: return "资源点";
                case BuildingId.Tower: return "箭塔";
                default: return "建筑";
            }
        }

        private static BuildingCommandExecution Failure(SiegeOrderError error, string message) =>
            new BuildingCommandExecution(false, error, 0, message);

        private static string ErrorText(SiegeOrderError error)
        {
            switch (error)
            {
                case SiegeOrderError.NoEligibleSoldiers: return "没有可下令士兵";
                case SiegeOrderError.NoValidPlacement: return "没有合法攻城位置";
                case SiegeOrderError.InvalidTarget: return "目标建筑已失效";
                case SiegeOrderError.InvalidState: return "对局状态无效";
                case SiegeOrderError.InvalidIssuer: return "发令阵营无效";
                case SiegeOrderError.InvalidIntent: return "建筑命令无效";
                case SiegeOrderError.IntentRejected: return "当前建筑状态不允许该命令";
                default: return "命令执行失败";
            }
        }
    }
}
