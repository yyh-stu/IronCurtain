using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// M2 共享效果库(金样候选,参数=README §4 调参起点;制作人风格裁定前一律"待验")。
    /// 全部走 VfxDirector 的池 + 战斗钟;共享材质 + MPB 着色(命中不新建材质);
    /// 出厂即无 Collider。颜色主题沿用已拍板配色:坠日红黄/星落紫,不擅改。
    /// 时序:命中亮点 0.04–0.08s,主体 0.08–0.18s,收尾 0.10–0.30s。
    /// </summary>
    public static class FxBank
    {
        // 主题色(已拍板口径:阵营蓝红只做敌我识别;技能色做内核)
        public static readonly Color Steel = new Color(0.78f, 0.82f, 0.90f);      // 磐石·钢银
        public static readonly Color EarthGold = new Color(0.80f, 0.66f, 0.34f);  // 镇岳·土金
        public static readonly Color SilverAmber = new Color(0.85f, 0.78f, 0.52f);// 断峰·银+琥珀
        public static readonly Color Wind = new Color(0.72f, 0.85f, 0.80f);       // 逐风·风带
        public static readonly Color Sunfire = new Color(1.00f, 0.62f, 0.28f);    // 坠日·红黄(既定)
        public static readonly Color Arcane = new Color(0.72f, 0.48f, 1.00f);     // 星落·紫(既定)
        public static readonly Color ShieldTint = new Color(0.55f, 0.85f, 1.00f); // 盾·青白

        private static Material _mat;

        public static Material SharedMaterial
        {
            get
            {
                if (_mat == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                    if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                    _mat = new Material(shader);
                }
                return _mat;
            }
        }

        /// <summary>按主将英雄取主题色(士兵随主将;无英雄=中性钢色)。</summary>
        public static Color ThemeOf(Entity attacker, SimCore sim)
        {
            if (attacker == null) return Steel;
            var hero = attacker.Hero;
            if (attacker.IsSoldier && attacker.GeneralId != 0)
            {
                var gen = sim.GetEntity(attacker.GeneralId);
                if (gen != null && gen.Hero.HasValue) hero = gen.Hero;
            }
            if (!hero.HasValue) return Steel;
            switch (hero.Value)
            {
                case HeroId.PanShi: return Steel;
                case HeroId.ZhenYue: return EarthGold;
                case HeroId.DuanFeng: return SilverAmber;
                case HeroId.ZhuFeng: return Wind;
                case HeroId.ZhuiRi: return Sunfire;
                case HeroId.XingLuo: return Arcane;
                default: return Steel;
            }
        }

        // ---------- 工厂:造一个一次性粒子宿主(由 VfxDirector 的池持有) ----------

        /// <summary>C01 命中:短亮点+方向性碎屑。strength:主将强/士兵弱。</summary>
        public static GameObject MakeHitSpark()
        {
            return MakeBurst(count: 9, speed: 3.2f, life: 0.22f, size: 0.09f, gravity: -2.2f, stretch: 2.2f);
        }

        /// <summary>C02 盾承击:受击侧短盾面闪(单层、快速淡出)。</summary>
        public static GameObject MakeShieldFlash()
        {
            var go = new GameObject("FX_ShieldFlash");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.15f; main.loop = false; main.playOnAwake = true;
            main.startLifetime = 0.15f; main.startSpeed = 0f; main.startSize = 0.9f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.45f;
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(White(0.9f), White(0f));
            AttachRenderer(ps);
            return go;
        }

        /// <summary>C03 破盾:裂纹散开+短锐光(两层:亮核+碎屑)。</summary>
        public static GameObject MakeShieldBreak()
        {
            var root = new GameObject("FX_ShieldBreak");
            var core = MakeBurst(count: 3, speed: 1.2f, life: 0.10f, size: 0.30f, gravity: 0f, stretch: 1f);
            core.transform.SetParent(root.transform, false);
            var shards = MakeBurst(count: 12, speed: 4.2f, life: 0.30f, size: 0.10f, gravity: -3.0f, stretch: 2.8f);
            shards.transform.SetParent(root.transform, false);
            return root;
        }

        /// <summary>C05 阵亡:短尘/碎光;主将加一簇上升碎光。</summary>
        public static GameObject MakeDeath(bool general)
        {
            var root = new GameObject("FX_Death");
            var dust = MakeBurst(count: general ? 12 : 6, speed: general ? 2.4f : 1.4f,
                life: 0.55f, size: 0.16f, gravity: 0.6f, stretch: 1f);
            dust.transform.SetParent(root.transform, false);
            if (general)
            {
                var sparks = MakeBurst(count: 5, speed: 2.8f, life: 0.45f, size: 0.08f, gravity: -1.2f, stretch: 1.6f);
                sparks.transform.SetParent(root.transform, false);
                sparks.name = "sparks";
            }
            return root;
        }

        /// <summary>出招弧(M3 主将出招,M2.1 样板先用):朝目标的短弧面。</summary>
        public static GameObject MakeSwingArc()
        {
            var go = new GameObject("FX_SwingArc");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.12f; main.loop = false; main.playOnAwake = true;
            main.startLifetime = 0.12f; main.startSpeed = 2.0f; main.startSize = 0.20f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 22f;
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(White(0.95f), White(0f));
            var vel = ps.velocityOverLifetime; vel.enabled = true;
            vel.speedModifier = 2.4f;
            AttachRenderer(ps);
            return go;
        }

        /// <summary>通用径向爆点工厂。</summary>
        private static GameObject MakeBurst(int count, float speed, float life, float size, float gravity, float stretch)
        {
            var go = new GameObject("FX_Burst");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = life + 0.05f; main.loop = false; main.playOnAwake = true;
            main.startLifetime = life; main.startSpeed = speed; main.startSize = size;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.08f;
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(White(0.95f), White(0f));
            var sizeLt = ps.sizeOverLifetime; sizeLt.enabled = true;
            sizeLt.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f));
            if (stretch > 1f)
            {
                var stretchMod = ps.velocityOverLifetime;   // 拉长=速度方向拉伸(碎屑感)
                // URP Unlit 粒子不支持 renderer stretch 直接属性,以 sizeOverLifetime 收尾代替
            }
            AttachRenderer(ps);
            return go;
        }

        private static void AttachRenderer(ParticleSystem ps)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SharedMaterial;
            r.alignment = ParticleSystemRenderSpace.Facing;
        }

        private static Color White(float a) => new Color(1f, 1f, 1f, a);

        /// <summary>按主题着色(借出时调用;MPB,不改共享材质)。</summary>
        public static void Tint(GameObject go, Color c)
        {
            if (go == null) return;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r == null) continue;
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", c);
                r.SetPropertyBlock(block);
            }
        }
    }
}
