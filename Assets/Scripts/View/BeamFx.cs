using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 2026-09-09 远程攻击弹道表现(制作人规格):
    /// 坠日=前端红后端黄渐变射线;星落=紫色射线;对应士兵=更细更短的紫/红射线。
    /// 纯表现,自生自灭,由 BoardView 在 DamageDealt(带 AttackerId)时生成。
    /// </summary>
    public sealed class BeamFx : MonoBehaviour
    {
        private LineRenderer _line;
        private float _t;
        private float _life = 0.14f;

        public static void Spawn(Transform parent, Vector3 from, Vector3 to, Color cFrom, Color cTo, float width, float reach)
        {
            var go = new GameObject("BeamFx");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<BeamFx>();
            fx.Build(from, to, cFrom, cTo, width, reach);
        }

        private void Build(Vector3 from, Vector3 to, Color cFrom, Color cTo, float width, float reach)
        {
            // reach<1:士兵弹道更短(不到目标即止)
            Vector3 end = Vector3.Lerp(from, to, reach);
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.SetPosition(0, from);
            _line.SetPosition(1, end);
            _line.startWidth = width;
            _line.endWidth = width * 0.55f;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(cFrom, 0f), new GradientColorKey(cTo, 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.15f, 1f) });
            _line.colorGradient = g;
            _line.material = ViewAssets.Mat(Color.black, 0f, 0.5f, cTo, 2.2f); // 自发光,无需光照
            _t = 0f;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            if (_t >= _life || _line == null) { Destroy(gameObject); return; }
            float a = 1f - _t / _life;
            _line.startColor = new Color(_line.startColor.r, _line.startColor.g, _line.startColor.b, a);
            _line.endColor = new Color(_line.endColor.r, _line.endColor.g, _line.endColor.b, a * 0.3f);
        }
    }
}
