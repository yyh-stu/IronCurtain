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

        public static Vector3 WorldPos(SimVec2 p) { return new Vector3(p.X, 0f, 3f - p.Y); }
        public static Vector3 WorldPos(GridPos p) { return new Vector3(p.Col, 0f, 3f - p.Row); }

        public void Init(SimCore sim, Transform parent)
        {
            _sim = sim;
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

        /// <summary>2026-09-09 远程弹道:坠日(红→黄渐变)/星落(紫)主将射线;对应士兵更细更短的紫/红射线。</summary>
        private void SpawnBeamIfRanged(SimEvent ev)
        {
            if (ev.AttackerId == 0) return;
            var atk = _sim.GetEntity(ev.AttackerId);
            var def = _sim.GetEntity(ev.SubjectId);
            if (atk == null || def == null || atk.Kind != EntityKind.Unit) return;
            HeroId hero = atk.Hero ?? HeroId.PanShi;
            if (atk.IsSoldier && atk.GeneralId != 0)
            {
                var gen = _sim.GetEntity(atk.GeneralId);
                if (gen != null && gen.Hero.HasValue) hero = gen.Hero.Value;
            }
            bool isZhuiRi = hero == HeroId.ZhuiRi, isXingLuo = hero == HeroId.XingLuo;
            if (!isZhuiRi && !isXingLuo) return; // 近战无弹道
            var av = GetView(ev.AttackerId);
            if (av == null) return;
            Vector3 from = av.transform.position + new Vector3(0f, atk.IsSoldier ? 1.0f : 1.9f, 0f);
            Vector3 to = def.Kind == EntityKind.Building ? WorldPos(def.Pos) : WorldPos(def.PosF);
            if (atk.IsSoldier)
            {
                // 士兵:更细更短
                if (isZhuiRi) BeamFx.Spawn(transform, from, to, new Color(1f, 0.3f, 0.15f), new Color(1f, 0.6f, 0.2f), 0.05f, 0.72f);
                else BeamFx.Spawn(transform, from, to, new Color(0.7f, 0.45f, 1f), new Color(0.55f, 0.3f, 0.95f), 0.05f, 0.72f);
            }
            else
            {
                if (isZhuiRi) BeamFx.Spawn(transform, from, to, new Color(1f, 0.28f, 0.12f), new Color(1f, 0.85f, 0.2f), 0.14f, 1f);
                else BeamFx.Spawn(transform, from, to, new Color(0.72f, 0.4f, 1f), new Color(0.5f, 0.25f, 0.95f), 0.12f, 1f);
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
    }
}
