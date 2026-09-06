using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public sealed class BoardView : MonoBehaviour
    {
        private SimCore _sim;
        private readonly Dictionary<int, EntityView> _views = new Dictionary<int, EntityView>();

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
                    break;
                case SimEventType.EntityDied:
                    var dv2 = GetView(ev.SubjectId);
                    if (dv2 != null) dv2.SetDead();
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
                v.Tick(dt, cam);
            }
        }

        private EntityView CreateView(Entity e)
        {
            if (_views.ContainsKey(e.Id)) return null;
            var ent = e;
            var go = new GameObject("Entity_" + e.Id);
            go.transform.SetParent(transform, false);
            var view = go.AddComponent<EntityView>();
            view.Bind(e, e.Side, e.Hero, e.Building, e.Kind == EntityKind.Unit && !e.IsSoldier ? (System.Func<int>)(() =>
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
