using System.Collections.Generic;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 2026-09-09 远程攻击弹道表现(制作人规格):
    /// 坠日=前端红后端黄渐变射线;星落=紫色射线;对应士兵=更细更短的紫/红射线。
    /// 纯表现,自生自灭,由 BoardView 在 DamageDealt(带 AttackerId)时生成。
    ///
    /// 2026-09-13 制作人:"坠日和星落的弹道廉价感也很重"。原实现是一根**一次性直线**,
    /// 三处廉价源:①材质是不透明自发光 → 顶点 alpha 被丢弃,所谓淡出是空写,观感是"啪一下消失";
    /// ②整根瞬现,没有出膛/飞行读感,像一条 debug 射线;③每次开火新建一个 Material 且从不销毁(泄漏)。
    /// 现改为:出膛 → 线段自原点**生长**到目标 → 满长驻留一瞬 → 整体真淡出;材质走 UnlitFade 并按颜色缓存复用。
    /// </summary>
    public sealed class BeamFx : MonoBehaviour
    {
        private const float GrowTime = 0.10f;   // 自原点生长到目标(读作"打出去")
        private const float HoldTime = 0.05f;   // 满长驻留(命中读感)
        private const float FadeTime = 0.14f;   // 整体淡出
        private const float Life = GrowTime + HoldTime + FadeTime;

        private static readonly Dictionary<Color, Material> MatCache = new Dictionary<Color, Material>();

        private LineRenderer _line;
        private Vector3 _from, _end;
        private Color _cFrom, _cTo;
        private float _width, _t;

        public static void Spawn(Transform parent, Vector3 from, Vector3 to, Color cFrom, Color cTo, float width, float reach)
        {
            var go = new GameObject("BeamFx");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<BeamFx>();
            fx.Build(from, to, cFrom, cTo, width, reach);
        }

        private static Material MatFor(Color c)
        {
            Material m;
            if (MatCache.TryGetValue(c, out m) && m != null) return m;
            // brightness>1 让它越过 URP 资产的 bloom 阈值(1.0)吃到泛光
            m = ViewAssets.UnlitFade(new Color(c.r, c.g, c.b, 1f), 1.9f);
            MatCache[c] = m;
            return m;
        }

        private void Build(Vector3 from, Vector3 to, Color cFrom, Color cTo, float width, float reach)
        {
            _from = from; _cFrom = cFrom; _cTo = cTo; _width = width;
            // reach<1:士兵弹道更短(不到目标即止)
            _end = Vector3.Lerp(from, to, reach);
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.numCapVertices = 2;
            _line.material = MatFor(cTo);
            _t = 0f;
            Draw(0f);
        }

        /// <summary>head∈[0,1]:线段自 _from 生长到 tip;宽度随生长变粗,alpha 走相位。</summary>
        private void Draw(float head)
        {
            float k = Mathf.Clamp01(head);
            float a = _t < GrowTime + HoldTime
                ? 1f
                : Mathf.Clamp01(1f - (_t - GrowTime - HoldTime) / FadeTime);
            _line.SetPosition(0, _from);
            _line.SetPosition(1, Vector3.Lerp(_from, _end, k));
            _line.startWidth = _width * (0.55f + 0.45f * k);
            _line.endWidth = _width * (0.30f + 0.25f * k);
            _line.startColor = new Color(_cFrom.r, _cFrom.g, _cFrom.b, a);
            _line.endColor = new Color(_cTo.r, _cTo.g, _cTo.b, a * 0.45f);
        }

        private void Update()
        {
            if (_line == null) { Destroy(gameObject); return; }
            _t += Time.deltaTime;
            if (_t >= Life) { Destroy(gameObject); return; }
            if (_t < GrowTime)
            {
                float k = _t / GrowTime;               // smoothstep:出膛快、末段收
                Draw(k * k * (3f - 2f * k));
            }
            else Draw(1f);
        }
    }
}
