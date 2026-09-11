using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 屏障特效(2026-09-11 制作人拍板"堆特效";2026-09-11 深夜·专门重做)。
    /// 制作人口径:'_barrier_bowl 加受击涟漪、粒子飘散' —— 幕面走渐隐膜(见 WallView),
    /// 视觉重量全部放在**受击反馈**上:
    /// - **受击涟漪**:命中点吸附碗沿曲面,沿该点法线取向的扩散环,~0.4s;
    /// - **命中核心闪光**:命中点一个短促亮斑(0.12s 爆闪即收)——打击感主要来自这一层;
    /// - **粒子飘散**:命中点向外上方喷 6~12 颗光点,沿幕面飘散淡出(1.2~2.2s);
    /// - **击碎动画**(制作人:保留,能用):隐藏该段 + 六边碎片沿弧飞散 + 地面光爆。
    /// 工程要点(本轮专门处理):
    /// ① 环/闪光用**对象池**(原先每次命中 new GameObject+Destroy,反复产生 GC 与材质实例);
    /// ② **同段 0.12s 节流**:多个单位同时拆墙时不至于糊成一团(实战里这条比什么都重要);
    /// ③ 环几何不靠 transform 缩放(线宽会跟着变粗),逐帧重建半径、线宽固定;
    /// ④ 全部 Alpha 混合而非加色:加色在白色石路广场上会被推向过曝隐形(已实测两轮)。
    /// edit 模式(批处理截图)由 CaptureBatch 调 RenderFrameAll 手动推进。
    /// </summary>
    public sealed class WallFx : MonoBehaviour
    {
        // ---- 参数 ----
        const float RippleLife = 0.40f;
        const float RippleMaxR = 3.4f;
        const float RippleWidth = 0.30f;
        const float RippleHeight = 2.4f;     // 涟漪高度(渐隐膜下段可读区;随幕高 7m 下调)
        const float FlashLife = 0.12f;       // 命中核心闪光
        const float FlashMaxR = 1.35f;
        const float ShockLife = 0.55f;
        const float ShockMaxR = 6.5f;
        const int FragCount = 46;            // 六边碎片数/段
        const int DriftPerHit = 9;           // 每次命中喷散光点数
        const float ThrottleRing = 0.12f;    // 同段涟漪节流(秒)
        const float ThrottleDrift = 0.06f;   // 同段喷散节流(秒)

        /// <summary>常驻氛围尘 发射/秒/方(制作人:先只做受击喷发 → 默认 0;性能近乎免费,可随时开)</summary>
        public static float AmbientRate = 0f;

        private static readonly List<WallFx> _rings = new List<WallFx>();
        private static readonly Stack<WallFx> _ringPool = new Stack<WallFx>();
        private static readonly List<ParticleSystem> _particles = new List<ParticleSystem>();
        private static readonly float[] _lastRing = new float[16];
        private static readonly float[] _lastDrift = new float[16];
        private static float _clock;

        private LineRenderer _ring;
        private float _t, _life, _maxR, _r0, _width;
        private Color _col;

        // ================= 事件入口(BoardView 路由) =================

        public static void OnEvent(Transform boardRoot, SimEvent ev)
        {
            var owner = (Side)ev.WallSide;
            if (ev.Type == SimEventType.WallDamaged)
            {
                int key = (int)owner * 8 + ev.SubjectId;
                // 命中点吸附到该段曲面(段内角度比例由命中方位角算出)
                float t = PanelT(owner, ev.SubjectId, ev.HitPos);
                Vector3 p = WallView.SurfacePoint(owner, ev.SubjectId, t, RippleHeight);
                Vector3 n = WallView.SurfaceNormal(owner, ev.SubjectId, t, RippleHeight);
                Color c = FxColor(owner);

                if (_clock - _lastRing[key] >= ThrottleRing)     // 节流:同段密集命中只留一发
                {
                    _lastRing[key] = _clock;
                    SpawnRing(boardRoot, p + n * 0.10f, n, c, RippleLife, RippleMaxR, RippleWidth);
                    WallFlash.Spawn(boardRoot, p + n * 0.18f, n, Color.Lerp(c, Color.white, 0.55f), FlashLife, FlashMaxR);
                }
                if (_clock - _lastDrift[key] >= ThrottleDrift)
                {
                    _lastDrift[key] = _clock;
                    EmitDrift(owner, p + n * 0.2f, n, DriftPerHit);
                }
            }
            else if (ev.Type == SimEventType.WallBroken)
            {
                WallView.HidePanel(owner, ev.SubjectId);          // 取巧:立即隐藏该段,粒子接手视觉
                var c = WallSystem.PanelCenterPos(owner, ev.SubjectId);
                var ground = BoardView.WorldPos(c);
                SpawnRing(boardRoot, ground, Vector3.up, FxColor(owner), ShockLife, ShockMaxR, 0.26f);
                SpawnFragments(boardRoot, owner, ev.SubjectId, c);
                EmitDrift(owner, ground + Vector3.up * 1.2f, Vector3.up, DriftPerHit * 2);
            }
        }

        /// <summary>阵营特效色:冰蓝/赤红,峰值通道压低避免加色叠到近白(实测过)。</summary>
        public static Color FxColor(Side side)
        {
            return side == Side.Blue
                ? new Color(0.22f, 0.68f, 1.00f)
                : new Color(0.96f, 0.21f, 0.17f);
        }

        /// <summary>命中点在段内的角度比例 t∈[0,1]。</summary>
        private static float PanelT(Side side, int panel, SimVec2 p)
        {
            var c = WallSystem.BaseCenter(side);
            float a = (float)System.Math.Atan2(p.Y - c.Y, p.X - c.X);
            float a0 = WallSystem.PanelAngle(side, panel) - WallSystem.PanelHalf(side, panel);
            float a1 = WallSystem.PanelAngle(side, panel) + WallSystem.PanelHalf(side, panel);
            return Mathf.Clamp01((a - a0) / Mathf.Max(0.0001f, a1 - a0));
        }

        // ================= 扩散环(对象池) =================

        private static void SpawnRing(Transform parent, Vector3 pos, Vector3 normal, Color col,
            float life, float maxR, float width)
        {
            WallFx fx;
            if (_ringPool.Count > 0)
            {
                fx = _ringPool.Pop();
                fx.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("WallRing");
                go.transform.SetParent(parent, false);
                fx = go.AddComponent<WallFx>();
                fx._ring = go.AddComponent<LineRenderer>();
                fx._ring.useWorldSpace = false;
                fx._ring.loop = true;
                fx._ring.positionCount = 40;
                fx._ring.numCapVertices = 2;
                fx._ring.material = AlphaUnlit(Color.white);   // 染色走顶点色
            }
            _rings.Add(fx);
            fx.transform.position = pos;
            fx.transform.rotation = Quaternion.LookRotation(normal.normalized, Vector3.up);
            fx._col = col;
            fx._ring.widthMultiplier = width;
            fx._t = 0f; fx._life = life; fx._maxR = maxR;
            fx._r0 = maxR > 5f ? 0.6f : 0.35f;   // 光爆环起点略大
            fx._width = width;
            fx.RebuildRing(fx._r0);
            fx.Advance(0f);
        }

        /// <summary>按当前半径重建环顶点(顶点在局部 XY 平面)。不用 transform 缩放:
        /// 线宽会随缩放一起变粗,环越扩越肥(2026-09-11 实测)。</summary>
        private void RebuildRing(float r)
        {
            if (_ring == null) return;
            for (int i = 0; i < _ring.positionCount; i++)
            {
                float a = i / (float)_ring.positionCount * Mathf.PI * 2f;
                _ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        // ================= 六边碎片(击碎) =================

        /// <summary>该段碎成六边碎片:沿整段弧、沿**可见高度**分散,向外抛飞+重力落地。</summary>
        private static void SpawnFragments(Transform parent, Side side, int panel, SimVec2 panelCenter)
        {
            var go = new GameObject("WallShards");
            go.transform.SetParent(parent, false);
            go.transform.position = BoardView.WorldPos(panelCenter);

            var ps = BuildBaseSystem(go);
            var main = ps.main;
            main.duration = 1.2f; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.7f);
            main.startSpeed = 0f;            // 速度由 EmitParams 逐颗给定
            main.startSize = new ParticleSystem.MinMaxCurve(0.30f, 0.62f);
            main.gravityModifier = 0.55f;
            main.startColor = Color.white;
            main.stopAction = ParticleSystemStopAction.Destroy;   // Play 模式播完自灭

            // 亮度:起始白闪 → 阵营色 → 淡出
            var co = ps.colorOverLifetime;
            co.enabled = true;
            var g = new Gradient();
            Color c = FxColor(side);
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 0.22f), new GradientColorKey(c * 0.7f, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f),
                        new GradientAlphaKey(0.95f, 0.55f), new GradientAlphaKey(0f, 1f) });
            co.color = new ParticleSystem.MinMaxGradient(g);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.6f, 0.85f), new Keyframe(1f, 0.25f)));

            var ren = go.GetComponent<ParticleSystemRenderer>();
            ren.material = AlphaParticles(HexSprite());
            ren.sortingOrder = 1;

            // 沿弧 + 沿可见高度分散(膜面上段已渐隐,碎片从那里出来会悬空)
            var rnd = new System.Random(1000 + (int)side * 10 + panel);
            var ep = new ParticleSystem.EmitParams();
            var baseC = WallSystem.BaseCenter(side);
            float a0 = WallSystem.PanelAngle(side, panel) - WallSystem.PanelHalf(side, panel);
            float a1 = WallSystem.PanelAngle(side, panel) + WallSystem.PanelHalf(side, panel);
            float maxH = WallView.HeightMeters * 0.6f;
            ps.Play();
            for (int i = 0; i < FragCount; i++)
            {
                float t = FragCount > 1 ? i / (float)(FragCount - 1) : 0.5f;
                float a = Mathf.Lerp(a0, a1, t);
                float hgt = 0.4f + (float)rnd.NextDouble() * (maxH - 0.4f);
                float rad = WallView.SurfaceRadiusAt(hgt);
                var pos = new Vector3(baseC.X + Mathf.Cos(a) * rad, hgt, (3f - baseC.Y) - Mathf.Sin(a) * rad);
                var radial = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                var tangent = new Vector3(-Mathf.Sin(a), 0f, -Mathf.Cos(a));
                ep.position = pos;
                ep.velocity = radial * (1.6f + (float)rnd.NextDouble() * 2.6f)
                    + Vector3.up * (2.2f + (float)rnd.NextDouble() * 2.8f)
                    + tangent * (((float)rnd.NextDouble() - 0.5f) * 1.8f);
                ep.startSize = 0.30f + (float)rnd.NextDouble() * 0.32f;
                ep.startLifetime = 0.9f + (float)rnd.NextDouble() * 0.8f;
                ep.rotation = (float)rnd.NextDouble() * 6.28f;
                ep.startColor = Color.white;
                ps.Emit(ep, 1);
            }
            _particles.Add(ps);
        }

        // ================= 受击粒子飘散 =================

        /// <summary>在命中点喷 n 颗光点:沿幕面法线向外 + 向上缓漂,尺寸微增、两端淡出。</summary>
        public static void EmitDrift(Side side, Vector3 pos, Vector3 normal, int count)
        {
            var ps = DriftSystem(side);
            if (ps == null) return;
            var rnd = new System.Random(Mathf.RoundToInt(_clock * 1000f) ^ (count * 7919));
            var ep = new ParticleSystem.EmitParams();
            Vector3 nrm = normal.normalized;
            Vector3 tan = Vector3.Cross(nrm, Vector3.up).normalized;
            for (int i = 0; i < count; i++)
            {
                float sp = 0.6f + (float)rnd.NextDouble() * 1.4f;
                float up = 0.5f + (float)rnd.NextDouble() * 1.1f;
                float sideDrift = ((float)rnd.NextDouble() - 0.5f) * 1.2f;
                ep.position = pos + tan * (((float)rnd.NextDouble() - 0.5f) * 1.6f)
                    + Vector3.up * ((float)rnd.NextDouble() * 0.8f);
                ep.velocity = nrm * sp + Vector3.up * up + tan * sideDrift;
                ep.startSize = 0.34f + (float)rnd.NextDouble() * 0.46f;
                ep.startLifetime = 1.2f + (float)rnd.NextDouble() * 1.0f;
                ep.startColor = Color.white;
                ps.Emit(ep, 1);
            }
        }

        /// <summary>每方一条飘散系统(世界空间、发射默认关、只手工 Emit);首次访问时建。</summary>
        private static readonly ParticleSystem[] _drift = new ParticleSystem[2];
        private static ParticleSystem DriftSystem(Side side)
        {
            int i = (int)side;
            if (i < 0 || i > 1) return null;
            if (_drift[i] != null) return _drift[i];
            var go = new GameObject("WallDrift_" + side);
            go.transform.position = BoardView.WorldPos(WallSystem.BaseCenter(side));
            var ps = BuildBaseSystem(go);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.34f, 0.80f);
            // 饱和阵营色(不是白):白点在白色石路广场上等于看不见(收尾实测)
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(FxColor(side).r, FxColor(side).g, FxColor(side).b, 0.92f),
                new Color(Mathf.Clamp01(FxColor(side).r * 0.6f + 0.4f),
                          Mathf.Clamp01(FxColor(side).g * 0.6f + 0.4f),
                          Mathf.Clamp01(FxColor(side).b * 0.6f + 0.4f), 0.98f));
            main.maxParticles = 240;
            main.gravityModifier = -0.02f;   // 极轻上浮
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);   // 三轴同曲线模式(踩过坑)
            vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.30f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            var co = ps.colorOverLifetime;
            co.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f),
                        new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            co.color = new ParticleSystem.MinMaxGradient(g);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f)));     // 飘散中微微胀大
            var ren = go.GetComponent<ParticleSystemRenderer>();
            ren.material = AlphaParticles(SoftDotSprite());
            ren.sortingOrder = 2;
            _drift[i] = ps;
            _particles.Add(ps);
            ps.Play();
            return ps;
        }

        /// <summary>把飘散系统挂到棋盘下(保持世界坐标);同时按 AmbientRate 决定常驻发射。</summary>
        public static void SpawnAmbient(Transform boardRoot, Side side)
        {
            var ps = DriftSystem(side);
            if (ps == null) return;
            ps.gameObject.transform.SetParent(boardRoot, true);
            SetAmbientRate(AmbientRate);
        }

        /// <summary>调常驻氛围速率(0=关)。性能实测近乎免费,留作审美档位。</summary>
        public static void SetAmbientRate(float rate)
        {
            AmbientRate = rate;
            for (int i = 0; i < 2; i++)
            {
                var ps = _drift[i];
                if (ps == null) continue;
                if (rate <= 0f)
                {
                    var em0 = ps.emission; em0.enabled = false;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play();                     // 继续"播放"以便受击 Emit 仍生效
                }
                else
                {
                    var em = ps.emission; em.enabled = true; em.rateOverTime = rate;
                    ps.Play();
                }
            }
        }

        // ================= 系统与生命周期 =================

        /// <summary>基础粒子系统:发射关闭、形状关闭(位置一律由 EmitParams 给)。</summary>
        private static ParticleSystem BuildBaseSystem(GameObject go)
        {
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            return ps;
        }

        private void Update() { Advance(Time.deltaTime); }

        /// <summary>edit 模式(批处理截图)手动推进所有特效+粒子。</summary>
        public static void RenderFrameAll(float dt)
        {
            _clock += dt;
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                var fx = _rings[i];
                if (fx == null) { _rings.RemoveAt(i); continue; }
                fx.Advance(dt);
            }
            WallFlash.AdvanceAll(dt);
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var ps = _particles[i];
                if (ps == null) { _particles.RemoveAt(i); continue; }
                // 一次性碎片系统播完即清(edit 模式无 stopAction 自毁,这里补)
                if (!ps.loop && !ps.IsAlive() && ps.particleCount == 0)
                {
                    _particles.RemoveAt(i);
                    if (!Application.isPlaying) DestroyImmediate(ps.gameObject);
                    continue;
                }
                ps.Simulate(dt, false, false);
            }
        }

        private void Advance(float dt)
        {
            if (_ring == null) { Retire(); return; }
            _t += dt;
            float k = _t / _life;
            if (k >= 1f) { Retire(); return; }
            float r = Mathf.Lerp(_r0, _maxR, 1f - (1f - k) * (1f - k));   // 先快后慢
            RebuildRing(r);
            float a = 1f - k * 0.78f;                                     // 中段保持亮度
            _ring.startColor = new Color(_col.r, _col.g, _col.b, a);
            _ring.endColor = new Color(_col.r, _col.g, _col.b, a * 0.7f);
            _ring.widthMultiplier = _width * (0.35f + 0.65f * (1f - k));
        }

        /// <summary>回收(不销毁):退回池中等待下次命中复用。</summary>
        private void Retire()
        {
            _rings.Remove(this);
            gameObject.SetActive(false);
            _ringPool.Push(this);
        }

        // ================= 资源 =================

        private static Material AlphaUnlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        private static Material AlphaParticles(Texture2D sprite)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            if (sprite != null) m.SetTexture("_BaseMap", sprite);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        private static Texture2D _hexSprite;
        /// <summary>运行时生成实心六边形碎片贴图(白色,由粒子 startColor 染阵营色)。</summary>
        private static Texture2D HexSprite()
        {
            if (_hexSprite != null) return _hexSprite;
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var center = new Vector2(S / 2f, S / 2f);
            float R = S * 0.44f;
            var px = tex.GetPixels();
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f) - center;
                    bool inside = Mathf.Abs(p.x) * 1.1547f <= R
                        && Mathf.Abs(p.x * 0.5f + p.y * 0.866f) * 1.1547f <= R
                        && Mathf.Abs(-p.x * 0.5f + p.y * 0.866f) * 1.1547f <= R;
                    float v = inside ? 0.78f : 0f;
                    float d = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                    if (inside && d > R * 0.62f) v = 1f;
                    px[y * S + x] = new Color(v, v, v, v);
                }
            tex.SetPixels(px);
            tex.Apply();
            _hexSprite = tex;
            return tex;
        }

        private static Texture2D _dotSprite;
        /// <summary>飘散光点=柔和圆点(径向平方衰减)。</summary>
        private static Texture2D SoftDotSprite()
        {
            if (_dotSprite != null) return _dotSprite;
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = tex.GetPixels();
            var center = new Vector2(S / 2f, S / 2f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / (S * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    // 2026-09-11 收尾实测:RGB 也随衰减压暗 → 在亮白广场上读成"灰污点"而不是光点。
                    // 只压 alpha、RGB 恒白,颜色由粒子 startColor 给(饱和阵营色),任何背景都读得出。
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            _dotSprite = tex;
            return tex;
        }
    }

    /// <summary>命中核心闪光:命中点一个短促亮斑(0.12s 爆闪即收)。自持对象池。</summary>
    public sealed class WallFlash : MonoBehaviour
    {
        private static readonly List<WallFlash> _live = new List<WallFlash>();
        private static readonly Stack<WallFlash> _pool = new Stack<WallFlash>();

        private Transform _quad;
        private Material _mat;
        private Color _col;
        private float _t, _life, _maxR;

        /// <summary>建一个闪光实例(池空时调用)。</summary>
        public static void Spawn(Transform parent, Vector3 pos, Vector3 normal, Color col, float life, float maxR)
        {
            WallFlash fx;
            if (_pool.Count > 0)
            {
                fx = _pool.Pop();
                fx.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("WallFlash");
                go.transform.SetParent(parent, false);
                fx = go.AddComponent<WallFlash>();
                fx.Build();
            }
            _live.Add(fx);
            fx.transform.position = pos;
            fx.transform.rotation = Quaternion.LookRotation(normal.normalized, Vector3.up);
            fx._t = 0f; fx._life = life; fx._maxR = maxR; fx._col = col;
            fx.Apply(col, 0.35f, 1f);
        }

        private void Build()
        {
            _mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _mat.SetColor("_BaseColor", Color.white);
            _mat.SetFloat("_Surface", 1f); _mat.SetFloat("_Blend", 0f);
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_ZWrite", 0);
            _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _quad = ViewAssets.DecoQuad(_mat, Vector3.zero, new Vector2(1f, 1f), transform, "FlashQuad").transform;
        }

        private void Apply(Color col, float scale, float alpha)
        {
            if (_quad != null) _quad.localScale = new Vector3(scale, scale, 1f);
            if (_mat != null) _mat.SetColor("_BaseColor", new Color(col.r, col.g, col.b, alpha));
        }

        private void Update() { Advance(Time.deltaTime); }

        public static void AdvanceAll(float dt)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var fx = _live[i];
                if (fx == null) { _live.RemoveAt(i); continue; }
                fx.Advance(dt);
            }
        }

        private void Advance(float dt)
        {
            _t += dt;
            float k = _t / _life;
            if (k >= 1f) { _live.Remove(this); gameObject.SetActive(false); _pool.Push(this); return; }
            float r = Mathf.Lerp(0.35f, _maxR, 1f - (1f - k) * (1f - k));
            Apply(new Color(_col.r, _col.g, _col.b), r, (1f - k) * 0.95f);
        }

    }
}
