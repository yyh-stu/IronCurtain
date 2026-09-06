using System;
using System.Collections.Generic;

namespace IronCurtain.Sim
{
    public sealed class SimCore
    {
        public SimConfig Config { get; }
        public SeededRng Rng { get; }
        public long Tick { get; private set; }
        public int EntityCount => _entities.Count;
        public IReadOnlyDictionary<int, Entity> Entities => _entities;

        /// <summary>将名额上限(M8.4b 名额制)。测试版 4;正式版 8。</summary>
        public const int GeneralCap = 4;

        /// <summary>游戏数值配置(由调用方注入;null 时战斗模块不生效)。</summary>
        public GameConfig Game { get; set; }

        /// <summary>胜负:大本营被摧毁时置位。</summary>
        public Side? Winner { get; private set; }
        public void SetWinner(Side side) { Winner = side; }

        // 补兵池(M8.4a):按阵营的全局池
        public readonly Dictionary<Side, float> ReinforceAccum = new Dictionary<Side, float> { { Side.Blue, 0f }, { Side.Red, 0f } };
        public readonly Dictionary<Side, int> ReinforcePool = new Dictionary<Side, int> { { Side.Blue, 0 }, { Side.Red, 0 } };

        // 升级(M4):每方已购买的英雄被动
        private readonly Dictionary<Side, HashSet<HeroId>> _upgrades = new Dictionary<Side, HashSet<HeroId>>();

        public bool IsUpgraded(Side side, HeroId hero)
        {
            return _upgrades.TryGetValue(side, out var s) && s.Contains(hero);
        }

        /// <summary>2EP 购买升级,单英雄每局一次。</summary>
        public bool BuyUpgrade(Side side, HeroId hero, out string error)
        {
            error = null;
            if (IsUpgraded(side, hero)) { error = "该英雄已升级"; return false; }
            if (!SpendEp(side, Game.Ep.UpgradeCost)) { error = "EP 不足"; return false; }
            if (!_upgrades.TryGetValue(side, out var s))
            {
                s = new HashSet<HeroId>();
                _upgrades[side] = s;
            }
            s.Add(hero);
            return true;
        }

        // 经济(M2)
        private readonly Dictionary<Side, float> _ep = new Dictionary<Side, float>();
        private readonly Dictionary<Side, float> _epAccum = new Dictionary<Side, float>();
        private readonly Dictionary<Side, GridPos> _rally = new Dictionary<Side, GridPos>
        {
            { Side.Blue, new GridPos(15, 270) },
            { Side.Red, new GridPos(15, 16) },
        };

        private readonly Dictionary<int, Entity> _entities = new Dictionary<int, Entity>();
        private readonly HashSet<int> _occupied = new HashSet<int>();
        private readonly Queue<ICommand> _commandQueue = new Queue<ICommand>();
        private readonly List<Action<SimEvent>> _listeners = new List<Action<SimEvent>>();
        private readonly LaneGrid _grid = new LaneGrid();
        private int _nextId = 1;

        public SimCore(SimConfig config, SeededRng rng)
        {
            Config = config;
            Rng = rng;
        }

        public Entity SpawnEntity(Side side, EntityKind kind, GridPos pos)
        {
            if (!_grid.InBounds(pos))
                throw new InvalidOperationException($"位置越界: {pos}");
            int idx = _grid.ToIndex(pos);
            if (_occupied.Contains(idx))
                throw new InvalidOperationException($"格子已占用: {pos}");
            // 连续坐标下单位不再占格;出生防重叠用距离校验(驻军/城内单位不占位)
            if (kind == EntityKind.Unit)
            {
                var center = SimVec2.FromGrid(pos);
                foreach (var o in _entities.Values)
                    if (o.Kind == EntityKind.Unit && o.GarrisonBuildingId == 0
                        && o.State != EntityState.Dead
                        && SimVec2.Distance(o.PosF, center) < 0.5f)
                        throw new InvalidOperationException($"出生点过近: {pos}");
            }

            var e = new Entity(_nextId++, side, kind, pos, 0f);
            e.PosF = SimVec2.FromGrid(pos);
            _entities[e.Id] = e;
            if (kind == EntityKind.Building)
                _occupied.Add(idx);
            Emit(new SimEvent(Tick, SimEventType.EntitySpawned, e.Id));
            return e;
        }

