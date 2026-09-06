using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// M8.6b 补兵行军表现:士兵从生产点(兵营/主基)走至兵团入队。
    /// 纯表现,demo 不可截击;Sim 侧士兵已出生在兵团槽位,本组件只接管 View 位置。
    /// </summary>
    public sealed class ReinforceMarchView : MonoBehaviour
    {
        private EntityView _view;
        private Entity _e;
        private Vector3 _origin;

        public void Init(EntityView view, Entity e, Vector3 originWorld)
        {
            _view = view;
            _e = e;
            _origin = originWorld;
            view.MarchHold = true;
            transform.position = originWorld;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_e == null || _e.State == EntityState.Dead)
            {
                End();
                return;
            }
            // 驻军:交回 EntityView(驻军隐藏由其原逻辑处理)
            if (_e.GarrisonBuildingId != 0)
            {
                End();
                return;
            }
            Vector3 target = BoardView.WorldPos(_e.PosF);
            Vector3 delta = target - transform.position;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist < 0.5f)
            {
                End();
                return;
            }
            float speed = Mathf.Max(0.1f, _e.MoveSpeed);
            Vector3 dir = delta / dist;
            Vector3 step = dir * Mathf.Min(speed * dt, dist);
            transform.position += step;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        private void End()
        {
            if (_view != null) _view.MarchHold = false;
            Destroy(this);
        }
    }
}
