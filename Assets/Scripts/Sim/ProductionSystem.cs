using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 生产系统(M8.4b 将兵制):主基地产将,20s/将,出场自带满编 20 兵兵团;
    /// 名额制校验、取消返还、出生点占用等待、自动行军。兵营只承担补兵,不产任何单位。
    /// 纯 tick 驱动,无 UnityEngine 依赖。出生点贴基地占地前缘(M8.1a 规则)。
    /// </summary>
    public static class ProductionSystem
    {
        /// <summary>将产出节奏:20s/将(暂定,后续平衡再调)。</summary>
        public const float GeneralProductionSeconds = 20f;

        /// <summary>浮点比较容差(防 0.1f 累积漂移)。</summary>
        private const float Epsilon = 1e-4f;

        /// <summary>出生候选行序:同排/上/下/再上/再下(从近到远)。</summary>
        // 2026-09-14 制作人:"已产出兵团在出生点堆叠" → 加宽**横向停靠面**。
        // 落位搜索的顺序是"先在同一纵深横向铺开,这一排满了才往敌方向推进一列",
        // 所以横向候选行数就是这个"基地前沿停车场"的宽度。原来只有 ±2 行(约 5 米),
        // 近处一排很快占满 → 后续兵团连续向敌方向排成一列,看着像自己在往前走。
        // 现放宽到 ±5 行(约 11 米,车路宽 14 米),产出的兵团在基地前横向排开;
        // 走水/越界/压建筑/贴单位的候选仍逐格剔除(见 TrySpawnGeneral)。
        // 注:玩家方的产出不再自动推进(见下面 IsAiControlled 闸门),所以停车位满时会走
        // "WaitingForSpawn → 总览面板显示(等位)"的既有路径,不会悄悄丢单。
        private static readonly int[] SpawnDRows = { 0, -1, 1, -2, 2, -3, 3, -4, 4, -5, 5 };

        public static void Tick(SimCore sim, float dt)
        {
            // 快照遍历:出生会向 Entities 字典添加实体,禁止边枚举边修改
            var snapshot = new List<Entity>(sim.Entities.Values);

            foreach (var e in snapshot)
            {
                if (e.Kind != EntityKind.Building) continue;
                if (e.Building != BuildingId.Base) continue; // 只有主基地产将
                var prod = e.Prod;
                if (prod == null) continue;

                for (int i = 0; i < prod.Slots.Length; i++)
                {
                    var slot = prod.Slots[i];

                    // 空闲槽开工:名额校验通过才从待产队列取出(满则等待,不报错)
                    if (slot.Current == null && slot.Pending.Count > 0)
                    {
                        if (sim.PopulationOf(e.Side) < sim.PopulationCapOf(e.Side))
                        {
                            slot.Current = slot.Pending.Dequeue();
                            slot.Progress = 0f;
                            slot.WaitingForSpawn = false;
                        }
                    }

                    if (slot.Current != null)
                    {
                        float secs = (slot.Current.Value == HeroId.ZhenYue && sim.IsUpgraded(e.Side, HeroId.ZhenYue))
                            ? (float)sim.Game.Production.ZhenYueUpgradedSeconds : GeneralProductionSeconds; // 迅捷:镇岳 7.5s
                        secs *= (float)HexSystem.ProductionSecondsMultiplier(sim, e.Side); // 快速部署:产速+40%(三十七轮接线,此前无人调用)
                        if (!slot.WaitingForSpawn && slot.Progress + dt < secs - Epsilon)
                        {
                            slot.Progress += dt;
                            continue;
                        }

                        // 完成或等待出生中:尝试出生(内含名额再校验)
                        if (TrySpawnGeneral(sim, e, slot.Current.Value, out var general))
                        {
                            slot.Current = null;
                            slot.Progress = 0f;
                            slot.WaitingForSpawn = false;
                            // 2026-09-14 制作人:"玩家刚生产出来的士兵还是会自动寻路" ——
                            // 产出后的自动行军到集结点原来对**双方**都下,玩家的兵团一出生就自己走。
                            // 现按"AI 的调度不套到玩家头上"改为**只给 AI 方**下这道令(AiSystem.IsAiControlled);
                            // 玩家的产出留在出生点听指挥(视野内出现敌人的自动攻击仍对双方生效,那条是制作人拍过板的)。
                            if (AiSystem.IsAiControlled(e.Side))
                                sim.SetMoveTarget(general.Id, sim.RallyPointOf(e.Side));
                        }
                        else
                        {
                            slot.WaitingForSpawn = true; // 名额满或出生点全占,下 tick 重试
                        }
                    }
                }
            }
        }

        /// <summary>在基地朝敌侧前缘出生完整兵团(名额再校验 + M8.1a 前缘规则)。</summary>
        private static bool TrySpawnGeneral(SimCore sim, Entity b, HeroId heroId, out Entity general)
        {
            general = null;

            // 名额再校验:满则等待
            if (sim.PopulationOf(b.Side) >= sim.PopulationCapOf(b.Side)) return false;

            int dir = b.Side == Side.Blue ? 1 : -1;

            // 全图非废墟建筑占地格集合(多格占用,M8.1a)
            var occupied = new HashSet<long>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Building && !e.Rubbled)
                    foreach (var cell in BuildingFootprint.CellsOccupied(e))
                        occupied.Add(((long)cell.Row << 32) | (uint)cell.Col);

            // 前缘列螺旋外扩:基地正前方被其他建筑(如紧邻兵营)堵住时,向外找可落兵团的位置
            int baseCol = b.Pos.Col + dir * ((int)System.Math.Ceiling(BuildingFootprint.HalfW(b)) + RegimentSystem.FormationRows + 1); // 再让出阵型纵深(4 排),兵团不压基地
            for (int d = 0; d <= 40; d++) // 外扩 40:出生带可能被中立点/兵团阵型块占满(如测试场景 116..136 挤满),保底不死锁
            {
            int col = baseCol + dir * d;
            for (int i = 0; i < SpawnDRows.Length; i++)
            {
                int row = b.Pos.Row + SpawnDRows[i];
                if (row < 0 || row >= LaneGrid.Rows || col < 0 || col >= LaneGrid.Cols) continue;
                var cand = new GridPos(row, col);
                if (!TerrainMap.IsInside(cand)) continue;                    // 椭圆外不可出生
                if (TerrainMap.IsWater(SimVec2.FromGrid(cand))) continue;    // 水域不可出生
                long key = ((long)row << 32) | (uint)col;
                if (occupied.Contains(key)) continue;

                // 连续坐标下出生点还要避开既有单位(0.5 米内)
                var center = SimVec2.FromGrid(cand);
                bool tooClose = false;
                foreach (var e in sim.Entities.Values)
                    if (e.Kind == EntityKind.Unit && SimVec2.Distance(e.PosF, center) < 0.5f)
                    { tooClose = true; break; }
                if (tooClose) continue;

                // 兵团整体出生校验:满编兵阵型不得压建筑占地、不得贴既有单位(并行产将时错峰)
                // 2026-09-10:改用 SlotOffset 统一(12 兵双态:出生时主将 Idle→圆阵)
                bool formationClear = true;
                var probeG = new Entity(0, b.Side, EntityKind.Unit, cand, 1f) { PosF = center };
                for (int s2 = 0; s2 < RegimentSystem.CompanySize(probeG) && formationClear; s2++)
                {
                    var sp = center + RegimentSystem.SlotOffset(probeG, s2);
                    var scell = sp.ToGrid();
                    if (occupied.Contains(((long)scell.Row << 32) | (uint)scell.Col)) { formationClear = false; break; }
                    foreach (var e2 in sim.Entities.Values)
                        if (e2.Kind == EntityKind.Unit && SimVec2.Distance(e2.PosF, sp) < 0.5f) { formationClear = false; break; }
                }
                if (!formationClear) continue;

                general = RegimentSystem.SpawnGeneral(sim, b.Side, heroId, cand); // 主将 + 满编 20 兵
                return true;
            }
            }
            return false;
        }

        public static bool Enqueue(SimCore sim, int baseId, HeroId hero, out string error)
        {
            error = null;
            if (!sim.Entities.TryGetValue(baseId, out var b) ||
                b.Kind != EntityKind.Building || b.Building != BuildingId.Base)
            {
                error = "目标不是主基地";
                return false;
            }
            var prod = b.Prod;
            if (prod == null) { error = "基地无生产集"; return false; }

            prod.Slots[BestSlotFor(prod)].Pending.Enqueue(hero);
            return true;
        }

        /// <summary>
        /// 挑生产槽(2026-09-14 制作人:"快速按下三个将名时进度条只跑一个 … 上限同时产三个")。
        /// 规则:**优先空闲槽**(无在产、无排队)→ 其次排队最少的 → 再次索引最小。
        /// 旧实现只比"排队数最少",而**三个空槽的排队数都是 0**,严格小于(`&lt;`)永不成立
        /// → 连按三下全部挤进 0 号槽,只有那一条进度条在跑(其余两槽空着)。
        /// </summary>
        private static int BestSlotFor(ProductionSet prod)
        {
            int best = 0; int bestScore = int.MaxValue;
            for (int i = 0; i < prod.Slots.Length; i++)
            {
                var slot = prod.Slots[i];
                bool idle = slot.Current == null && slot.Pending.Count == 0;
                int score = (idle ? 0 : 1000) + slot.Pending.Count * 10 + i;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        public static bool CancelLast(SimCore sim, int baseId, int slotIndex, out string error)
        {
            error = null;
            if (!sim.Entities.TryGetValue(baseId, out var b) ||
                b.Kind != EntityKind.Building || b.Building != BuildingId.Base)
            {
                error = "目标不是主基地";
                return false;
            }
            var prod = b.Prod;
            if (prod == null) { error = "基地无生产集"; return false; }
            if (slotIndex < 0 || slotIndex >= prod.Slots.Length) { error = "槽索引越界"; return false; }

            var slot = prod.Slots[slotIndex];
            if (slot.Pending.Count > 0)
            {
                var list = new List<HeroId>(slot.Pending);
                list.RemoveAt(list.Count - 1);
                slot.Pending.Clear();
                foreach (var h in list) slot.Pending.Enqueue(h);
                return true;
            }
            if (slot.Current != null)
            {
                slot.Current = null;
                slot.Progress = 0f;
                slot.WaitingForSpawn = false;
                return true;
            }
            error = "队列为空";
            return false;
        }

        /// <summary>建筑销毁/废墟化时清空生产集(保留原名,M8.4b 起作用于基地)。</summary>
        public static void OnBarracksDestroyed(SimCore sim, Entity building)
        {
            if (building == null) return;
            if (building.Prod != null) building.Prod.Clear();
        }
    }
}