        public bool RemoveEntity(int id)
        {
            if (!_entities.TryGetValue(id, out var e)) return false;
            if (e.Kind == EntityKind.Building)
                _occupied.Remove(_grid.ToIndex(e.Pos));
            _entities.Remove(id);
            Emit(new SimEvent(Tick, SimEventType.EntityRemoved, id));
            return true;
        }

        public Entity GetEntity(int id) => _entities.TryGetValue(id, out var e) ? e : null;

        /// <summary>释放格位占用(驻军/入城交换等"离格但实体仍在"的场景)。</summary>
        public void FreeCell(GridPos pos)
        {
            _occupied.Remove(_grid.ToIndex(pos));
        }

        public void SubmitCommand(ICommand cmd) => _commandQueue.Enqueue(cmd);

        /// <summary>单位移动指令:目标越界/实体非单位时抛异常。既有两参签名视为手动命令。</summary>
        public void SetMoveTarget(int id, GridPos target) => SetMoveTarget(id, target, true);

        /// <summary>M8.5b 重载:manual=true(玩家/AI 手动)打断自动决策并写防复读字段;manual=false 为自动层专用。</summary>
        public void SetMoveTarget(int id, GridPos target, bool manual)
        {
            if (!_entities.TryGetValue(id, out var e))
                throw new InvalidOperationException($"实体不存在: {id}");
            if (e.Kind != EntityKind.Unit)
                throw new InvalidOperationException($"非线单位不可设置移动目标: {id}");
            if (!_grid.InBounds(target))
                throw new InvalidOperationException($"移动目标越界: {target}");

            if (manual && !e.IsSoldier)
            {
                if (e.AutoKind != 0) { e.SupKind = e.AutoKind; e.SupTargetId = e.AutoTargetId; }
                e.AutoKind = 0;
                e.AutoTargetId = 0;
            }

            e.MoveTarget = target;
            e.HasMoveTarget = true;
            e.State = EntityState.Moving;
        }

        public void Step()
        {
            Tick++;
            Emit(new SimEvent(Tick, SimEventType.StepBegin, 0));
            while (_commandQueue.Count > 0)
            {
                var cmd = _commandQueue.Dequeue();
                cmd.Execute(this);
                Emit(new SimEvent(Tick, SimEventType.CommandExecuted, 0));
            }
            MoveUnits();
            CombatUpdate();
            EconomyUpdate();
            SiegeSystem.Tick(this, 1f / Config.TickRate);
            TowerCombatSystem.Tick(this, 1f / Config.TickRate);
            GarrisonSystem.Tick(this, 1f / Config.TickRate);
            DestroySystem.Tick(this, 1f / Config.TickRate);
            HexSystem.Tick(this, 1f / Config.TickRate);
            VisionSystem.Tick(this, 1f / Config.TickRate);
            AiSystem.Tick(this, 1f / Config.TickRate);
            if (Config.AutoBehavior) RegimentBehaviorSystem.Tick(this, 1f / Config.TickRate); // M8.5b 兵团自主行为
            Emit(new SimEvent(Tick, SimEventType.StepEnd, 0));
        }

        public void AddEventListener(Action<SimEvent> listener) => _listeners.Add(listener);
        public void ClearEventListeners() => _listeners.Clear();

        // ===== 经济与生产挂钩(M2;M8.4b 人口→名额制)=====

