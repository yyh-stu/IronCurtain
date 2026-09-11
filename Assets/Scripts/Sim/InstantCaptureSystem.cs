using System;
using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 即时占领(2026-09-11 制作人裁决):
    /// "对方军队一进去不用交换就占领了,反正和其它建筑一样,要不要驻军看个人。"
    /// 适用于非大本营建筑——敌方单位进入建筑接触范围即换归属(满血),驻军与否由玩家自便。
    /// 大本营不占领(原规则,摧毁即胜)。中立语义保留给废墟(重建机制),不再用于"中立据点"。
    /// </summary>
    public static class InstantCaptureSystem
    {
        /// <summary>判定半径:建筑碰撞半径 + 0.5(与近战贴身容差一致)。</summary>
        public static float RadiusOf(SimCore sim, Entity b)
        {
            return SimCore.BuildingCollisionRadius(b) + 0.5f;
        }

        public static void Tick(SimCore sim, float dt)
        {
            if (sim == null || sim.Game == null) return;
            List<Entity> captured = null;
            foreach (var kv in sim.Entities)
            {
                var b = kv.Value;
                if (b == null || b.Kind != EntityKind.Building) continue;
                if (b.Rubbled || b.Sealed) continue;
                if (b.Building == BuildingId.Base) continue;      // 大本营不可占领(摧毁即胜)
                if (b.IsFort) continue;                              // 堡垒=只可摧毁的临时工事(见类注释)
                if (b.Side == Side.Neutral) continue;             // 废墟(重建线)不在本系统
                // 制作人 2026-09-11 澄清:只有【无人】建筑才能直接占领——
                // 有驻军(正式/临时)或城内有攻城交换单位时,必须先打掉守军,本系统跳过
                if (b.GarrisonFormal.Count > 0 || b.GarrisonTemp.Count > 0) continue;
                Entity captor = null;
                foreach (var u in sim.Entities.Values)
                {
                    if (u.Kind != EntityKind.Unit || u.Side == Side.Neutral) continue;
                    if (u.State == EntityState.Dead || u.GarrisonBuildingId != 0) continue;
                    if (u.Side == b.Side) continue;
                    if (u.TargetBuildingId == b.Id) continue;     // 正在执行摧毁意图的兵不触发占领
                    if (SimVec2.Distance(u.PosF, SimVec2.FromGrid(b.Pos)) <= RadiusOf(sim, b))
                    {
                        captor = u;
                        break;
                    }
                }
                if (captor == null) continue;
                (captured ?? (captured = new List<Entity>())).Add(b);
                // 提交放在收集后,避免遍历中改集合
            }
            if (captured == null) return;
            foreach (var b in captured)
            {
                var old = b.Side;
                Entity anyEnemy = null;
                foreach (var u in sim.Entities.Values)
                {
                    if (u.Kind != EntityKind.Unit || u.Side == old || u.Side == Side.Neutral) continue;
                    if (u.State == EntityState.Dead) continue;
                    if (SimVec2.Distance(u.PosF, SimVec2.FromGrid(b.Pos)) <= RadiusOf(sim, b)) { anyEnemy = u; break; }
                }
                if (anyEnemy == null) continue;
                b.Side = anyEnemy.Side;
                b.Intent = SiegeIntent.None;
                b.IntentIssuer = Side.Neutral;
                GarrisonSystem.OnCityCaptured(sim, b);            // 旧主驻军清登记(士兵留场)
                sim.Emit(new SimEvent(sim.Tick, SimEventType.EntitySpawned, b.Id)); // 视图层刷新归属色用既有事件
            }
        }
    }
}
