using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// M3-S01 SFX 框架:SimEvent 驱动 + AudioSource 对象池(10源,优先级抢占)。
    /// 音频文件放入 Resources/Audio/sfx/ 即生效;缺席时静默跳过(每音效名仅警告一次)。
    /// BGM 由 BgmController 处理,本类不涉及。
    /// </summary>
    public sealed class AudioSystem : MonoBehaviour
    {
        private struct SfxSpec
        {
            public string Name;
            public int Priority;
            public float PitchMin;
            public float PitchMax;

            public SfxSpec(string name, int priority, float pitchMin, float pitchMax)
            {
                Name = name;
                Priority = priority;
                PitchMin = pitchMin;
                PitchMax = pitchMax;
            }
        }

        private const int PoolSize = 10;
        private const float Volume = 0.7f;
        private const float ThrottleSeconds = 0.05f;

        // 事件→音效映射表(M3-S01 规格)
        private static readonly Dictionary<SimEventType, SfxSpec> EventMap =
            new Dictionary<SimEventType, SfxSpec>
            {
                { SimEventType.DamageDealt, new SfxSpec("sfx_hit", 3, 0.9f, 1.1f) },
                { SimEventType.EntityDied, new SfxSpec("sfx_die", 4, 0.95f, 1.05f) },
                { SimEventType.EntitySpawned, new SfxSpec("sfx_spawn", 2, 1.0f, 1.0f) },
                { SimEventType.CommandExecuted, new SfxSpec("sfx_command", 2, 1.0f, 1.0f) },
                { SimEventType.OvertimeStarted, new SfxSpec("sfx_overtime", 5, 1.0f, 1.0f) },
                { SimEventType.MatchEnded, new SfxSpec("sfx_matchend", 5, 1.0f, 1.0f) },
            };

        private SimCore _sim;
        private readonly List<AudioSource> _pool = new List<AudioSource>(PoolSize);
        private readonly HashSet<string> _warnedMissing = new HashSet<string>();
        private readonly Dictionary<string, float> _lastPlayTime = new Dictionary<string, float>();

        public void Init(SimCore sim)
        {
            _sim = sim;
            BuildPool();
            _sim.AddEventListener(OnEvent);
        }

        private void BuildPool()
        {
            if (_pool.Count > 0) return; // 域重载防护:池已建则跳过
            for (int i = 0; i < PoolSize; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f; // 2D
                src.volume = Volume;
                _pool.Add(src);
            }
        }

        private void OnEvent(SimEvent evt)
        {
            float now = Time.unscaledTime;
            // 2026-09-12 拍板:屏障与建筑分家——屏障=瓷器材质(Broken Plate,bigsoundbank 公有领域),
            // 击碎=整段(瓷盘落碎);受击=同源音频弃前 1/7 取后 6/7(去掉起始瞬态,做"被打"听感)。
            // 建筑(石造棱堡)仍走 sfx_build_hit(采矿石响)/sfx_build_break。单位受击按攻击者细分同下。
            if (evt.Type == SimEventType.WallDamaged)
            {
                PlayThrottled("sfx_wall_hit", 3, 0.95f, 1.05f, now);
                return;
            }
            if (evt.Type == SimEventType.WallBroken)
            {
                PlayThrottled("sfx_wall_break", 4, 1f, 1f, now);
                return;
            }
            if (!EventMap.TryGetValue(evt.Type, out var spec)) return;
            if (evt.Type == SimEventType.DamageDealt)
            {
                var target = _sim.GetEntity(evt.SubjectId);
                if (target != null && target.Kind == EntityKind.Building)
                {
                    PlayThrottled("sfx_build_hit", 3, 0.95f, 1.05f, now);
                    return;
                }
                var atk = evt.AttackerId != 0 ? _sim.GetEntity(evt.AttackerId) : null;
                if (atk == null)
                {
                    PlayThrottled("sfx_hit", 3, spec.PitchMin, spec.PitchMax, now);   // 塔/未指名
                    return;
                }
                // 2026-09-13 箭塔开火此前不发伤害事件,所以这一段从未走到过;补发后到这里按放箭音处理
                if (atk.Kind == EntityKind.Building)
                {
                    PlayThrottled("sfx_cast", 3, 0.80f, 0.90f, now);                 // 箭塔放箭
                    return;
                }
                if (atk.Hero == HeroId.ZhuiRi || atk.Hero == HeroId.XingLuo)
                {
                    PlayThrottled("sfx_cast", 3, 0.95f, 1.1f, now);                  // 远程施法/放箭
                    return;
                }
                // 2026-09-14 制作人:"近战挥砍听感极差,把金属摩擦音换成金属清脆碰撞音"。
                // 旧 sfx_swing 客观指标就是"摩擦/挥空":起振 **0.258s**、谱重心 **337Hz**、高频占比 **3%**。
                // 新 sfx_melee 为合成金属碰撞(3ms 噪声起振 + 不谐和分音组 + 撞击钝音垫底):
                // 起振 **0.006s**、谱重心 **1961Hz**、高频占比 **48%**,时长 0.653→0.220s。
                PlayThrottled("sfx_melee", 3, 0.92f, 1.12f, now);                    // 近战:金属清脆碰撞
                MeleePlays++;
                return;
            }
            if (evt.Type == SimEventType.EntityDied)
            {
                var dead = _sim.GetEntity(evt.SubjectId);
                if (dead != null && dead.Kind == EntityKind.Building)
                {
                    PlayThrottled("sfx_build_break", 4, 1f, 1f, now);
                    return;
                }
            }
            if (_lastPlayTime.TryGetValue(spec.Name, out float last) && now - last < ThrottleSeconds) return;
            _lastPlayTime[spec.Name] = now;
            Play(spec.Name, spec.Priority, spec.PitchMin, spec.PitchMax);
        }

        /// <summary>供实机自检:近战命中音累计播放次数(证明接线走到了新音效)。</summary>
        public static int MeleePlays;

        private void PlayThrottled(string name, int priority, float pitchMin, float pitchMax, float now)
        {
            if (_lastPlayTime.TryGetValue(name, out float last) && now - last < ThrottleSeconds) return;
            _lastPlayTime[name] = now;
            Play(name, priority, pitchMin, pitchMax);
        }

        /// <summary>播放入口:clip 缺失静默跳过(每音效名仅警告一次);空闲源优先,无空闲则抢占最低优先级。</summary>
        public void Play(string sfxName, int priority, float pitchMin, float pitchMax)
        {
            var clip = Resources.Load<AudioClip>("Audio/sfx/" + sfxName);
            if (clip == null)
            {
                if (_warnedMissing.Add(sfxName))
                    Debug.LogWarning($"[AudioSystem] sfx missing: Audio/sfx/{sfxName} (silent skip)");
                return;
            }

            AudioSource target = null;
            AudioSource lowest = null;
            for (int i = 0; i < _pool.Count; i++)
            {
                var src = _pool[i];
                if (!src.isPlaying) { target = src; break; }
                if (lowest == null || src.priority > lowest.priority) lowest = src;
            }

            if (target == null)
            {
                // 无空闲:抢占正在播的最低优先级源(AudioSource.priority 越小越高)
                if (lowest != null && priority * -1 < lowest.priority) target = lowest;
                if (target == null) return; // 新优先级不够高,丢弃
            }

            target.priority = priority * -1; // 映射为 Unity priority(小=高)
            target.pitch = Random.Range(pitchMin, pitchMax);
            target.volume = Volume;
            target.clip = clip;
            target.Play();
        }

        private void OnDisable()
        {
            // 防域重载重复注册
            if (_sim != null) _sim.RemoveEventListener(OnEvent);
        }

        /// <summary>
        /// 2026-09-14:与 `BgmController` 同一个漏 —— 池里 10 条 AudioSource 是**挂在物体上的独立组件**,
        /// `GameBootstrap.TeardownView` 只销毁 `AudioSystem` 组件,音源会留在物体上:换边一次多 10 条
        /// (实测换边后音源总数 12→24)。这里由组件自己把池收干净。
        /// </summary>
        private void OnDestroy()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                var s = _pool[i];
                if (s == null) continue;
                s.Stop();
                Object.Destroy(s);
            }
            _pool.Clear();
        }
    }
}