        /// <summary>生成建筑实体(归属方 owner)。中立建筑无血量;已方建筑按配置填数值。</summary>
        public Entity SpawnBuilding(Side owner, BuildingId type, GridPos pos)
        {
            var e = SpawnEntity(owner, EntityKind.Building, pos);
            e.Building = type;
            if (Game != null && owner != Side.Neutral)
            {
                var c = BuildingConfigOf(type);
                e.MaxHp = c.MaxHp;
                e.Hp = c.MaxHp;
                e.Attack = c.Attack;
                e.AttackIntervalSec = (float)c.AttackIntervalSec;
                e.Range = c.Range;
            }
            return e;
        }

        public BuildingCfg BuildingConfigOf(BuildingId id)
        {
            foreach (var b in Game.Buildings)
                if (b.Id == id) return b;
            throw new InvalidOperationException("建筑配置缺失: " + id);
        }

        /// <summary>某方拥有的资源点数量(按场上建筑实况计算)。</summary>
        public int OwnedResourcePointsOf(Side side)
        {
            int n = 0;
            foreach (var e in _entities.Values)
                if (e.Kind == EntityKind.Building && e.Building == BuildingId.ResourcePoint && e.Side == side)
                    n++;
            return n;
        }

        /// <summary>名额制(M8.4b):每个将恒占 1 名额。</summary>
        public int PopCostOf(HeroId id) => 1;

        /// <summary>已用名额 = 该方存活主将数(非士兵 Unit,名额制 M8.4b)。</summary>
        public int PopulationOf(Side side)
        {
            int used = 0;
            foreach (var e in _entities.Values)
                if (e.Kind == EntityKind.Unit && !e.IsSoldier && e.Side == side && e.State != EntityState.Dead)
                    used++;
            return used;
        }

        /// <summary>名额上限 = GeneralCap(测试版 4;正式版 8)。</summary>
        public int PopulationCapOf(Side side) => GeneralCap;

        public float EpOf(Side side) => _ep.TryGetValue(side, out var v) ? v : 0f;

        public bool SpendEp(Side side, float amount)
        {
            float cur = EpOf(side);
            if (cur < amount) return false;
            _ep[side] = cur - amount;
            return true;
        }

        public GridPos RallyPointOf(Side side) => _rally[side];
        public void SetRallyPoint(Side side, GridPos p) { _rally[side] = p; }

        private void EconomyUpdate()
        {
            if (Game == null) return;
            float dt = 1f / Config.TickRate;

            foreach (Side s in new[] { Side.Blue, Side.Red })
            {
                int pts = OwnedResourcePointsOf(s);
                if (pts <= 0) continue;
                float acc = _epAccum.TryGetValue(s, out var a) ? a : 0f;
                acc += pts * dt;
                float interval = (float)Game.Ep.IntervalSeconds;
                while (acc >= interval)
                {
                    acc -= interval;
                    float cur = EpOf(s);
                    if (cur < Game.Ep.Cap) _ep[s] = cur + 1f;
                }
                _epAccum[s] = acc;
            }

            ProductionSystem.Tick(this, dt);
            ReinforceSystem.Tick(this, dt);
        }

        private void MoveUnits()
        {
            float perTick = 1f / Config.TickRate;
            foreach (var e in _entities.Values)
            {
                if (e.IsSoldier) continue; // 士兵移动全归 RegimentSystem.Tick,不进命令移动分支
                if (e.Kind != EntityKind.Unit || (e.State != EntityState.Moving && e.State != EntityState.Routing) || e.MoveSpeed <= 0f)
                    continue;
                if (e.GarrisonBuildingId != 0) continue; // 驻军单位不占线移动
                if (!e.HasMoveTarget) continue;          // 无目标的 Routing 原地待命

                SimVec2 target = SimVec2.FromGrid(e.MoveTarget);
                SimVec2 delta = target - e.PosF;
                float dist = delta.Magnitude;
                if (dist <= 0.15f)
                {
                    e.HasMoveTarget = false;
                    e.State = EntityState.Idle;
                    e.MoveAccum = 0f;
                    continue;
                }

                float spd = MoveSpeedOf(e);
                float step = System.Math.Min(spd * perTick, dist);
                e.PosF = TerrainMap.ClampToEllipse(e.PosF + delta.Normalized() * step);
                PushOutOfBuildings(e);
                e.PosF = TerrainMap.ClampToEllipse(e.PosF);
                e.Pos = e.PosF.ToGrid();

                e.MoveAccum += step;
                if (e.MoveAccum >= 1f)
                {
                    e.MoveAccum = 0f;
                    Emit(new SimEvent(Tick, SimEventType.UnitAdvanced, e.Id)); // 节流:约每 1 米一次
                }

                if ((target - e.PosF).Magnitude <= 0.15f)
                {
                    e.HasMoveTarget = false;
                    e.State = EntityState.Idle;
                    e.MoveAccum = 0f;
                }
            }
            RegimentSystem.Tick(this, perTick); // 兵团底座(M8.2a):阵型跟随/无将续战,先于软碰撞
            SoftCollide();
        }

