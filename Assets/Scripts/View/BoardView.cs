using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public sealed class BoardView : MonoBehaviour
    {
        private SimCore _sim;
        private readonly Dictionary<int, EntityView> _views = new Dictionary<int, EntityView>();

        public Side PlayerSide = Side.Blue;

        /// <summary>当前对局实例(2026-09-17 M2:VFX 死亡快照要取视图实际位置;拆除置空,ReferenceEquals 守卫)。</summary>
        public static BoardView Active { get; private set; }

        public static Vector3 WorldPos(SimVec2 p) { return new Vector3(p.X, 0f, 3f - p.Y); }
        public static Vector3 WorldPos(GridPos p) { return new Vector3(p.Col, 0f, 3f - p.Row); }

        public void Init(SimCore sim, Transform parent)
        {
            _sim = sim;
            Active = this;
            EntityView.SimRef = sim;   // F-A03:EntityView 查攻击者位置用
            BuildBoard();
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e != null) CreateView(e);
            }
        }

        public EntityView GetView(int entityId)
        {
            EntityView v;
            return _views.TryGetValue(entityId, out v) ? v : null;
        }

        public void OnSimEvent(SimEvent ev)
        {
            switch (ev.Type)
            {
                case SimEventType.EntitySpawned:
                    var e = _sim.GetEntity(ev.SubjectId);
                    if (e != null)
                    {
                        var v = CreateView(e); // CreateView 返回类型改为 EntityView(现状已建视图后存 _views,改成 return 即可)
                        if (e.IsSoldier && e.MarchOrigin != null && v != null)
                            v.gameObject.AddComponent<ReinforceMarchView>().Init(v, e, WorldPos(e.MarchOrigin.Value));
                    }
                    break;
                case SimEventType.EntityRemoved:
                    EntityView rv;
                    if (_views.TryGetValue(ev.SubjectId, out rv))
                    {
                        _views.Remove(ev.SubjectId);
                        if (rv != null && rv.gameObject != null) Destroy(rv.gameObject);
                    }
                    break;
                case SimEventType.DamageDealt:
                    var dv = GetView(ev.SubjectId);
                    if (dv != null) dv.FlashHit();
                    SpawnBeamIfRanged(ev);
                    break;
                case SimEventType.EntityDied:
                    var dv2 = GetView(ev.SubjectId);
                    if (dv2 != null) dv2.SetDead();
                    break;
                // 2026-09-11 屏障特效:受击涟漪/击碎动画(纯表现,自生自灭)
                case SimEventType.WallDamaged:
                case SimEventType.WallBroken:
                    WallFx.OnEvent(transform, ev);
                    break;
            }
        }

        public void TickViews()
        {
            Camera cam = Camera.main;
            float dt = Time.deltaTime;
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (v == null) continue;
                var e = _sim.GetEntity(kv.Key);
                if (e == null || e.State == EntityState.Dead) v.SetDead();
                if (e != null) ApplyFog(v, e);
                v.Tick(dt, cam);
            }
        }

        private void ApplyFog(EntityView v, Entity e)
        {
            if (e.Kind == EntityKind.Building)
            {
                if (e.Side == PlayerSide) { v.SetFogBuilding(false, false); v.SetFogGarrison(null); return; }
                var st = FogRules.BuildingState(_sim, PlayerSide, e);
                v.SetFogBuilding(st == BuildingFogState.Unknown, st == BuildingFogState.Stale);
                v.SetFogGarrison(st == BuildingFogState.Stale
                    ? VisionSystem.LastKnownGarrison(_sim, PlayerSide, e.Id) : null);
            }
            else if (e.Kind == EntityKind.Unit)
                v.SetFogUnitHidden(e.Side != PlayerSide && !FogRules.UnitVisible(_sim, PlayerSide, e));
        }

        /// <summary>2026-09-09 远程弹道:坠日(红→黄渐变)/星落(紫)主将射线;对应士兵更细更短的紫/红射线。
        /// 2026-09-18 Q05:弹道按迷雾分段裁剪(起点 Sim 坐标→终点 Sim 坐标采样)。</summary>
        private void SpawnBeamIfRanged(SimEvent ev)
        {
            if (ev.AttackerId == 0) return;
            var atk = _sim.GetEntity(ev.AttackerId);
            var def = _sim.GetEntity(ev.SubjectId);
            if (atk == null || def == null) return;
            var av = GetView(ev.AttackerId);
            if (av == null) return;
            Vector3 to = def.Kind == EntityKind.Building ? WorldPos(def.Pos) : WorldPos(def.PosF);

            // Q05:迷雾弹道裁剪——可见比例作为 reach(0=整条不画,1=全程可见)
            SimVec2 fromSim = atk.Kind == EntityKind.Building ? SimVec2.FromGrid(atk.Pos) : atk.PosF;
            SimVec2 toSim = def.Kind == EntityKind.Building ? SimVec2.FromGrid(def.Pos) : def.PosF;
            float fogReach = VfxVisibility.BeamVisibleFraction(_sim, PlayerSide, fromSim, toSim);
            if (fogReach <= 0.01f) return;   // 整条不可见:不画(不暴露隐藏发射源)

            // 2026-09-13 箭塔弹道。塔走自己的配色(琥珀),与坠日/星落区分开,起点取塔模型顶面。
            if (atk.Kind == EntityKind.Building)
            {
                if (atk.Building != BuildingId.Tower) return;
                Vector3 fromTower = av.transform.position + new Vector3(0f, Mathf.Max(2.0f, av.RoofY), 0f);
                BeamFx.Spawn(transform, fromTower, to, new Color(1f, 0.88f, 0.50f), new Color(1f, 0.60f, 0.16f), 0.16f, fogReach);
                return;
            }
            if (atk.Kind != EntityKind.Unit) return;

            HeroId hero = atk.Hero ?? HeroId.PanShi;
            if (atk.IsSoldier && atk.GeneralId != 0)
            {
                var gen = _sim.GetEntity(atk.GeneralId);
                if (gen != null && gen.Hero.HasValue) hero = gen.Hero.Value;
            }
            bool isZhuiRi = hero == HeroId.ZhuiRi, isXingLuo = hero == HeroId.XingLuo;
            if (!isZhuiRi && !isXingLuo) return; // 近战无弹道
            // B02(2026-09-18):驻军射手 → 弹道起点挂**建筑**(冷却/伤害归属仍在主将)
            Vector3 from;
            if (atk.GarrisonBuildingId != 0)
            {
                var bldg = _sim.GetEntity(atk.GarrisonBuildingId);
                var bv = bldg != null ? GetView(bldg.Id) : null;
                from = bv != null
                    ? bv.transform.position + new Vector3(0f, 2.5f, 0f)   // 建筑射击口高度
                    : av.transform.position + new Vector3(0f, atk.IsSoldier ? 1.0f : 1.9f, 0f);
            }
            else
                from = av.transform.position + new Vector3(0f, atk.IsSoldier ? 1.0f : 1.9f, 0f);
            if (atk.IsSoldier)
            {
                // 士兵:更细但全到达(2026-09-17 制作人:"弹道没打在对面身上";旧 reach=0.72 差 28%)
                // 2026-09-18 Q05:reach 取迷雾可见比例(不再硬编码 1)
                if (isZhuiRi) BeamFx.Spawn(transform, from, to, new Color(1f, 0.3f, 0.15f), new Color(1f, 0.6f, 0.2f), 0.05f, fogReach);
                else BeamFx.Spawn(transform, from, to, new Color(0.7f, 0.45f, 1f), new Color(0.55f, 0.3f, 0.95f), 0.05f, fogReach);
            }
            else
            {
                if (isZhuiRi) BeamFx.Spawn(transform, from, to, new Color(1f, 0.28f, 0.12f), new Color(1f, 0.85f, 0.2f), 0.14f, fogReach);
                else BeamFx.Spawn(transform, from, to, new Color(0.72f, 0.4f, 1f), new Color(0.5f, 0.25f, 0.95f), 0.12f, fogReach);
            }
        }

        private EntityView CreateView(Entity e)
        {
            if (_views.ContainsKey(e.Id)) return null;
            var ent = e;
            // 士兵模型随主将兵种:主将阵亡(GeneralId 失效)时回退默认风格
            HeroId? heroFor = e.Hero;
            if (e.IsSoldier && e.GeneralId != 0)
            {
                var gen = _sim.GetEntity(e.GeneralId);
                if (gen != null && gen.Hero.HasValue) heroFor = gen.Hero;
            }
            var go = new GameObject("Entity_" + e.Id);
            go.transform.SetParent(transform, false);
            var view = go.AddComponent<EntityView>();
            view.Bind(e, e.Side, heroFor, e.Building, e.Kind == EntityKind.Unit && !e.IsSoldier ? (System.Func<int>)(() =>
            {
                int c = 0;
                foreach (var x in _sim.Entities.Values)
                    if (x.IsSoldier && x.GeneralId == ent.Id && x.State != EntityState.Dead) c++;
                return c;
            }) : null);
            _views[e.Id] = view;
            return view;
        }

        private void BuildBoard()
        {
            TerrainView.Build(transform);
        }

        private void OnDestroy()
        {
            // 静态口摘除(ReferenceEquals 守卫,同 VfxDirector 的 M1 教训)
            if (System.Object.ReferenceEquals(Active, this)) Active = null;
        }
    }
}
