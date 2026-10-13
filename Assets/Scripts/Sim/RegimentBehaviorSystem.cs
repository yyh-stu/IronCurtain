using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>
    /// 兵团自主行为层(M8.5b):视野索敌自动进攻(最高优先)> 15 格内友军交战自动支援;
    /// 玩家手动命令打断后该次自动决策永久失效(Sup 字段防复读)。纯 tick,无 UnityEngine。
    /// </summary>
    public static class RegimentBehaviorSystem
    {
        public const float SupportRange = 20f;          // 支援判定半径(格/米;2026-09-14 制作人:20 格以内必须支援)
        public const float SupportArriveOffset = 2f;    // 支援落点:友军前方约 2 格
        /// <summary>2026-09-14 制作人"多军团交战时射手尽量在近战后方":射手站在**交战近战身后**多远。
        /// 取 3.5 米 —— 实测 2 米时仍会被近战贴身(敌近战射程 2 米,再加推挤余量)。</summary>
        public const float BehindMeleeOffset = 3.5f;

        public static void Tick(SimCore sim, float dt)
        {
            var snapshot = new List<Entity>(sim.Entities.Values);
            foreach (var g in snapshot)
            {
                if (g.Kind != EntityKind.Unit || g.IsSoldier || g.State == EntityState.Dead) continue;
                // 注(2026-09-14 制作人澄清):"视野内出现敌人自动攻击"是**有意的简化逻辑,玩家方同样生效** ——
                // 本轮一度收窄成"只对 AI 方生效",已按制作人口径还原为双方都生效。不要按"部队自己会往前走"
                // 去删这一层:那是设计,不是 bug。
                if (g.RetreatHold) continue; // M8.7 防拉回:玩家授意撤退后自动层让位,直到玩家再下令(被迫接战由 CombatUpdate 兜底)
                if (g.GarrisonBuildingId != 0) continue;

                // 1) 手动/自动命令执行保护(防拉回):有目标在身一律不动,到位后自然重评估
                if (g.HasMoveTarget) continue;

                // 1b) 2026-09-14 制作人"磐石打不到屏障":军队**走到墙脚站定后** HasMoveTarget 归零,
                //     本层立刻按"视野索敌"给它派新目标 → 它离开墙皮去追远处的敌人,屏障只挨了一刀。
                //     实测对照(同一行军令):开自动 10 秒墙血 200→194 且单位被拉走;
                //     关自动 10 秒 200→182 且稳站墙皮。贴墙单位交给 CombatUpdate 的
                //     "推进遇墙就拆"分支即可(近处来了敌人仍由 CombatUpdate 优先接战)。
                if (WallSystem.BlockedByWall(g)) continue;

                // 2) 到达即清:本次自动决策自然完成,后续按新事件重新评估
                if (g.AutoKind != 0) { g.AutoKind = 0; g.AutoTargetId = 0; }

                // 2-0) ★★ 最优先:支援(2026-09-14 制作人实机口径)
                //   "把 20 格以内队友挨打,除非玩家有调度安排,否则必须支援,写进最优先。"
                //   —— 排在射手靠拢与索敌进攻**之前**;判定对象是**任意友方单位**(含士兵,不再只认主将)。
                //   "除非玩家有调度安排":玩家手动令会清 `AutoKind` 并在上面 1)/`RetreatHold` 里让位,
                //   同一目标被打断过则用 `SupKind==2` 记档,不重复拉回(与既有防复读机制同源)。
                //   自身已在交战中的兵团不跑:它**正在**支援(那就是战线本身),跑去别处会让战线自己崩。
                // 2026-09-14 二次修正(制作人实机:"中路 3v3 磐石在对方脸上却既不攻击也不掉血,是群体事件"):
                // 第一版支援把落点定成"挨打友军身后 2 米",于是**每 tick 都在重定位**、迟迟不投入战斗
                // (探针实测:双方磐石长时间 `AK2` + "未交战" + 敌距 5~6 米 = 既不攻击也不掉血)。
                // 修正:**支援照旧最优先,但近战支援者的目标点改成"正在打它的那个敌人"** —— 走过去就是要打它,
                // 落位后由 CombatUpdate 接战;射手仍站在友军身后(保持"射手在近战后方"的既有口径)。
                // (自身已在交战的团不跑:它就是战线本身 —— 见上面的 SecondsSinceCombat 闸。)
                if (g.SecondsSinceCombat >= MeleeEngagedSeconds)
                {
                    var hurt = NearestFightingAlly(sim, snapshot, g);
                    if (hurt != null && !(g.SupKind == 2 && g.SupTargetId == hurt.Id))
                    {
                        var toHurt = hurt.PosF - g.PosF;
                        var dirS = toHurt.Magnitude > 1e-4f ? toHurt.Normalized() : new SimVec2(1f, 0f);
                        var threat = NearestEnemyTo(snapshot, hurt);
                        if (!DamageCalculator.IsRanged(g) && threat != null)
                        {
                            sim.SetMoveTarget(g.Id, threat.PosF.ToGrid(), false);   // 近战:直接压向打它的敌人
                        }
                        else
                        {
                            // 射手(或无敌人可指):站在挨打友军**靠我方一侧** 2 米处
                            var destS = TerrainMap.ClampToEllipse(hurt.PosF - dirS * SupportArriveOffset);
                            sim.SetMoveTarget(g.Id, destS.ToGrid(), false);
                        }
                        g.AutoKind = 2;
                        g.AutoTargetId = hurt.Id;
                        continue;   // 本 tick 不再评估其它规则
                    }
                }

                // 2a) 2026-09-14 制作人口径(选项 A + "近战交战就直接发动攻击"):
                //     **射手视野内出现"敌方近战单位" → 优先向附近友军靠拢**;但**己方近战一旦交战**,
                //     这条靠拢不再生效 —— 射手直接转入下面的索敌进攻(即"直接发动攻击")。
                //     制作人明确:射手**照常输出**(开火条件不变,**不做 hold-fire**),
                //     "近战交战直接开火"只是**附加**的触发条件,不是"没交战就不许打"的门槛。
                //     防复读沿用既有机制:玩家手动打断后,对**同一个**近战威胁不再自动后撤(SupKind=3)。
                if (DamageCalculator.IsRanged(g))
                {
                    var threat = NearestVisibleMeleeEnemy(snapshot, g);
                    if (threat != null && !(g.SupKind == 3 && g.SupTargetId == threat.Id))
                    {
                        // 锚点:优先站到**己方交战中的近战**身后(制作人:"多军团交战时,射手尽量在近战后方"),
                        // 没有交战的近战就退到最近友军身后。落点 = 该近战身后 2 米
                        // (从敌人指向该近战的方向再往后)。
                        var engaged = NearestEngagedMeleeAlly(snapshot, g);
                        var shield = engaged ?? NearestAllyGeneral(snapshot, g);
                        if (shield != null)
                        {
                            var back = shield.PosF - threat.PosF;
                            var dir = back.Magnitude > 1e-4f ? back.Normalized()
                                : (shield.PosF - g.PosF).Magnitude > 1e-4f ? (shield.PosF - g.PosF).Normalized()
                                : new SimVec2(1f, 0f);
                            // 交战的近战身后要留够间距(否则射手仍被贴身);普通友军身后 2 米即可
                            float off = engaged != null ? BehindMeleeOffset : SupportArriveOffset;
                            var dest = TerrainMap.ClampToEllipse(shield.PosF + dir * off);
                            sim.SetMoveTarget(g.Id, dest.ToGrid(), false);
                            g.AutoKind = 3;
                            g.AutoTargetId = threat.Id;
                            continue;   // 本 tick 不再索敌进攻(开火由 CombatUpdate 负责,不受此影响)
                        }
                    }
                }

                // 3) 索敌进攻(优先级最高)
                var enemy = NearestVisibleEnemy(sim, snapshot, g);
                if (enemy != null)
                {
                    if (g.SupKind == 1 && g.SupTargetId == enemy.Id) continue; // 该敌已被玩家打断过
                    // 2026-09-14 ★制作人"两军在中路大桥交战,会边交战边位移…应该原地交战":
                    // CombatUpdate 一旦进射程就把 HasMoveTarget 清掉,本层下一 tick 见到"无目标"又派一次
                    // "走到敌人格子"的令 → **每个循环往前挪 0.2~0.3 米**;弱势方还会被 SoftCollide 一路推回
                    // 自己大本营方向(实测蓝 6 军团 vs 红 3 军团,从大桥推到上草坪)。
                    // 修:已经在**武器射程内**就别再前压 —— 交战交给 CombatUpdate,原地打。
                    if (SimVec2.Distance(g.PosF, enemy.PosF) <= g.Range + 0.3f) continue;
                    sim.SetMoveTarget(g.Id, enemy.PosF.ToGrid(), false);
                    g.AutoKind = 1;
                    g.AutoTargetId = enemy.Id;
                    continue; // 本 tick 不再评估支援
                }

                // 3b) 2026-09-17 制作人:"20m内的友军在攻击目标时,如果自身附近没有敌人或
                //     锁定不到敌人,就自动去攻击友军在打的敌人。"
                //     索敌(规则3)看不到敌 → 找 20m 内正在交战的友军 → 压向该友军最近的敌人。
                //     与支援规则(2-0)的区别:支援要求"友军**挨打**"且自身 SecondsSinceCombat≥3;
                //     本规则只要"友军**在打人**"(攻击者也标记交战),且**不设冷却闸**——
                //     刚杀完人 1 秒的近战,只要视野内没敌、友军在打,就应立刻参战。
                var fightingAlly = NearestAttackingAlly(snapshot, g);
                if (fightingAlly != null && !(g.SupKind == 4 && g.SupTargetId == fightingAlly.Id))
                {
                    var allyEnemy = NearestEnemyTo(snapshot, fightingAlly);
                    var dest = allyEnemy != null ? allyEnemy.PosF : fightingAlly.PosF;
                    sim.SetMoveTarget(g.Id, dest.ToGrid(), false);
                    g.AutoKind = 4;
                    g.AutoTargetId = fightingAlly.Id;
                    continue;
                }

                // 4) "被迫接战"无需新逻辑:敌方进射程后 CombatUpdate 自动还击
                //    (支援已提到最优先的规则 2-0;旧的"15 格内交战友军"那一版已删除,不留两套口径)
            }
        }

        /// <summary>
        /// 兵团视野内最近可见敌(成员视野口径:士兵 远程6/近战3;主将 近战6/射手9/断峰10)。
        /// 2026-09-14 追加**"挨打 = 看得见"**通道:成员刚挨过打(3 秒内)时,记录里的那个攻击者
        /// **无条件算可见** —— 射手射程 9 米大于近战士兵视野 3 / 近战主将视野 6,否则近战兵团会被
        /// 隔在视野外打死也不还手(制作人:「士兵挨打了,意味着敌人出现在视野内了」)。
        /// </summary>
        static Entity NearestVisibleEnemy(SimCore sim, List<Entity> snapshot, Entity g)
        {
            var members = MembersOf(snapshot, g);
            var knownAttackers = new HashSet<int>();
            foreach (var m in members)
                if (m.SecondsSinceCombat < MeleeEngagedSeconds && m.LastAttackerId != 0)
                    knownAttackers.Add(m.LastAttackerId);
            Entity best = null;
            float bestDist = float.MaxValue;
            foreach (var o in snapshot)
            {
                if (o.Kind != EntityKind.Unit || o.Side == g.Side || o.State == EntityState.Dead) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging) continue;
                if (!VisibleByAny(members, o) && !knownAttackers.Contains(o.Id)) continue;
                float d = SimVec2.Distance(g.PosF, o.PosF);
                if (d < bestDist - 1e-4f || (System.Math.Abs(d - bestDist) <= 1e-4f && best != null && o.Id < best.Id))
                {
                    best = o;
                    bestDist = d;
                }
            }
            return best;
        }

        /// <summary>"交战中"的时间口径(与既有判定同源:伤害结算处把 SecondsSinceCombat 归零,3 秒内算交战中)。</summary>
        public const float MeleeEngagedSeconds = 3f;

        /// <summary>兵团视野内最近的**敌方近战**单位(视觉口径与 NearestVisibleEnemy 完全同源:
        /// 成员视野 士兵远程6/近战3;主将 近战6/射手9/断峰10)。射手的"靠拢"规则只认这一类威胁。</summary>
        static Entity NearestVisibleMeleeEnemy(List<Entity> snapshot, Entity g)
        {
            var members = MembersOf(snapshot, g);
            Entity best = null;
            float bestDist = float.MaxValue;
            foreach (var o in snapshot)
            {
                if (o.Kind != EntityKind.Unit || o.Side == g.Side || o.State == EntityState.Dead) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging) continue;
                if (DamageCalculator.IsRanged(o)) continue;      // 只认敌方**近战**:远程对射不属于"该后收"的局面
                if (!VisibleByAny(members, o)) continue;
                float d = SimVec2.Distance(g.PosF, o.PosF);
                if (d < bestDist - 1e-4f || (System.Math.Abs(d - bestDist) <= 1e-4f && best != null && o.Id < best.Id))
                {
                    best = o;
                    bestDist = d;
                }
            }
            return best;
        }

        /// <summary>附近**正在交战**的己方近战主将(射手要站到它身后)。口径与既有"交战中"同源。</summary>
        static Entity NearestEngagedMeleeAlly(List<Entity> snapshot, Entity g)
        {
            Entity best = null;
            float bestDist = float.MaxValue;
            foreach (var f in snapshot)
            {
                if (f.Id == g.Id || f.Kind != EntityKind.Unit || f.IsSoldier || f.Side != g.Side) continue;
                if (f.State == EntityState.Dead || f.GarrisonBuildingId != 0) continue;
                if (DamageCalculator.IsRanged(f)) continue;                       // 只认近战
                if (f.SecondsSinceCombat >= MeleeEngagedSeconds) continue;         // 未交战
                float d = SimVec2.Distance(g.PosF, f.PosF);
                if (d > SupportRange) continue;
                if (d < bestDist - 1e-4f) { best = f; bestDist = d; }
            }
            return best;
        }

        /// <summary>附近最近的友方主将(SupportRange 内),射手"靠拢"的目标。
        /// 与支援规则的区别:这里**不要求队友正在交战** —— 制作人的口径是"向附近队友靠拢"。</summary>
        static Entity NearestAllyGeneral(List<Entity> snapshot, Entity g)
        {
            Entity best = null;
            float bestDist = float.MaxValue;
            foreach (var f in snapshot)
            {
                if (f.Id == g.Id || f.Kind != EntityKind.Unit || f.IsSoldier || f.Side != g.Side) continue;
                if (f.State == EntityState.Dead || f.GarrisonBuildingId != 0) continue;
                float d = SimVec2.Distance(g.PosF, f.PosF);
                if (d > SupportRange) continue;
                if (d < bestDist - 1e-4f || (System.Math.Abs(d - bestDist) <= 1e-4f && best != null && f.Id < best.Id))
                {
                    best = f;
                    bestDist = d;
                }
            }
            return best;
        }

        /// <summary>离指定单位最近的敌方单位(支援落点用:近战去压"正在打它的那个敌人")。</summary>
        static Entity NearestEnemyTo(List<Entity> snapshot, Entity center)
        {
            Entity best = null; float bestDist = float.MaxValue;
            foreach (var o in snapshot)
            {
                if (o.Kind != EntityKind.Unit || o.Side == center.Side || o.State == EntityState.Dead) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging) continue;
                float d = SimVec2.Distance(center.PosF, o.PosF);
                if (d < bestDist - 1e-4f || (System.Math.Abs(d - bestDist) <= 1e-4f && best != null && o.Id < best.Id))
                { best = o; bestDist = d; }
            }
            return best;
        }

        /// <summary>2026-09-17 制作人:"20m内的友军在攻击目标时…自动去攻击友军在打的敌人"。
        /// 找 20m 内**正在交战**(攻击或被打,SecondsSinceCombat&lt;3)的**友方主将**。
        /// 与 NearestFightingAlly 的区别:那个找"在挨打的任意友方单位"(支援触发),
        /// 这个找"在打人的友方主将"(参战触发)——射手攻击时也标记 SecondsSinceCombat=0 ✓。</summary>
        static Entity NearestAttackingAlly(List<Entity> snapshot, Entity g)
        {
            Entity best = null; float bestDist = float.MaxValue;
            foreach (var f in snapshot)
            {
                if (f.Id == g.Id || f.Kind != EntityKind.Unit || f.Side != g.Side) continue;
                if (f.IsSoldier) continue;                                  // 只看主将
                if (f.State == EntityState.Dead || f.GarrisonBuildingId != 0) continue;
                if (f.State == EntityState.Exchanging) continue;
                if (f.SecondsSinceCombat >= MeleeEngagedSeconds) continue;  // 非交战中
                float d = SimVec2.Distance(g.PosF, f.PosF);
                if (d > SupportRange) continue;
                if (d < bestDist - 1e-4f || (System.Math.Abs(d - bestDist) <= 1e-4f && best != null && f.Id < best.Id))
                { best = f; bestDist = d; }
            }
            return best;
        }

        /// <summary>
        /// 20 格内**最近的在挨打的友方单位**(2026-09-14 制作人口径:含士兵,不再只认主将)。
        /// "挨打"的时间口径与既有判定同源(`MeleeEngagedSeconds`:伤害结算处把 SecondsSinceCombat 归零)。
        /// </summary>
        static Entity NearestFightingAlly(SimCore sim, List<Entity> snapshot, Entity g)
        {
            Entity best = null;
            float bestDist = float.MaxValue;
            foreach (var f in snapshot)
            {
                if (f.Id == g.Id || f.Kind != EntityKind.Unit || f.Side != g.Side) continue;
                if (f.State == EntityState.Dead || f.GarrisonBuildingId != 0) continue;
                if (f.State == EntityState.Exchanging) continue;
                // **同团不算"友军需要支援"**:主将和它的士兵本来就是一个单位(2026-09-14 口径),
                // 士兵挨打时主将该做的是继续打(阵型/接战由 RegimentSystem 与 CombatUpdate 负责),
                // 而不是把自己当外人跑去"支援"自己的兵。
                if (f.IsSoldier && f.GeneralId == g.Id) continue;
                if (f.SecondsSinceCombat >= MeleeEngagedSeconds) continue; // 非交战中
                float d = SimVec2.Distance(g.PosF, f.PosF);
                if (d > SupportRange) continue;
                if (d < bestDist - 1e-4f || (System.Math.Abs(d - bestDist) <= 1e-4f && best != null && f.Id < best.Id))
                {
                    best = f;
                    bestDist = d;
                }
            }
            return best;
        }

        static List<Entity> MembersOf(List<Entity> snapshot, Entity g)
        {
            var list = new List<Entity> { g };
            foreach (var e in snapshot)
            {
                if (!e.IsSoldier || e.GeneralId != g.Id || e.State == EntityState.Dead) continue;
                if (e.GarrisonBuildingId != 0) continue;
                list.Add(e);
            }
            return list;
        }

        static bool VisibleByAny(List<Entity> members, Entity enemy)
        {
            foreach (var m in members)
                if (SimVec2.Distance(m.PosF, enemy.PosF) <= VisionRadiusOf(m)) return true;
            return false;
        }

        /// <summary>
        /// 视野半径(**必须与 `VisionSystem.UnitRadius` 一字不差**,否则迷雾与索敌会各说各话):
        /// 近战主将 8 / 近战兵 4(=主将一半);射手主将 9 / **射手兵 9(=主将,不折半)**。
        /// 2026-09-14 制作人口径(原为 近战主将 6 / 近战兵 3 / 断峰 10 / 射手 6·9);
        /// **断峰=鹰眼 10 是专属被动,按制作人澄清保留**(本层与 VisionSystem 的差别仅在
        /// VisionSystem 还要求"它是己方最靠前的野战单位",本层按既有实现不判 —— 保持原样)。
        /// </summary>
        static float VisionRadiusOf(Entity e)
        {
            bool ranged = e.Hero == HeroId.ZhuiRi || e.Hero == HeroId.XingLuo;
            if (e.IsSoldier) return ranged ? 9f : 4f;
            if (e.Hero == HeroId.DuanFeng) return 10f;   // 鹰眼(断峰专属被动)
            if (e.Hero == null) return 8f;
            return ranged ? 9f : 8f;
        }
    }
}