        private float MoveSpeedOf(Entity e)
        {
            float spd = e.MoveSpeed * e.Status.MoveMultiplier;
            if (e.IsCharging && e.Hero == HeroId.ZhuFeng && Game != null)
                spd = (float)ChargeSpeedOf(HeroId.ZhuFeng);
            if (e.Hero == HeroId.ZhenYue && Tick >= 1800) // 3 分钟后移速衰减(D11 默认 1.4)
                spd = IsUpgraded(e.Side, HeroId.ZhenYue) ? 2.2f : 1.4f;
            spd *= HexSystem.MoveMultiplierOf(this, e); // 海克斯移速层
            spd *= TerrainMap.MoveFactor(e.PosF);       // 涉水减半/桥上正常
            return spd;
        }

        /// <summary>建筑碰撞:把单位推出建筑占地矩形(连续坐标下的障碍语义,M8.1a 矩形化)。</summary>
        private void PushOutOfBuildings(Entity u)
        {
            foreach (var b in _entities.Values)
            {
                if (b.Kind != EntityKind.Building || b.Rubbled) continue;
                if (!BuildingFootprint.Contains(b, u.PosF)) continue;
                u.PosF = BuildingFootprint.ClosestPoint(b, u.PosF); // 点在矩形内→最小穿透轴推出,幂等无死锁
            }
        }

        /// <summary>单位间软碰撞:间距小于 0.9 米时沿连线对半推开。</summary>
        private void SoftCollide()
        {
            var units = new List<Entity>();
            foreach (var e in _entities.Values)
                if (e.Kind == EntityKind.Unit && e.GarrisonBuildingId == 0
                    && e.State != EntityState.Dead && e.State != EntityState.Exchanging)
                    units.Add(e);

            const float MinDist = 0.9f;
            var moved = new HashSet<int>();
            for (int i = 0; i < units.Count; i++)
            {
                for (int j = i + 1; j < units.Count; j++)
                {
                    var a = units[i];
                    var b = units[j];
                    // 兵团底座(M8.2a):同兵团士兵之间、士兵与己方主将之间不互推,避免阵型震荡
                    if ((a.IsSoldier || b.IsSoldier) && (a.GeneralId == b.Id || b.GeneralId == a.Id || (a.IsSoldier && b.IsSoldier && a.GeneralId != 0 && a.GeneralId == b.GeneralId))) continue;
                    float dx = a.PosF.X - b.PosF.X, dy = a.PosF.Y - b.PosF.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= MinDist * MinDist) continue;
                    float d = (float)System.Math.Sqrt(d2);
                    float push = (MinDist - d) * 0.5f;
                    float nx = d > 1e-6f ? dx / d : 1f;
                    float ny = d > 1e-6f ? dy / d : 0f;
                    a.PosF = TerrainMap.ClampToEllipse(a.PosF + new SimVec2(nx * push, ny * push));
                    b.PosF = TerrainMap.ClampToEllipse(b.PosF - new SimVec2(nx * push, ny * push));
                    moved.Add(a.Id);
                    moved.Add(b.Id);
                }
            }
            // 仅被推挤的单位回写格(避免覆盖未移动单位的手动定位)
            foreach (var e in units)
                if (moved.Contains(e.Id))
                    e.Pos = e.PosF.ToGrid();
        }

