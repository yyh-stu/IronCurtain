using System.Collections.Generic;
using System.Linq;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 驻军系统(M8.3a 将兵化;临时驻军=城后等候不冻结移动;士兵永不溃逃)。M8.1b:城门占地矩形语义(D18)。
    /// 2026-09-14 制作人口径:「主将和他的兵团就是一个单位」——主将可入城(有兵力值),正式/临时驻军都不区分主将/士兵。
    /// </summary>
    public static class GarrisonSystem
    {

        // D27:建筑脱战 20 秒后每秒回复 4 点,至上限
        private const float RegenDelaySec = 20f;
        private const float RegenPerSec = 4f; // D27
        private const float TempFollowRange = 15f; // 城后等候:主将距城超出即解除

        private static readonly HashSet<HeroId> RangedHeroes = new HashSet<HeroId> { HeroId.ZhuiRi, HeroId.XingLuo };

        /// <summary>城池主动攻击(2026-09-13 制作人):5 伤/0.5s(DPS 10=坠日+星落并肩;两射手 TTK 一致)。</summary>
        public const float CityAttackDamage = 5f;
        public const float CityAttackInterval = 0.5f;

        /// <summary>城池攻击射程=建筑视野(大本营 40/箭塔 24/其余 20,制作人口径"建筑视野范围内")。</summary>
        private static int VisionRadiusOf(Entity b) =>
            b.Building == BuildingId.Base ? 40 : b.Building == BuildingId.Tower ? 24 : 20;

        public static bool EnterFormal(SimCore sim, IReadOnlyList<int> unitIds, int buildingId, out string error)
        {
            error = null;
            if (!sim.Entities.ContainsKey(buildingId)) { error = "建筑不存在"; return false; }
            var b = sim.GetEntity(buildingId);
            var cfg = sim.BuildingConfigOf(b.Building.Value);
            if (b.IsFort) { error = "堡垒不可驻军(2026-09-11 制作人:只可摧毁,持续攻击)"; return false; } // 堡垒豁免在配置读取前
            // 2026-09-13 制作人口径:"除了大本营都可以驻军"。原判据是配置里的 GarrisonFormal<=0
            // (资源点=0 因此被误拒),改为只排除大本营。
            if (b.Building == BuildingId.Base) { error = "大本营不可驻军"; return false; }
            // 2026-09-14:先**全量校验**再落地 —— 原来边收边登记,若中途某个单位不合法就把已收的
            // 从名册删掉(旧 Rollback 还不清 GarrisonBuildingId)→ 一批人变成幽灵。现在一次判定完毕。
            var accepted = new List<int>();
            foreach (var id in unitIds)
            {
                if (!sim.Entities.ContainsKey(id)) continue;
                var u = sim.GetEntity(id);
                // 2026-09-13 制作人口径更正:"主将现在可和士兵一起驻军"(此前这里写"主将永不进城")。
                // 这条旧门禁与 Exit(...) 的实现自相矛盾 —— Exit 是按 **HeroId** 从驻军里找人、
                // 且专门处理"非士兵放城门站定",说明驻军本就允许主将。此处放开。
                if (u.Side != b.Side) { error = "建筑须为己方"; Rollback(sim, accepted, buildingId); return false; }
                accepted.Add(id);
            }
            // 2026-09-11 制作人:单城**不限量**驻守(废除容量判定,原满员改走城门的分支删除)
            foreach (var id in accepted) DoEnterFormal(sim, sim.GetEntity(id), buildingId, b.GarrisonFormal);
            return true;
        }

        public static bool EnterTemp(SimCore sim, IReadOnlyList<int> unitIds, int buildingId, out string error)
        {
            error = null;
            if (!sim.Entities.ContainsKey(buildingId)) { error = "建筑不存在"; return false; }
            var b = sim.GetEntity(buildingId);
            if (b.IsFort) { error = "堡垒不可驻军(2026-09-11 制作人裁决)"; return false; } // 堡垒=只可摧毁的临时工事
            // 2026-09-14:与正式驻军同一口径——「除了大本营都可以驻军」(2026-09-13 制作人)。
            // 原判据读配置的 `GarrisonTemp`,资源点=0 因此被误拒(与"除大本营外都可驻军"冲突);
            // 配置里的 `GarrisonTemp` 数值此后不再作为硬门槛(与 `CanGarrison` 一并列入未接线键)。
            if (b.Building == BuildingId.Base) { error = "大本营不可驻军"; return false; }
            var accepted = new List<int>();
            foreach (var id in unitIds)
            {
                if (!sim.Entities.ContainsKey(id)) continue;
                var u = sim.GetEntity(id);
                // 2026-09-14 制作人口径删除:「仅士兵可驻军 / 主将城外等候」作废——主将早有兵力值、可以入城,
                // 「主将和他的兵团就是一个单位」,临时登记同样不区分主将/士兵。
                if (u.Side != b.Side) { error = "建筑须为己方"; RollbackTemp(sim, accepted, buildingId); return false; }
                // 2026-09-11 制作人:临时驻军同样不限量
                DoEnterTemp(u, buildingId, b.GarrisonTemp);
                accepted.Add(id);
            }
            return true;
        }

        public static bool Exit(SimCore sim, int buildingId, HeroId hero, out string error) // D24:士兵回兵团;非士兵放城门站定
        {
            error = null;
            if (!sim.Entities.ContainsKey(buildingId)) { error = "建筑不存在"; return false; }
            var b = sim.GetEntity(buildingId);
            PruneStale(sim, b);
            int id = FirstOf(sim, b.GarrisonFormal, hero);
            if (id == 0) id = FirstOf(sim, b.GarrisonTemp, hero);
            if (id == 0) { error = "城内无该兵种"; return false; }
            return ExitById(sim, b, id, out error);
        }

        /// <summary>
        /// 2026-09-13 制作人:"士兵跟着主将走" —— 释放时把该主将**和他的士兵**一起放出城。
        /// 2026-09-14 修两个真缺陷:
        /// ① 原实现先 `n=计数` 再循环调 `Exit` —— 计数是**预先算的**,循环里任何一次失败都会让
        ///    "另一个人"被留在城里(而且旧 `Exit` 是先把人从名册删掉再找城门格,城门被占满时
        ///    该单位既不在名册、又还登记着 GarrisonBuildingId = **永远放不出来的幽灵**);
        /// ② 名册里可能残留已阵亡单位的陈旧 id(死亡路径不清驻军名册),旧 `Exit` 直接 `sim.GetEntity(x).Hero`
        ///    会空引用抛出,把整次释放打断。
        /// 现在:先清理陈旧 id → 快照要放的人 → 逐个放、放不动就跳过并如实报告放出人数。
        /// </summary>
        public static bool ExitRegiment(SimCore sim, int buildingId, HeroId hero, out string error)
            => ExitRegiment(sim, buildingId, hero, out error, out _);

        public static bool ExitRegiment(SimCore sim, int buildingId, HeroId hero, out string error, out int released)
        {
            error = null; released = 0;
            if (!sim.Entities.ContainsKey(buildingId)) { error = "建筑不存在"; return false; }
            var b = sim.GetEntity(buildingId);
            PruneStale(sim, b);
            var ids = new List<int>();
            foreach (var x in b.GarrisonFormal) { var u = sim.GetEntity(x); if (u != null && u.Hero == hero) ids.Add(x); }
            foreach (var x in b.GarrisonTemp)
            { var u = sim.GetEntity(x); if (u != null && u.Hero == hero && !ids.Contains(x)) ids.Add(x); }
            if (ids.Count == 0) { error = "城内无该兵种"; return false; }
            string last = null;
            foreach (var id in ids)
            {
                string e1;
                if (ExitById(sim, b, id, out e1)) released++;
                else if (!string.IsNullOrEmpty(e1)) last = e1;
            }
            if (released == 0) { error = last ?? "未能释放"; return false; }
            if (released < ids.Count) error = "只放出 " + released + "/" + ids.Count + " 人(" + (last ?? "部分门位被占") + ")";
            return true;
        }

        /// <summary>按单位 id 放一个出城。门位**先判定成功再动名册** —— 失败时名册原样保留,下次还能放。</summary>
        private static bool ExitById(SimCore sim, Entity b, int id, out string error)
        {
            error = null;
            var u = sim.GetEntity(id);
            if (u == null) { RemoveFromRoster(b, id); error = "名册里有已阵亡的陈旧记录"; return false; }
            GridPos? gate = null;
            if (!u.IsSoldier)   // 士兵回兵团不需门位;主将要从城门走出去
            {
                gate = FindGateCell(sim, b);
                if (gate == null) { error = "城门无空位"; return false; }
            }
            RemoveFromRoster(b, id);
            if (u.IsSoldier)
            {   // 士兵出城:回到兵团阵型(跟随主将;主将死→无将续战)
                u.GarrisonBuildingId = 0; u.TempGarrisonId = 0;
                u.State = EntityState.Idle; u.HasMoveTarget = false;
                return true;
            }
            u.Pos = gate.Value; u.PosF = SimVec2.FromGrid(gate.Value);
            u.GarrisonBuildingId = 0; u.TempGarrisonId = 0;
            u.State = EntityState.Idle; u.HasMoveTarget = false;
            return true;
        }

        private static void RemoveFromRoster(Entity b, int id)
        {
            b.GarrisonFormal.Remove(id);
            b.GarrisonTemp.Remove(id);
        }

        /// <summary>名册第一个属于该英雄的 id(空引用安全)。</summary>
        private static int FirstOf(SimCore sim, List<int> list, HeroId hero)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var u = sim.GetEntity(list[i]);
                if (u != null && u.Hero == hero) return list[i];
            }
            return 0;
        }

        /// <summary>清掉名册里已阵亡/已移除的陈旧 id(死亡与占领路径不清驻军名册,留着会打断释放)。</summary>
        private static void PruneStale(SimCore sim, Entity b)
        {
            for (int i = b.GarrisonFormal.Count - 1; i >= 0; i--)
            {
                var u = sim.GetEntity(b.GarrisonFormal[i]);
                if (u == null || u.State == EntityState.Dead) b.GarrisonFormal.RemoveAt(i);
            }
            for (int i = b.GarrisonTemp.Count - 1; i >= 0; i--)
            {
                var u = sim.GetEntity(b.GarrisonTemp[i]);
                if (u == null || u.State == EntityState.Dead) b.GarrisonTemp.RemoveAt(i);
            }
        }

        /// <summary>城池易主:士兵只清登记(永不溃逃,留场续战);非士兵(旧数据)向同一线后方最近己方据点溃逃。</summary>
        public static void OnCityCaptured(SimCore sim, Entity b)
        {
            // 2026-09-14 ★制作人"攻占敌方建筑时点驻军,进去的士兵消失了"的真因:
            // 这里原来**只清 GarrisonTemp,从没清 GarrisonFormal** —— 而"驻军"按钮放进去的正是**正式**驻军。
            // 于是城池易主后,正式驻军仍登记着 GarrisonBuildingId(却被视图按"城内"隐藏),
            // 又不在任何会被清理的名册里 → **永久消失**(敌方视角"我的兵没了",我方视角"进去的兵没了")。
            // 修法与下面 OnBuildingRubbled 一致(它本来就两个名册都清):正式+临时一起取出、一起清空。
            var fleeing = new List<int>(b.GarrisonFormal);
            fleeing.AddRange(b.GarrisonTemp);
            b.GarrisonFormal.Clear();
            b.GarrisonTemp.Clear();
            foreach (var id in fleeing)
            {
                var u = sim.GetEntity(id);
                if (u == null) continue;
                if (u.IsSoldier)
                {   // 士兵永不溃逃:清登记留兵团(主将已死→无将续战)
                    u.GarrisonBuildingId = 0; u.TempGarrisonId = 0; u.State = EntityState.Idle;
                    continue;
                }
                var dest = FindRoutDestination(sim, u);
                if (dest == null) { u.GarrisonBuildingId = 0; u.State = EntityState.Idle; continue; }
                var gate = FindGateCell(sim, dest);
                if (gate == null)
                {   // 城门无位则就近找空位
                    for (int d = 1; d <= 8 && gate == null; d++)
                        for (int dr = 0; dr < LaneGrid.Rows && gate == null; dr++)
                        {
                            var p = new GridPos(dr, dest.Pos.Col + (u.Side == Side.Blue ? -d : d));
                            if (IsCellFree(sim, p) && TerrainMap.IsInside(p)) gate = p;
                        }
                }
                u.GarrisonBuildingId = 0;
                // 从城池后方出城,避免被已失守的城堵住退路
                int back = u.Side == Side.Blue ? -1 : 1;
                int behind = back * ((int)System.Math.Ceiling(BuildingFootprint.HalfW(b)) + 1); // 起点须落在失守建筑占地矩形之外
                var start = new GridPos(b.Pos.Row, b.Pos.Col + behind);
                if (!IsCellFree(sim, start))
                {
                    for (int dr = 0; dr < LaneGrid.Rows; dr++)
                    {
                        var alt = new GridPos(dr, b.Pos.Col + behind);
                        if (IsCellFree(sim, alt) && TerrainMap.IsInside(alt)) { start = alt; break; }
                    }
                }
                if (IsCellFree(sim, start)) { u.Pos = start; u.PosF = SimVec2.FromGrid(start); }
                u.State = EntityState.Routing;
                u.HasMoveTarget = true;
                u.MoveTarget = gate ?? dest.Pos;
            }
        }

        /// <summary>建筑化为废墟:士兵只清登记(永不溃逃);非士兵(旧数据)强制后撤(途中遇敌交战 -30%)。</summary>
        public static void OnBuildingRubbled(SimCore sim, Entity b)
        {
            var fleeing = new List<int>(b.GarrisonFormal);
            fleeing.AddRange(b.GarrisonTemp);
            b.GarrisonFormal.Clear();
            b.GarrisonTemp.Clear();
            foreach (var id in fleeing)
            {
                var usedGates = new System.Collections.Generic.HashSet<long>(); // 溃逃单位分派不同城门格,避免堆叠无法抵达
                var u = sim.GetEntity(id);
                if (u == null) continue;
                if (u.IsSoldier)
                {   // 士兵永不溃逃:清登记留兵团
                    u.GarrisonBuildingId = 0; u.TempGarrisonId = 0; u.State = EntityState.Idle;
                    continue;
                }
                var dest = FindRoutDestination(sim, u);
                if (dest == null) { u.GarrisonBuildingId = 0; u.State = EntityState.Idle; continue; }
                var gate = FindGateCell(sim, dest, usedGates);
                if (gate != null)
                    usedGates.Add(((long)gate.Value.Row << 32) | (uint)gate.Value.Col);
                if (gate == null)
                {
                    for (int d = 1; d <= 8 && gate == null; d++)
                        for (int dr = 0; dr < LaneGrid.Rows && gate == null; dr++)
                        {
                            var p = new GridPos(dr, dest.Pos.Col + (u.Side == Side.Blue ? d : -d));
                            if (IsCellFree(sim, p) && TerrainMap.IsInside(p)) gate = p;
                        }
                }
                u.GarrisonBuildingId = 0;
                int back = u.Side == Side.Blue ? -1 : 1;
                int behind = back * ((int)System.Math.Ceiling(BuildingFootprint.HalfW(b)) + 1); // 起点须落在失守建筑占地矩形之外
                var start = new GridPos(b.Pos.Row, b.Pos.Col + behind);
                if (!IsCellFree(sim, start))
                {
                    for (int dr = 0; dr < LaneGrid.Rows; dr++)
                    {
                        var alt = new GridPos(dr, b.Pos.Col + behind);
                        if (IsCellFree(sim, alt) && TerrainMap.IsInside(alt)) { start = alt; break; }
                    }
                }
                if (IsCellFree(sim, start)) { u.Pos = start; u.PosF = SimVec2.FromGrid(start); }
                u.State = EntityState.Routing;
                u.HasMoveTarget = true;
                u.MoveTarget = gate ?? dest.Pos;
            }
        }

        /// <summary>溃逃目的地:同一线后方(远离敌方向)最近的己方建筑;无则己方大本营。</summary>
        static Entity FindRoutDestination(SimCore sim, Entity u)
        {
            int dir = u.Side == Side.Blue ? -1 : 1; // 远离敌方向
            Entity best = null;
            int bestD = int.MaxValue;
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Building || e.Side != u.Side) continue;
                int d = (e.Pos.Col - u.Pos.Col) * dir;
                if (d <= 0) continue; // 只取后方
                if (d < bestD) { best = e; bestD = d; }
            }
            if (best != null) return best;
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Building && e.Side == u.Side && e.Building == BuildingId.Base)
                    return e;
            return null;
        }

        public static void Tick(SimCore sim, float dt)
        {
            // 0) 城后等候自动解除:主将已死/不存在、主将距城 > 15m、或城不存在 → 离城(士兵跟随主将=截击)
            foreach (var e in sim.Entities.Values.Where(x => x.Kind == EntityKind.Unit && x.TempGarrisonId != 0).ToList())
            {
                var g = e.GeneralId != 0 ? sim.GetEntity(e.GeneralId) : null;
                bool release = g == null || g.Kind != EntityKind.Unit || g.State == EntityState.Dead;
                if (!release)
                {
                    var tb = sim.GetEntity(e.TempGarrisonId);
                    release = tb == null || tb.Kind != EntityKind.Building ||
                              SimVec2.Distance(g.PosF, SimVec2.FromGrid(tb.Pos)) > TempFollowRange;
                }
                if (release)
                {
                    var tb2 = sim.GetEntity(e.TempGarrisonId);
                    if (tb2 != null) tb2.GarrisonTemp.Remove(e.Id);
                    e.TempGarrisonId = 0;
                }
            }

            // 1) 临时驻军:正式有空缺且未封城 → 自动转正式(城守住补入正式)
            foreach (var b in sim.Entities.Values.Where(e => e.Kind == EntityKind.Building).ToList())
            {
                // 2026-09-11 制作人:废除封城,随时可转正(原 Sealed 拦截删除)
                // 敌方攻城意图存续期间不转:临时驻军受保护,防止"转正后被交换杀死"
                if (b.Intent != SiegeIntent.None && b.IntentIssuer != b.Side) continue;
                if (b.IsFort) continue; // 堡垒不可驻军(2026-09-11),无转正一说
                int cap = b.Building == null ? 0 : sim.BuildingConfigOf(b.Building.Value).GarrisonFormal;
                while (b.GarrisonTemp.Count > 0 && b.GarrisonFormal.Count < cap)
                {
                    var id = b.GarrisonTemp[0];
                    b.GarrisonTemp.RemoveAt(0);
                    var u = sim.GetEntity(id);
                    if (u == null) continue;
                    u.TempGarrisonId = 0;
                    DoEnterFormal(sim, u, b.Id, b.GarrisonFormal);
                }
            }

            foreach (var b in sim.Entities.Values.Where(e => e.Kind == EntityKind.Building).ToList())
            {
                // 2026-09-15:废墟不做任何主动行为(与 `TowerCombatSystem` 同一个口径 ——
                // 制作人:"被摧毁的箭塔还是会攻击,有点抽象")。本处当前**恰好**安全:
                // `OnBuildingRubbled` 会把两个驻军名册清空 → 射手守将找不到、近战出击也没名册。
                // 但那是"靠上游顺手清干净"撑着,不是本处的保证;加一道显式闸,顺便省掉
                // 每座废墟对全场单位的 O(n) 威胁扫描。
                if (b.Rubbled) continue;
                var garrison = b.GarrisonFormal.Concat(b.GarrisonTemp).ToList();
                // 2) 城池主动攻击(2026-09-13 制作人拍板两轮修正,替换旧"依托射击"):
                //    触发=城内驻有**射手主将**(坠日/星落的非士兵驻将;士兵不算);
                //    攻击者=该主将(冷却挂主将、弹道特效经 DamageDealt.AttackerId 从主将位置发出);
                //    伤害 5、间隔 0.5s(DPS 10=坠日+星落并肩),数值固定 → 两射手守城 TTK 一致、驻多少主将不叠加(B3);
                //    射程=建筑视野;直伤(先扣盾),不套英雄克制。
                Entity archerGeneral = null;
                foreach (var id in garrison)
                {
                    var u = sim.GetEntity(id);
                    if (u == null || u.IsSoldier || u.Hero == null || !RangedHeroes.Contains(u.Hero.Value)) continue;
                    archerGeneral = u;
                    break;
                }
                if (archerGeneral != null)
                {
                    archerGeneral.AttackCooldown -= dt;
                    if (archerGeneral.AttackCooldown <= 0f)
                    {
                        var tgt = FindRangedTarget(sim, b, VisionRadiusOf(b));
                        if (tgt != null)
                        {
                            float dmg = CityAttackDamage;
                            if (tgt.Shield > 0)
                            {
                                float abs = System.Math.Min(tgt.Shield, dmg);
                                tgt.Shield -= abs; dmg -= abs;
                            }
                            if (dmg > 0f) tgt.Hp -= dmg;
                            if (tgt.Hp <= 0f && tgt.State != EntityState.Dead)
                            { tgt.State = EntityState.Dead; sim.RemoveEntity(tgt.Id); }
                            archerGeneral.AttackCooldown = CityAttackInterval;
                            sim.Emit(new SimEvent(sim.Tick, SimEventType.DamageDealt, tgt.Id, CityAttackDamage, archerGeneral.Id));
                        }
                        else archerGeneral.AttackCooldown = 0f;   // 无目标不积累冷却
                    }
                }
                // 3) 近战出击:敌单位欧氏≤2.5 米(建筑中心起算)
                var bc = SimVec2.FromGrid(b.Pos);
                bool threat = sim.Entities.Values.Any(e =>
                    e.Kind == EntityKind.Unit && e.Side != b.Side &&
                    e.GarrisonBuildingId == 0 && e.State != EntityState.Dead && e.State != EntityState.Exchanging &&
                    SimVec2.Distance(e.PosF, bc) <= 2.5f);
                if (threat)
                foreach (var id in garrison)
                {
                    var u = sim.GetEntity(id);
                    if (u == null || u.Hero == null || RangedHeroes.Contains(u.Hero.Value)) continue;
                    var gate = FindGateCell(sim, b);
                    if (gate == null) break;
                    b.GarrisonFormal.Remove(id); b.GarrisonTemp.Remove(id);
                    u.Pos = gate.Value; u.PosF = SimVec2.FromGrid(gate.Value);
                    u.GarrisonBuildingId = 0; u.TempGarrisonId = 0; u.State = EntityState.Idle;
                }
            }

            // 4) 建筑脱战回复(D27):非大本营
            foreach (var b in sim.Entities.Values.Where(e => e.Kind == EntityKind.Building).ToList())
            {
                if (b.Building == BuildingId.Base) continue;
                // 2026-09-15:废墟不回血。原来只挡大本营,于是**已被拆平的废墟**在脱战后按 4HP/s 悄悄涨回去
                // (探针实测:箭塔被打成 HP 0 之后 10 秒涨到 40.4)。当前没有玩法依赖废墟血量,
                // 但"废墟 HP > 0"让"还在/没了"在数值上不可区分,是下一颗雷。
                if (b.Rubbled) continue;
                b.SecondsSinceCombat += dt;
                if (b.SecondsSinceCombat >= RegenDelaySec && b.Hp < b.MaxHp)
                    b.Hp = System.Math.Min(b.MaxHp, b.Hp + RegenPerSec * dt);
            }
        }

        public static GridPos RallyPointOf(Side side) =>
            side == Side.Blue ? new GridPos(15, 270) : new GridPos(15, 16);

        private static void DoEnterFormal(SimCore sim, Entity u, int buildingId, List<int> list)
        {
            sim.FreeCell(u.Pos);
            u.GarrisonBuildingId = buildingId;
            u.State = EntityState.Idle;
            u.HasMoveTarget = false;
            list.Add(u.Id);
        }

        private static void DoEnterTemp(Entity u, int buildingId, List<int> list)
        {   // 城后等候:只登记,不冻结移动(士兵保持跟随主将)
            u.TempGarrisonId = buildingId;
            list.Add(u.Id);
        }

        // 2026-09-14 ★制作人"点驻军后主将没进去、进去的士兵消失了"的真因:
        // 旧 Rollback 只把单位从**名册**里删掉,**没有清 GarrisonBuildingId** —— 于是那些单位
        // 既不在任何名册里(释放/PruneStale 都找不到),又被视图按"城内"隐藏(`EntityView.Tick`) →
        // **永久幽灵**,玩家侧看就是"进去的士兵消失了"。(与旧 Exit 的幽灵同源,已一并按同一口径修。)
        // 现在:入场先**全量校验**,任一不合法就整批不落地;真要回滚也把登记状态一起还原。
        private static void Rollback(SimCore sim, List<int> accepted, int buildingId)
        {
            var b = sim.GetEntity(buildingId);
            if (b == null) return;
            foreach (var id in accepted)
            {
                b.GarrisonFormal.Remove(id);
                var u = sim.GetEntity(id);
                if (u != null) { u.GarrisonBuildingId = 0; u.State = EntityState.Idle; }
            }
        }
        private static void RollbackTemp(SimCore sim, List<int> accepted, int buildingId)
        {
            var b = sim.GetEntity(buildingId);
            if (b == null) return;
            foreach (var id in accepted)
            {
                b.GarrisonTemp.Remove(id);
                var u = sim.GetEntity(id);
                if (u != null) u.TempGarrisonId = 0;
            }
        }

        /// <summary>城门位置(D18,M8.1b):占地矩形敌侧缘 +0.6 米列起,行优先建筑行(±ceil(HalfH) 内扩散),椭圆内、非水域,列向外延展找最近空位;全占返回 null。</summary>
        private static GridPos? FindGateCell(SimCore sim, Entity b, System.Collections.Generic.HashSet<long> exclude = null)
        {
            int dir = b.Side == Side.Blue ? 1 : -1; // 朝敌侧:蓝方敌人在 +x、红方敌人在 -x(蓝基地列 15、红基地列 271)
            var gateF = SimVec2.FromGrid(b.Pos) + new SimVec2(dir * (BuildingFootprint.HalfW(b) + 0.6f), 0f);
            int gateCol = gateF.ToGrid().Col;
            int rh = (int)System.Math.Ceiling(BuildingFootprint.HalfH(b)); // 建筑占地行范围
            int[] rows = { 0, -1, 1, -2, 2, -3, 3 }; // 行优先建筑行,向外扩散
            for (int d = 0; d <= 4; d++)
            {
                foreach (int r in rows)
                {
                    if (r < -rh || r > rh) continue;
                    var p = new GridPos(b.Pos.Row + r, gateCol + dir * d);
                    if (p.Row < 0 || p.Row >= LaneGrid.Rows || p.Col < 0 || p.Col >= LaneGrid.Cols) continue;
                    if (!TerrainMap.IsInside(p)) continue;
                    if (TerrainMap.IsWater(SimVec2.FromGrid(p))) continue;
                    if (exclude != null && exclude.Contains(((long)p.Row << 32) | (uint)p.Col)) continue; // 溃逃单位分派不同城门格
                    if (IsCellFree(sim, p)) return p;
                }
            }
            return null; // 城门区全占 → 无空位
        }

        private static bool IsCellFree(SimCore sim, GridPos p)
        {
            var c = SimVec2.FromGrid(p);
            foreach (var e in sim.Entities.Values)
            {
                if (e.State == EntityState.Dead) continue;
                if (e.Kind == EntityKind.Building && !e.Rubbled && e.Pos == p) return false;
                if (e.Kind == EntityKind.Unit && e.GarrisonBuildingId == 0
                    && SimVec2.Distance(e.PosF, c) < 1.0f) return false;
            }
            return true;
        }

        private static Entity FindRangedTarget(SimCore sim, Entity b, int range)
        {
            Entity best = null; float bestD = float.MaxValue; int bestId = int.MaxValue;
            var bc = SimVec2.FromGrid(b.Pos);
            foreach (var e in sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Unit || e.Side == b.Side || e.Side == Side.Neutral) continue;
                if (e.GarrisonBuildingId != 0 || e.State == EntityState.Dead || e.State == EntityState.Exchanging) continue;
                float d = SimVec2.Distance(e.PosF, bc);
                if (d > range) continue;
                if (d < bestD || (System.Math.Abs(d - bestD) <= 1e-4f && e.Id < bestId)) { best = e; bestD = d; bestId = e.Id; }
            }
            return best;
        }

    }

    // 侧别辅助
    internal static class SideUtilities
    {
        public static Side SideOf(SimCore sim, IReadOnlyList<int> unitIds) =>
            unitIds.Count > 0 && sim.Entities.ContainsKey(unitIds[0]) ? sim.GetEntity(unitIds[0]).Side : Side.Blue;
    }
}
