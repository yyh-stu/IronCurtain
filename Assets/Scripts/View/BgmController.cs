using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 2026-09-10 制作人拍板·两阶段 BGM(参考决战平安京):
    /// 前期曲开局起;敌方第一个建筑被推(废墟)或被占领(易主)时切后期曲,保持到终局。
    /// 双 AudioSource 交叉淡化 2.5s;音频文件(Resources/Audio/bgm_early|bgm_late)缺席时静默跳过。
    /// 纯 View,直接扫 Sim 建筑状态,不改 Sim。
    /// </summary>
    public sealed class BgmController : MonoBehaviour
    {
        private SimCore _sim;
        private Side _player;
        private readonly int[] _enemyBuildingIds = new int[64];
        private int _enemyCount;
        private bool _switched;
        private float _checkT;
        private AudioSource _a, _b;
        private bool _lateOnB;
        private float _fadeT = -1f;
        private const float FadeSec = 2.5f;
        private const float Volume = 0.5f;

        // 2026-09-14 制作人报"BGM 重复播放":训练场**换边**是"原地重建视图",`GameBootstrap.TeardownView`
        // 只销毁了 `BgmController` **组件**,而它 `AddComponent<AudioSource>` 挂在物体上的两个音源**不会被一起销毁** ——
        // 循环的曲子继续播,新视图又建一套 → 两条 `bgm_early` 同时响(实测换边后:24 条音源、3 条 BGM 在播)。
        // 修法:①组件在 `OnDestroy` 里停掉并销毁自己建的音源;②加**全局单条 BGM 不变量** ——
        // 任何新控制器起来时先把上一个实例的音源停掉销毁,这样无论从哪条路径重建都不可能叠。
        private static readonly List<AudioSource> LiveSources = new List<AudioSource>();

        /// <summary>停掉并销毁所有"已被建出来"的 BGM 音源(换边/重建时由新实例调用)。</summary>
        private static void StopLiveSources()
        {
            for (int i = 0; i < LiveSources.Count; i++)
            {
                var s = LiveSources[i];
                if (s == null) continue;
                s.Stop();
                s.clip = null;
                Object.Destroy(s);
            }
            LiveSources.Clear();
        }

        private void OnDestroy()
        {
            LiveSources.Remove(_a);
            LiveSources.Remove(_b);
            if (_a != null) { _a.Stop(); Object.Destroy(_a); }
            if (_b != null) { _b.Stop(); Object.Destroy(_b); }
            _a = null; _b = null;
        }

        public void Init(SimCore sim, Side playerSide)
        {
            _sim = sim;
            _player = playerSide;
            SnapshotEnemyBuildings();
            StopLiveSources();   // 任何已有 BGM 音源先收掉(换边重建时旧实例的组件已销毁、音源还在播)
            _a = gameObject.AddComponent<AudioSource>();
            _b = gameObject.AddComponent<AudioSource>();
            LiveSources.Add(_a);
            LiveSources.Add(_b);
            SetupSource(_a);
            SetupSource(_b);
            var early = UnityEngine.Resources.Load<AudioClip>("Audio/bgm_early");
            var late = UnityEngine.Resources.Load<AudioClip>("Audio/bgm_late");
            if (early == null || late == null)
            {
                Debug.Log("[bgm] clip missing(early=" + (early != null) + " late=" + (late != null) + "), controller idle");
                enabled = false;
                return;
            }
            Debug.Log("[bgm] 本实例接管 BGM(已收掉旧音源),当前 BGM 音源数=" + LiveSources.Count);
            _a.clip = early;
            _b.clip = late;
            _a.volume = Volume;
            _b.volume = 0f;
            _a.Play(); // 前期曲起播
        }

        private static void SetupSource(AudioSource s)
        {
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f; // 2D
        }

        private void SnapshotEnemyBuildings()
        {
            _enemyCount = 0;
            if (_sim == null) return;
            foreach (var kv in _sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Building || e.Side == _player) continue;
                // 敌方+中立均不入列:制作人语义是"敌方第一个建筑"(中立资源点占领不算)
                if (e.Side == Side.Neutral) continue;
                if (_enemyCount < _enemyBuildingIds.Length) _enemyBuildingIds[_enemyCount++] = e.Id;
            }
        }

        private void Update()
        {
            if (_switched)
            {
                UpdateFade();
                return;
            }
            // 交叉淡化推进(触发后也要持续)
            if (_fadeT >= 0f) UpdateFade();
            _checkT += Time.unscaledDeltaTime;
            if (_checkT < 0.5f) return;
            _checkT = 0f;
            if (_sim == null) return;
            foreach (var kv in _sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Building) continue;
                // 条件一:敌方建筑被推成废墟
                if (e.Side == Side.Neutral && e.Rubbled && WasEnemy(e.Id)) { SwitchToLate(); return; }
                // 条件二:原敌方建筑易主为玩家方(占领)
                if (e.Side == _player && WasEnemy(e.Id)) { SwitchToLate(); return; }
            }
        }

        private bool WasEnemy(int id)
        {
            for (int i = 0; i < _enemyCount; i++)
                if (_enemyBuildingIds[i] == id) return true;
            return false;
        }

        private void SwitchToLate()
        {
            _switched = true;
            _lateOnB = true;
            _fadeT = 0f;
            _b.Play();
            Debug.Log("[bgm] switch to late phase");
        }

        private void UpdateFade()
        {
            if (_fadeT < 0f) return;
            _fadeT += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_fadeT / FadeSec);
            if (_lateOnB)
            {
                _a.volume = Volume * (1f - k);
                _b.volume = Volume * k;
            }
            else
            {
                _a.volume = Volume * k;
                _b.volume = Volume * (1f - k);
            }
            if (k >= 1f)
            {
                _fadeT = -1f;
                // 淡化完成后把输的一方**暂停**(音量 0 还继续解码没有意义,也避免任何残留出声)
                if (_lateOnB) { if (_a != null) _a.Pause(); }
                else if (_b != null) _b.Pause();
            }
        }
    }
}