        /// <summary>建筑外接半径(米)= Max(HalfW,HalfH)+0.4。兼容旧近战判定,下一批 M8.1b 换精确矩形距;新代码请用 BuildingFootprint。</summary>
        public static float BuildingCollisionRadius(Entity b)
        {
            return System.Math.Max(BuildingFootprint.HalfW(b), BuildingFootprint.HalfH(b)) + 0.4f;
        }

        private void CombatUpdate()
        {
            if (Game == null) return;
            float dt = 1f / Config.TickRate;
            var dead = new List<int>();

            foreach (var e in _entities.Values)
            {
                if (e.Kind != EntityKind.Unit || e.State == EntityState.Dead) continue;
                if (e.GarrisonBuildingId != 0) continue; // 驻军单位不参与场外逻辑

                e.Status.Tick(dt);
                e.SecondsSinceCombat += dt;
                HeroAbilitySystem.UpdatePanShiShield(e, dt);
                if (e.Hero == HeroId.ZhuFeng && e.IsCharging && !HeroAbilitySystem.CanCharge(e, this))
                    e.IsCharging = false;

                if (e.Attack <= 0f) continue;

                if (e.AttackCooldown > 0f)
                    e.AttackCooldown -= dt;

                Entity target = FindNearestEnemy(e, e.State == EntityState.Routing ? 1.4f : float.MaxValue);
                if (target == null)
                {
                    if (e.State == EntityState.Attacking) e.State = EntityState.Idle;
                    continue;
                }

                // 溃逃单位保持 Routing 状态(不可调遣语义),不转入 Attacking
                if (e.State != EntityState.Routing)
                {
                    e.HasMoveTarget = false;
                    e.State = EntityState.Attacking;
                }
                if (e.AttackCooldown <= 0f)
                {
                    e.AttackCooldown = e.AttackIntervalSec * e.Status.AttackIntervalMultiplier;
                    ApplyAttack(e, target, dead);
                }
            }

            foreach (int id in dead)
            {
                if (_entities.TryGetValue(id, out var e))
                {
                    if (e.Hero != null) HexSystem.OnUnitDied(this, e.Side); // 陷阵之志
                    Emit(new SimEvent(Tick, SimEventType.EntityDied, id));
                    if (e.Kind == EntityKind.Building)
                        _occupied.Remove(_grid.ToIndex(e.Pos));
                    _entities.Remove(id);
                    Emit(new SimEvent(Tick, SimEventType.EntityRemoved, id));
                }
            }
        }

