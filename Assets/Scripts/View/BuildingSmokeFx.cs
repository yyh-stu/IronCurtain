using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 2026-09-10 制作人定稿·建筑分阶段烟雾:
    /// ≥75% 无烟;50%~75% 微微轻烟;25%~50% 偏浓黑烟;≤25% 浓黑烟。
    /// 纯表现,挂在建筑 EntityView 模型根下,每帧读 HP 比例调发射率与颜色。
    /// </summary>
    public sealed class BuildingSmokeFx : MonoBehaviour
    {
        private ParticleSystem _ps;
        private ParticleSystem.EmissionModule _em;
        private ParticleSystem.MainModule _main;
        private int _stage = -1; // 0=无 1=轻 2=偏浓 3=浓

        public static void Attach(GameObject modelRoot, float topY, float radius)
        {
            var go = new GameObject("SmokeFx");
            go.transform.SetParent(modelRoot.transform, false);
            go.transform.localPosition = new Vector3(0f, topY * 0.55f, 0f);
            var fx = go.AddComponent<BuildingSmokeFx>();
            fx.Build(radius);
        }

        private void Build(float radius)
        {
            _ps = gameObject.AddComponent<ParticleSystem>();
            _main = _ps.main;
            _main.loop = true;
            _main.playOnAwake = true;
            _main.duration = 5f;
            _main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            _main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            _main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.4f, radius * 0.7f);
            _main.gravityModifier = -0.02f; // 微上飘
            _main.maxParticles = 80;
            _main.simulationSpace = ParticleSystemSimulationSpace.Local;

            _em = _ps.emission;
            _em.rateOverTime = 0f;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius * 0.35f;

            var vel = _ps.velocityOverLifetime;
            vel.y = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.grey, 0f), new GradientColorKey(Color.black, 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0.25f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            // URP 粒子材质
            var mr = GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetFloat("_Surface", 1f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mr.material = mat;
        }

        /// <summary>hpFrac = 当前血量/满血;destroyed = Rubbled(摧毁余烟)。</summary>
        public void SetHpFraction(float hpFrac, bool destroyed = false)
        {
            if (destroyed)
            {
                // 2026-09-10 制作人定稿:摧毁保留余烟(由浓黑烟缓慢衰减至熄灭)
                if (_stage != 4)
                {
                    _stage = 4;
                    _destroyT = 0f;
                }
                return;
            }
            int stage = hpFrac >= 0.75f ? 0 : (hpFrac >= 0.50f ? 1 : (hpFrac >= 0.25f ? 2 : 3));
            if (stage == _stage) return;
            _stage = stage;
            switch (stage)
            {
                case 0:
                    _em.rateOverTime = 0f;
                    break;
                case 1: // 微微轻烟
                    _em.rateOverTime = 3f;
                    _main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
                    SetColor(new Color(0.65f, 0.65f, 0.65f), 0.22f);
                    break;
                case 2: // 偏浓黑烟
                    _em.rateOverTime = 8f;
                    _main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
                    SetColor(new Color(0.25f, 0.22f, 0.20f), 0.4f);
                    break;
                case 3: // 浓黑烟
                    _em.rateOverTime = 16f;
                    _main.startSize = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
                    SetColor(new Color(0.08f, 0.07f, 0.06f), 0.6f);
                    break;
            }
        }

        private float _destroyT;

        private void Update()
        {
            if (_stage != 4) return;
            // 余烟:浓黑烟起,8 秒线性衰减到熄
            _destroyT += Time.deltaTime;
            float k = Mathf.Clamp01(1f - _destroyT / 8f);
            _em.rateOverTime = 16f * k;
            _main.startSize = new ParticleSystem.MinMaxCurve(1.0f * k + 0.2f, 1.8f * k + 0.3f);
            if (k <= 0f) { _stage = 0; _em.rateOverTime = 0f; }
        }

        private void SetColor(Color c, float alpha)
        {
            var col = _ps.colorOverLifetime;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c * 0.6f, 1f) },
                new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha * 0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
        }
    }
}
