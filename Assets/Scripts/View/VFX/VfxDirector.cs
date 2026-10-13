using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 特效公共基础 + M2 效果路由(依 docs/VFX_PLAN_20260917,EXEC_prompts B-M1.x/B-M2)。
    /// ① 对局根 + 会话代号(自增,句柄防跨局);② 双时钟(战斗钟=帧时×Speed/暂停冻结;UI 钟=unscaled);
    /// ③ 事件→效果路由:DamageDealt(滤墙体自伤/无来源)→命中+出招(同tick同攻击者去重);
    /// SettledDamage→盾承击/破盾/阵亡(结算后语义,不重算伤害)。
    /// 可见性:目标侧效果须受害者可见;出招须攻击者可见。池总量=初始预算 192。
    /// 依赖面:只读 GameBootstrap.Speed/Paused、Sim 事件与只读查询;不写 Sim、不碰玩法。
    /// </summary>
    public sealed class VfxDirector : MonoBehaviour
    {
        public static VfxDirector Current { get; private set; }

        private static long _sessionCounter;   // long 防回绕(M1-B1 审核意见)

        /// <summary>当前对局代号(每次 Init 自增)。效果句柄持有并核对它,过期即回收。</summary>
        public long SessionId { get; private set; }

        public float BattleTime { get; private set; }
        public float BattleDelta { get; private set; }
        public float UiDelta { get; private set; }

        public Transform Root { get; private set; }

        private GameBootstrap _boot;
        private SimCore _sim;
        // 按效果族分池(README §7 初始预算 192:命中96/出招32/盾闪24/破盾24/阵亡16;M6 冻结时收口全局统筹)
        private VfxPool _hitPool, _swingPool, _shieldPool, _breakPool, _deathPool;
        private readonly List<ActiveFx> _active = new List<ActiveFx>();
        private readonly VfxSwingDedup _swings = new VfxSwingDedup();

        public void Init(GameBootstrap boot)
        {
            Current = this;
            _boot = boot;
            _sim = boot != null ? boot.Sim : null;
            SessionId = ++_sessionCounter;
            var go = new GameObject("VfxRoot");
            go.transform.SetParent(transform, false);
            Root = go.transform;

            _hitPool = new VfxPool(96, FxBank.MakeHitSpark);
            _swingPool = new VfxPool(32, FxBank.MakeSwingArc);
            _shieldPool = new VfxPool(24, FxBank.MakeShieldFlash);
            _breakPool = new VfxPool(24, FxBank.MakeShieldBreak);
            _deathPool = new VfxPool(16, () => FxBank.MakeDeath(true));

            if (_sim != null) _sim.AddEventListener(OnSimEvent);
        }

        public void TickClock(float realDelta, float unscaledDelta)
        {
            bool paused = _boot != null && _boot.Paused;
            float speed = _boot != null ? _boot.Speed : 1f;
            BattleDelta = ComputeBattleDelta(realDelta, paused, speed);
            BattleTime += BattleDelta;
            UiDelta = unscaledDelta;
        }

        /// <summary>测试注入口(审核意见 1-1):与 Update 同形的显参版。</summary>
        public void TickClockForTest(float realDelta, float unscaledDelta, bool paused, float speed)
        {
            BattleDelta = ComputeBattleDelta(realDelta, paused, speed);
            BattleTime += BattleDelta;
            UiDelta = unscaledDelta;
        }

        /// <summary>战斗钟增量纯函数:暂停=0,否则 ×倍速(非档位倍速同样成立)。</summary>
        public static float ComputeBattleDelta(float realDelta, bool paused, float speed)
        {
            return paused ? 0f : realDelta * speed;
        }

        private void Update()
        {
            TickClock(Time.deltaTime, Time.unscaledDeltaTime);
            // Q04:战斗钟驱动粒子 simulationSpeed(暂停=0,×N=N;恢复后不补播)
            float simSpeed = BattleDelta > 0f && Time.deltaTime > 0f ? BattleDelta / Time.deltaTime : 0f;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var fx = _active[i];
                if (fx.Done(this))
                {
                    fx.Pool.Return(fx.Go);
                    fx.Return();
                    _active.RemoveAt(i);
                }
                else if (fx._cachedPs != null)
                {
                    // 缓存的 PS 数组直接设置 simulationSpeed(不加分配)
                    for (int j = 0; j < fx._cachedPs.Length; j++)
                    {
                        if (fx._cachedPs[j] == null) continue;
                        var main = fx._cachedPs[j].main;
                        main.simulationSpeed = simSpeed;
                    }
                }
            }
        }

        // ================= 事件路由(M2:命中/出招/盾/破盾/阵亡;更多族自 M3/M4 接入) =================

        private void OnSimEvent(SimEvent ev)
        {
            if (_sim == null || VfxDirector.Current == null) return;
            switch (ev.Type)
            {
                case SimEventType.DamageDealt:
                    OnDamage(ev);
                    break;
                case SimEventType.SettledDamage:
                    OnSettled(ev);
                    break;
                case SimEventType.EntitySpawned:
                    // Q06 修复:区分出生/占领 —— 已有视图=实体已存在=易手;无视图=新实体=出生
                    var spawned = _sim.GetEntity(ev.SubjectId);
                    if (spawned == null) break;
                    var existingView = BoardView.Active != null ? BoardView.Active.GetView(spawned.Id) : null;
                    if (spawned.Kind == EntityKind.Building && existingView != null
                        && spawned.Side == AppFlow.PlayerSide)
                    {
                        // B04 占领易手(已有视图 → 实体已存在 → 归属变化)
                        Spawn(_shieldPool, BoardView.WorldPos(spawned.Pos), FxBank.ShieldTint, 1.5f);
                    }
                    else if (spawned.Kind == EntityKind.Unit && !spawned.IsSoldier
                        && spawned.Side == AppFlow.PlayerSide && existingView == null)
                    {
                        // U02 主将出生(无视图 → 新实体)
                        Spawn(_hitPool, BoardView.WorldPos(spawned.PosF), FxBank.Steel, 1.2f);
                    }
                    break;
                case SimEventType.OvertimeStarted:
                    // U05 加时:全屏边缘效果(当前简化为主将位置闪光;全屏后效在 M6 精修)
                    if (VfxDirector.Current != null)
                        Spawn(_breakPool, BoardView.WorldPos(new SimVec2(135f, 80f)),
                            new Color(1f, 0.85f, 0.3f), 8f);
                    break;
                case SimEventType.FortPulsed:
                    // T05 地堡脉冲:Q06 修正——真实贴地扩散环(盾池圆形几何,非碎点爆发);
                    // 敌方堡垒也正确显示(只要其建筑对玩家可见)
                    var fort = _sim.GetEntity(ev.SubjectId);
                    if (fort != null && fort.IsFort)
                        Spawn(_shieldPool, BoardView.WorldPos(fort.Pos) + new Vector3(0, 0.15f, 0),
                            FxBank.EarthGold, 3.0f);   // 扩散到脉冲半径 2m 的视觉范围
                    break;
            }
        }

        private void OnDamage(SimEvent ev)
        {
            if (VfxSourceRules.IsWallSelfDamage(ev)) return;          // 墙体拦截自伤:不产生命中表现
            var victim = _sim.GetEntity(ev.SubjectId);
            if (victim == null) return;
            bool victimVisible = VfxVisibility.ShowAttached(_sim, AppFlow.PlayerSide, victim);

            // 出招:有来源、攻击者可见、同 tick 同攻击者只一次(已知局限见 VfxSwingDedup)
            if (VfxSourceRules.HasSource(ev) && _swings.FirstOfSwing(ev.AttackerId, ev.Tick))
            {
                var attacker = _sim.GetEntity(ev.AttackerId);
                if (attacker != null && VfxVisibility.ShowAttached(_sim, AppFlow.PlayerSide, attacker))
                {
                    var emitter = _sim.GetEntity(VfxSourceRules.EmitterEntityId(_sim, ev.AttackerId));
                    if (emitter != null)
                    {
                        // M3 英雄差异化出招:镇岳宽重/断峰窄快/磐石标准/射手不画弧(走弹道)
                        float swingScale = 1f;
                        var hero = attacker.Hero;
                        if (attacker.IsSoldier && attacker.GeneralId != 0)
                        {
                            var gen = _sim.GetEntity(attacker.GeneralId);
                            if (gen != null && gen.Hero.HasValue) hero = gen.Hero;
                        }
                        if (hero == HeroId.ZhenYue) swingScale = 1.5f;        // 镇岳:宽重锤感
                        else if (hero == HeroId.DuanFeng) swingScale = 0.6f;   // 断峰:窄刺快
                        else if (hero == HeroId.ZhuFeng) swingScale = 0.8f;    // 逐风:短风带
                        if (hero != HeroId.ZhuiRi && hero != HeroId.XingLuo)    // 射手走弹道不画弧
                            Spawn(_swingPool, BoardView.WorldPos(From(emitter)),
                                FxBank.ThemeOf(attacker, _sim), swingScale);
                    }
                }
            }

            // 命中:受害者侧(来源不可见也允许——只展示目标被击,不画指向隐藏来源的线)
            // Q06 修复:不再限定 Unit —— 建筑被打(攻城锥/单位拆建筑)同样要有目标侧命中反馈
            if (victimVisible)
            {
                // B05(2026-09-18):建筑 HP 刚被打到 ≤0 → 即将废墟化 → 一次坍尘
                // (DamageDealt 在 ApplyBuildingDamage 的 HP 扣减**之后**发出,Rubbled 在 emit **之后**才置,
                //  此刻 Hp≤0 且 Rubbled=false = 建筑正在垮)
                if (victim.Kind == EntityKind.Building && victim.Hp <= 0f && !victim.Rubbled)
                    Spawn(_deathPool, HitPoint(victim), new Color(0.45f, 0.40f, 0.35f), 2.5f);
                else
                    Spawn(_hitPool, HitPoint(victim), FxBank.ThemeOf(_sim.GetEntity(ev.AttackerId), _sim),
                        victim.Kind == EntityKind.Unit ? (victim.IsSoldier ? 0.6f : 1f) : 1.3f);
            }
        }

        private void OnSettled(SimEvent ev)
        {
            var victim = _sim.GetEntity(ev.SubjectId);
            if (victim == null) return;
            if (!VfxVisibility.ShowAttached(_sim, AppFlow.PlayerSide, victim)) return;

            switch (VfxSourceRules.Classify(ev))
            {
                case VfxSourceRules.SettledKind.ShieldAbsorb:
                    Spawn(_shieldPool, HitPoint(victim), FxBank.ShieldTint, 1f);
                    break;
                case VfxSourceRules.SettledKind.ShieldBreak:
                    Spawn(_breakPool, HitPoint(victim), FxBank.ShieldTint, 1f);
                    break;
                case VfxSourceRules.SettledKind.Death:
                    // 死亡余留在视图拆除前取位置快照(M0 契约 1.5):优先视图实际位置
                    var view = BoardView.Active != null ? BoardView.Active.GetView(victim.Id) : null;
                    var pos = view != null ? view.transform.position : HitPoint(victim);
                    Spawn(_deathPool, pos, victim.IsSoldier ? FxBank.Steel * 0.7f : FxBank.Steel, 1f);
                    break;
                // Damage:命中已在 OnDamage 播过;此处不重复(职责分工)
            }
        }

        private static SimVec2 From(Entity e) => e.Kind == EntityKind.Building ? SimVec2.FromGrid(e.Pos) : e.PosF;

        private static Vector3 HitPoint(Entity victim) =>
            BoardView.WorldPos(victim.Kind == EntityKind.Building ? SimVec2.FromGrid(victim.Pos) : victim.PosF);

        // ================= 池化发射(命中不新建材质;MPB 着色) =================

        /// <summary>U03:升级成功金色闪光(StatusIconSystem 轮询 IsUpgraded 变化时调用)。</summary>
        public void SpawnUpgradeFlash(Vector3 worldPos)
        {
            Spawn(_breakPool, worldPos, new Color(1f, 0.85f, 0.3f), 1.2f);
        }

        private void Spawn(VfxPool pool, Vector3 worldPos, Color color, float scale)
        {
            var go = pool.Borrow(Root);
            if (go == null) return;   // 超容量:该族降级为不播(关键族优先保留在 M6 预算收口)
            go.transform.position = worldPos + new Vector3(0f, 0.6f, 0f);
            go.transform.localScale = Vector3.one * scale;
            FxBank.Tint(go, color);
            _active.Add(new ActiveFx { Go = go, Pool = pool, Session = SessionId });
        }

        /// <summary>在借效果句柄(Q02 修复:检查**全部子级** PS,非仅根 —— MakeShieldBreak/MakeDeath
        /// 的根无 PS 组件,旧实现 GetComponent 返回 null 立即归还,破盾/阵亡从未真正播放)。
        /// PS 数组借出时缓存一次,归还时清空引用。</summary>
        private class ActiveFx
        {
            public GameObject Go;
            public VfxPool Pool;
            public long Session;          // Q03:会话代号,拆除/换边后句柄即过期
            public ParticleSystem[] _cachedPs;   // 借出时缓存一次(Q02:避免每帧 GetComponentsInChildren)

            public bool Done(VfxDirector owner)
            {
                if (Go == null) return true;
                if (owner == null || owner.SessionId != Session) return true;
                if (_cachedPs == null)
                    _cachedPs = Go.GetComponentsInChildren<ParticleSystem>(true);
                if (_cachedPs.Length == 0) return true;
                for (int i = 0; i < _cachedPs.Length; i++)
                {
                    if (_cachedPs[i] == null) continue;
                    if (_cachedPs[i].IsAlive(true) || _cachedPs[i].isPlaying) return false;
                }
                return true;
            }

            public void Return()
            {
                _cachedPs = null;
                Go = null;
            }
        }

        private void OnDisable() => Dispose();
        private void OnDestroy() => Dispose();

        /// <summary>显式拆除(测试与换边链路直接调;运行时由 OnDisable/OnDestroy 兜底 ——
        /// EditMode 的 DestroyImmediate 不触发生命周期回调)。</summary>
        public void Dispose() => Teardown();

        /// <summary>拆除(Q03 修复:在借先归还再清池+显式拆根)。幂等:重复调用安全。
        /// 解订阅由 GameBootstrap 的 Sim.ClearEventListeners 统一覆盖。
        /// ReferenceEquals:Unity 重载 == 的坑见 M1 教训。</summary>
        private void Teardown()
        {
            // 在借对象:先归还池(而非直接销毁,保池复用)
            for (int i = 0; i < _active.Count; i++)
            {
                var fx = _active[i];
                if (fx.Go != null) fx.Pool?.Return(fx.Go);
                fx.Return();
            }
            _active.Clear();
            // 池整清
            _hitPool?.DisposeAll(); _swingPool?.DisposeAll(); _shieldPool?.DisposeAll();
            _breakPool?.DisposeAll(); _deathPool?.DisposeAll();
            // Q03:显式拆根(旧版只清列表+池,Root 挂在本组件下靠组件销毁兜底;EditMode 不触发回调)
            if (Root != null)
            {
                if (Application.isPlaying) Destroy(Root.gameObject);
                else DestroyImmediate(Root.gameObject);
                Root = null;
            }
            if (System.Object.ReferenceEquals(Current, this)) Current = null;
        }
    }
}
