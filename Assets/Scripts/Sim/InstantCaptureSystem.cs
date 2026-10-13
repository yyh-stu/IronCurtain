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

        /// <summary>"刚占领保护期"(tick):3 秒内不可被即时夺回,防来回翻面。
        /// 交换占领(SiegeSystem)与即时占领(本系统)共用同一常量 —— 同一语义只有一个数。</summary>
        public const int FreshCaptureProtectTicks = 30;

        public static void Tick(SimCore sim, float dt)
        {
            if (sim == null || sim.Game == null) return;
            List<Entity> captured = null;
            foreach (var kv in sim.Entities)
            {
                var b = kv.Value;
                if (b == null || b.Kind != EntityKind.Building) continue;
                if (b.Rubbled) continue;
                if (b.Building == BuildingId.Base) continue;      // 大本营不可占领(摧毁即胜)
                if (b.IsFort) continue;                              // 堡垒=只可摧毁的临时工事(见类注释)
                if (b.Side == Side.Neutral) continue;             // 废墟(重建线)不在本系统
                // 制作人 2026-09-11 澄清:只有【无人】建筑才能直接占领——
                // 有驻军(正式/临时)或城内有攻城交换单位时,必须先打掉守军,本系统跳过
                if (b.GarrisonFormal.Count > 0 || b.GarrisonTemp.Count > 0) continue;
                if (sim.Tick < b.CapturedUntilTick) continue;   // 刚被占领的保护期(防来回翻面)
                // 2026-09-14 修复"建筑红蓝每 tick 闪烁"(实测:有驻军的兵营被"近战出击"放出驻军后,
                // 同一 tick 就被当无人建筑占领;紧接着旁边归属方的单位又把它翻回去 → 永久闪烁,
                // 障碍一会儿有一会儿没有,绕行判定永远不成立 = 制作人说的"被卡住很久过不去")。
                // 口径补齐:「只有【无人】建筑才能直接占领」= 既没有驻军(上面那道闸),
                // **当前归属方在该建筑半径内也确实没有任何单位**(有人守就不算无人) → 闪烁从结构上不可能发生。
                float rad = RadiusOf(sim, b);
                var bpos = SimVec2.FromGrid(b.Pos);
                bool defended = false;
                foreach (var d in sim.Entities.Values)
                {
                    if (d.Kind != EntityKind.Unit || d.Side != b.Side) continue;
                    if (d.State == EntityState.Dead) continue;
                    if (SimVec2.Distance(d.PosF, bpos) <= rad) { defended = true; break; }
                }
                if (defended) continue;
                Entity captor = null;
                foreach (var u in sim.Entities.Values)
                {
                    if (u.Kind != EntityKind.Unit || u.Side == Side.Neutral) continue;
                    if (u.State == EntityState.Dead || u.GarrisonBuildingId != 0) continue;
                    if (u.Side == b.Side) continue;
                    if (u.TargetBuildingId == b.Id) continue;     // 正在执行摧毁意图的兵不触发占领
                    // 2026-09-15 制作人:"**还是不攻击建筑,全在发呆**"的第二个真因
                    // (探针逐 tick 抓到:建筑 Intent 在 t=3s 由 Destroy 变成 None,而塔血一点没掉)。
                    // 攻城令下给的是**主将**;士兵要等主将到位(SiegeSystem 第 0 步)才拿到自己的标记,
                    // 而士兵是编队跟随、先贴上去的那批 —— 他们此刻 `TargetBuildingId == 0`,
                    // 于是对一座**无守军**的敌方建筑正好符合"接触即占领" → **被自己人顺手翻成己方** →
                    // `Capture` 清空 Intent → 全军的摧毁令一起作废 → 全体原地发呆 ✗。
                    // 口径:一座建筑若已被某方明确下了摧毁令,**该方自己的单位不许顺手占领它**
                    // (要拆就拆到底;想改占领请先改意图)。只挡下令方,不挡别的阵营趁虚而入。
                    if (b.Intent == SiegeIntent.Destroy && u.Side == b.IntentIssuer) continue;
                    if (SimVec2.Distance(u.PosF, bpos) <= rad)
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
                var bpos = SimVec2.FromGrid(b.Pos);      // 应用阶段自带作用域(收集阶段的不在此处)
                float rad = RadiusOf(sim, b);
                Entity anyEnemy = null;
                foreach (var u in sim.Entities.Values)
                {
                    if (u.Kind != EntityKind.Unit || u.Side == old || u.Side == Side.Neutral) continue;
                    if (u.State == EntityState.Dead) continue;
                    if (SimVec2.Distance(u.PosF, bpos) <= rad) { anyEnemy = u; break; }
                }
                if (anyEnemy == null) continue;
                b.Side = anyEnemy.Side;
                b.Intent = SiegeIntent.None;
                b.IntentIssuer = Side.Neutral;
                // 2026-09-16 三十七轮:即时占领同样进入"刚占领保护期"。旧口径只在交换占领(SiegeSystem)
                // 设保护 —— 慢攻时进攻方士兵先一步接触占领空塔(无驻军),主将尚未进城、身边己方单位
                // 又被自动层拉走 → "无人防守"窗口敞开 → 刚被赶出来的原守军原地夺回,占领白打
                // (实机 IC_ACCEPT 判据"塔已归我=否"的追因,复现见 InstantCaptureTests.T1)。
                b.CapturedUntilTick = sim.Tick + FreshCaptureProtectTicks;
                GarrisonSystem.OnCityCaptured(sim, b);            // 旧主驻军清登记(士兵留场)
                sim.Emit(new SimEvent(sim.Tick, SimEventType.EntitySpawned, b.Id)); // 视图层刷新归属色用既有事件
            }
        }
    }
}
