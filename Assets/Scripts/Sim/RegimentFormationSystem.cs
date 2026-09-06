using System;
using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>英雄兵种分类(M8.5c 自动布阵)。</summary>
    public enum HeroClass { Heavy, Warrior, Archer }

    /// <summary>
    /// 自动布阵规划器(M8.5c,纯 Sim 逻辑):玩家框选多兵团按 T 自动布阵。
    /// 重装在前、战士居中、射手在后;多向受敌(方位极差>120°)射手居中、近战屏风。
    /// </summary>
    public static class RegimentFormationSystem
    {
        public const float RegimentSpacing = 6f;   // 兵团横距(5 列阵宽 +1)
        public const float RowSpacing = 6f;        // 前中后排距
        public const float ScreenRadius = 4f;      // 屏风弧半径

        public sealed class Plan
        {
            public Dictionary<int, GridPos> Targets = new Dictionary<int, GridPos>(); // generalId → 目标格
            public Dictionary<int, SimVec2> Facings = new Dictionary<int, SimVec2>(); // generalId → 朝向
            public bool ScreenMode;                                                   // 多向受敌屏风
        }

        public static HeroClass ClassOf(HeroId h)
        {
            switch (h)
            {
                case HeroId.PanShi:
                case HeroId.ZhenYue:
                    return HeroClass.Heavy;
                case HeroId.DuanFeng:
                case HeroId.ZhuFeng:
                    return HeroClass.Warrior;
                default:
                    return HeroClass.Archer; // ZhuiRi / XingLuo
            }
        }

        /// <summary>视野半径(同 RegimentBehaviorSystem/VisionSystem 口径):士兵远程 6/近战 3;断峰 10;射手 9;近战 6。</summary>
        private static float VisionOf(SimCore sim, Entity e)
        {
            bool ranged = e.Hero == HeroId.ZhuiRi || e.Hero == HeroId.XingLuo;
            if (e.IsSoldier) return ranged ? 6f : 3f;
            if (e.Hero == HeroId.DuanFeng) return 10f;
            return ranged ? 9f : 6f;
        }

        public static Plan PlanFormation(SimCore sim, IReadOnlyList<int> generalIds)
        {
            var plan = new Plan();

            // 1) 收集存活主将
            var gens = new List<Entity>();
            if (generalIds != null)
                foreach (int id in generalIds)
                {
                    var g = sim.GetEntity(id);
                    if (g == null || g.Kind != EntityKind.Unit || g.IsSoldier || g.State == EntityState.Dead) continue;
                    gens.Add(g);
                }
            if (gens.Count == 0) return plan;

            // 同阵营假设:按第一个主将阵营计
            Side side = gens[0].Side;

            // 2) 质心
            var c = new SimVec2(0f, 0f);
            foreach (var g in gens) c += g.PosF;
            c = c * (1f / gens.Count);

            // 3) 敌集 E:任一选中兵团成员(主将或其存活士兵)视野内的敌方 Unit
            var members = new List<Entity>();
            var memberVision = new List<float>();
            foreach (var g in gens)
            {
                members.Add(g); memberVision.Add(VisionOf(sim, g));
                foreach (var o in sim.Entities.Values)
                    if (o.Kind == EntityKind.Unit && o.IsSoldier && o.GeneralId == g.Id && o.State != EntityState.Dead)
                    { members.Add(o); memberVision.Add(VisionOf(sim, o)); }
            }
            var enemies = new List<Entity>();
            foreach (var o in sim.Entities.Values)
            {
                if (o.Kind != EntityKind.Unit || o.Side == side || o.State == EntityState.Dead) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging) continue;
                for (int i = 0; i < members.Count; i++)
                    if (SimVec2.Distance(members[i].PosF, o.PosF) <= memberVision[i]) { enemies.Add(o); break; }
            }

            // 4) 朝向与模式
            var baseDir = side == Side.Blue ? new SimVec2(1f, 0f) : new SimVec2(-1f, 0f);
            var fac = baseDir;
            if (enemies.Count > 0)
            {
                var ec = new SimVec2(0f, 0f);
                foreach (var e in enemies) ec += e.PosF;
                ec = ec * (1f / enemies.Count);
                var d = ec - c;
                if (d.SqrMagnitude > 1e-8f) fac = d.Normalized();
                // 方位角分布:极差(最小包含弧)>120° → 屏风
                var angles = new List<double>();
                foreach (var e in enemies)
                    angles.Add(Math.Atan2(e.PosF.Y - c.Y, e.PosF.X - c.X) * 180.0 / Math.PI);
                angles.Sort();
                double maxGap = 0.0;
                for (int i = 0; i < angles.Count; i++)
                {
                    double gap = (i + 1 < angles.Count) ? angles[i + 1] - angles[i] : angles[0] + 360.0 - angles[angles.Count - 1];
                    if (gap > maxGap) maxGap = gap;
                }
                double spread = 360.0 - maxGap;
                plan.ScreenMode = spread > 120.0;
            }

            if (plan.ScreenMode) PlanScreen(plan, gens, c, fac, enemies);
            else PlanLine(plan, gens, c, fac);

            // 7) 目标格 Clamp 后 ToGrid
            var ids = new System.Collections.Generic.List<int>(plan.Targets.Keys);
            foreach (int id in ids)
            {
                var p = SimVec2.FromGrid(plan.Targets[id]);
                plan.Targets[id] = TerrainMap.ClampToEllipse(p).ToGrid();
            }
            return plan;
        }

        /// <summary>常规模式:重装 +fac 前排、战士中排、射手 -fac 后排;每排沿 perp 等距居中排开。</summary>
        private static void PlanLine(Plan plan, List<Entity> gens, SimVec2 c, SimVec2 fac)
        {
            var perp = new SimVec2(fac.Y, -fac.X);
            var rows = new Dictionary<HeroClass, List<Entity>>();
            foreach (var cls in new[] { HeroClass.Heavy, HeroClass.Warrior, HeroClass.Archer }) rows[cls] = new List<Entity>();
            foreach (var g in gens) rows[ClassOf(g.Hero ?? HeroId.PanShi)].Add(g);

            var offsets = new Dictionary<HeroClass, float> { { HeroClass.Heavy, RowSpacing }, { HeroClass.Warrior, 0f }, { HeroClass.Archer, -RowSpacing } };
            foreach (var cls in new[] { HeroClass.Heavy, HeroClass.Warrior, HeroClass.Archer })
            {
                var list = rows[cls];
                if (list.Count == 0) continue; // 该类为空跳过,其余排位不变
                var rowCenter = c + fac * offsets[cls];
                for (int i = 0; i < list.Count; i++)
                {
                    var pos = rowCenter + perp * ((i - (list.Count - 1) * 0.5f) * RegimentSpacing);
                    plan.Targets[list[i].Id] = pos.ToGrid();
                    plan.Facings[list[i].Id] = fac;
                }
            }
        }

        /// <summary>屏风模式:射手居中;近战按敌方位角聚类(≤3 簇,差>60° 分簇)在簇向 ScreenRadius 处屏护。</summary>
        private static void PlanScreen(Plan plan, List<Entity> gens, SimVec2 c, SimVec2 fac, List<Entity> enemies)
        {
            var archers = new List<Entity>();
            var melee = new List<Entity>();
            foreach (var g in gens)
            {
                var cls = ClassOf(g.Hero ?? HeroId.PanShi);
                if (cls == HeroClass.Archer) archers.Add(g); else melee.Add(g);
            }

            var perp = new SimVec2(fac.Y, -fac.X);
            for (int i = 0; i < archers.Count; i++)
            {
                var pos = c + perp * ((i - (archers.Count - 1) * 0.5f) * RegimentSpacing);
                plan.Targets[archers[i].Id] = pos.ToGrid();
                plan.Facings[archers[i].Id] = fac;
            }

            // 敌方位角聚类
            var angles = new List<double>();
            foreach (var e in enemies) angles.Add(Math.Atan2(e.PosF.Y - c.Y, e.PosF.X - c.X));
            angles.Sort();
            var clusters = new List<List<double>> { new List<double> { angles[0] } };
            for (int i = 1; i < angles.Count; i++)
            {
                double gap = angles[i] - angles[i - 1];
                bool wrapMerge = i == angles.Count - 1 && (angles[0] + 2 * Math.PI - angles[i]) * 180.0 / Math.PI <= 60.0;
                if ((gap * 180.0 / Math.PI > 60.0 || wrapMerge) && clusters.Count < 3)
                    clusters.Add(new List<double> { angles[i] });
                else if (wrapMerge) clusters[0].Add(angles[i]);
                else clusters[clusters.Count - 1].Add(angles[i]);
            }

            for (int i = 0; i < melee.Count; i++)
            {
                SimVec2 dir;
                if (i < clusters.Count)
                {
                    double sum = 0.0;
                    foreach (var a in clusters[i]) sum += a;
                    double mean = sum / clusters[i].Count;
                    dir = new SimVec2((float)Math.Cos(mean), (float)Math.Sin(mean)).Normalized();
                }
                else
                {
                    // 多余近战放 fac 主方向弧上
                    double rot = 60.0 * Math.PI / 180.0 * (i - clusters.Count + 1);
                    dir = new SimVec2((float)(fac.X * Math.Cos(rot) - fac.Y * Math.Sin(rot)),
                                      (float)(fac.X * Math.Sin(rot) + fac.Y * Math.Cos(rot))).Normalized();
                }
                var pos = c + dir * ScreenRadius;
                plan.Targets[melee[i].Id] = pos.ToGrid();
                plan.Facings[melee[i].Id] = dir.SqrMagnitude > 1e-8f ? dir : fac;
            }
        }

        /// <summary>应用布阵:设 FacingOverride + 手动级移动指令(清 Auto/写 Sup,防自动层拉回)。</summary>
        public static void Apply(SimCore sim, IReadOnlyList<int> generalIds)
        {
            var plan = PlanFormation(sim, generalIds);
            foreach (var kv in plan.Targets)
            {
                var g = sim.GetEntity(kv.Key);
                if (g == null) continue;
                g.FacingOverride = plan.Facings[kv.Key];
                sim.SetMoveTarget(g.Id, kv.Value, true);
            }
        }
    }
}
