using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    // View 层只做驱动与呈现,不持有任何模拟状态
    public sealed class SimBootstrapper : MonoBehaviour
    {
        public SimCore Sim { get; private set; }
        public bool Paused { get; set; }

        private double _accumulator;

        private void Awake()
        {
            Sim = new SimCore(new SimConfig(), new SeededRng(42));
            Debug.Log("SimCore ready");
        }

        private void Update()
        {
            if (Paused) return;
            // 累加器解耦渲染帧率,保证固定 10Hz 步进
            double interval = 1.0 / Sim.Config.TickRate;
            _accumulator += Time.deltaTime;
            while (_accumulator >= interval)
            {
                _accumulator -= interval;
                Sim.Step();
            }
        }

        private void OnDestroy()
        {
            Sim?.ClearEventListeners();
        }
    }
}
