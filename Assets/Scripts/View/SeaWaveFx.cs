using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>海面轻微海浪(2026-09-12 制作人:地图外改成海、要有轻微海浪):
    /// 两层大水面贴图错速滚动,不用粒子也不动网格 —— 每帧两次 SetTextureOffset,零渲染开销。</summary>
    public sealed class SeaWaveFx : MonoBehaviour
    {
        public Material Far;
        public Material Near;
        private float _t;
        private static readonly int[] TexIds = { Shader.PropertyToID("_BaseMap"), Shader.PropertyToID("_MainTex") };

        private void Update()
        {
            _t += Time.deltaTime;
            // 远海慢、近海快且反向 → 叠出细微的浪纹流动感(幅度刻意很小,不喧宾夺主)
            Apply(Far, new Vector2(_t * 0.010f, _t * 0.007f));
            Apply(Near, new Vector2(-_t * 0.017f, _t * 0.012f));
        }

        private static void Apply(Material m, Vector2 off)
        {
            if (m == null) return;
            for (int i = 0; i < TexIds.Length; i++) m.SetTextureOffset(TexIds[i], off);
        }
    }
}