        private Entity FindNearestEnemy(Entity e, float maxDist)
        {
            Entity best = null;
            float bestDist = float.MaxValue;
            foreach (var o in _entities.Values)
            {
                if (o.Kind != EntityKind.Unit || o.Side == e.Side || o.State == EntityState.Dead)
                    continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging)
                    continue; // 城内/交换中的单位不可被城外锁定
                float d = SimVec2.Distance(e.PosF, o.PosF);
                float effRange = e.Range + 0.3f; // 近战贴身容差
                if (d > effRange || d > maxDist) continue;
                if (d < bestDist - 1e-4f || (System.Math.Abs(d - bestDist) <= 1e-4f && best != null && o.Id < best.Id))
                {
                    best = o;
                    bestDist = d;
                }
            }
            return best;
        }

        private void ApplyAttack(Entity attacker, Entity defender, List<int> dead)
        {
            attacker.SecondsSinceCombat = 0f;
            defender.SecondsSinceCombat = 0f;

            // 基础层(GDD 公式)
            float baseDmg = DamageCalculator.CalculateDamage(attacker, defender, Game, attacker.IsCharging);

            // 星落升级·诛灭:基础攻击 5 → 6(基础层内替换)
            bool zhuMie = attacker.Hero == HeroId.XingLuo && IsUpgraded(attacker.Side, HeroId.XingLuo);
            if (zhuMie && attacker.Attack > 0f) baseDmg = baseDmg / attacker.Attack * 6f;

            // 叠算层(D28):输出加算、承伤加算、免伤乘算;溃逃并入对应层
            float outSum = attacker.Status.AttackBonus + HexSystem.OutputBonusOf(this, attacker)
                         + (attacker.State == EntityState.Routing ? -0.3f : 0f);
            float takeSum = defender.Status.DamageTakenBonus + HexSystem.DamageTakenBonusOf(this, defender)
                          + (defender.State == EntityState.Routing ? 0.3f : 0f);
            float reduce = defender.Status.DamageReduction * HexSystem.DamageReductionOf(this, defender);
            float dmg = baseDmg * (1f + outSum) * (1f + takeSum) * reduce;

            // 附加项(层后加法)
            if (zhuMie) dmg += 0.01f * defender.MaxHp;

            // 护盾优先吸收,溢出扣生命
            float remaining = dmg;
            if (defender.Shield > 0f)
            {
                float absorbed = Math.Min(defender.Shield, remaining);
                defender.Shield -= absorbed;
                remaining -= absorbed;
            }
            defender.Hp -= remaining;

            Emit(new SimEvent(Tick, SimEventType.DamageDealt, defender.Id, dmg));

            HeroAbilitySystem.ApplyOnHitEffects(this, attacker, defender);

            // 断峰升级·破军:身前 90 度扇形(半径 2 米)内全部敌人受单次 75% 伤害
            if (attacker.Hero == HeroId.DuanFeng && IsUpgraded(attacker.Side, HeroId.DuanFeng))
                Cleave(attacker, defender, dead);

            if (defender.Hp <= 0f)
            {
                defender.State = EntityState.Dead;
                dead.Add(defender.Id);
            }
        }

        private void Cleave(Entity attacker, Entity mainTarget, List<int> dead)
        {
            float facing = mainTarget.PosF.X >= attacker.PosF.X ? 1f : -1f;
            foreach (var o in _entities.Values)
            {
                if (o.Id == mainTarget.Id) continue;
                if (o.Kind != EntityKind.Unit || o.Side == attacker.Side || o.State == EntityState.Dead) continue;
                if (o.GarrisonBuildingId != 0 || o.State == EntityState.Exchanging) continue;
                float dx = o.PosF.X - attacker.PosF.X;
                float dy = o.PosF.Y - attacker.PosF.Y;
                if (dx * facing < 0) continue;         // 身后不打
                if (dx * dx + dy * dy > 4f) continue;  // 半径 2 米扇形
                float baseDmg = 0.75f * DamageCalculator.CalculateDamage(attacker, o, Game, attacker.IsCharging);
                float outSum = attacker.Status.AttackBonus + (attacker.State == EntityState.Routing ? -0.3f : 0f);
                float takeSum = o.Status.DamageTakenBonus + (o.State == EntityState.Routing ? 0.3f : 0f);
                float dmg = baseDmg * (1f + outSum) * (1f + takeSum) * o.Status.DamageReduction;
                float remaining = dmg;
                if (o.Shield > 0f)
                {
                    float absorbed = System.Math.Min(o.Shield, remaining);
                    o.Shield -= absorbed;
                    remaining -= absorbed;
                }
                o.Hp -= remaining;
                Emit(new SimEvent(Tick, SimEventType.DamageDealt, o.Id, dmg));
                if (o.Hp <= 0f) { o.State = EntityState.Dead; dead.Add(o.Id); }
            }
        }

        private double ChargeSpeedOf(HeroId id)
        {
            foreach (var h in Game.Heroes)
                if (h.Id == id) return h.ChargeSpeed;
            return 0;
        }

        private void Emit(SimEvent ev)
        {
            for (int i = _listeners.Count - 1; i >= 0; i--)
                _listeners[i]?.Invoke(ev);
        }
    }
}
