using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>程序化 UI 音(2026-09-12 拍板 A+C 之 C):零资产,AudioClip 代码合成,单 AudioSource 播放。
    /// Click=按钮(由 UiKit.MakeButton 统一挂钩);Select=框选命中;Open=面板展开;Error=拒绝反馈。
    /// 音色=短促正弦/方波包络,音量克制(0.35),频繁触发不刺耳。</summary>
    public static class UiSfx
    {
        private const float Volume = 0.35f;
        private const int SampleRate = 22050;
        private static AudioSource _src;
        private static AudioClip _click, _select, _open, _error;

        public static void Click() => Play(ref _click, 0.06f, 900f, 500f, false);
        public static void Select() => Play(ref _select, 0.035f, 1250f, 1250f, false);
        public static void Open() => Play(ref _open, 0.09f, 480f, 760f, false);
        public static void Error() => Play(ref _error, 0.14f, 190f, 150f, true);

        private static void Play(ref AudioClip cache, float dur, float f0, float f1, bool square)
        {
            if (!EnsureSource()) return;
            if (cache == null) cache = Synth(dur, f0, f1, square);
            _src.pitch = 1f;
            _src.PlayOneShot(cache, Volume);
        }

        private static bool EnsureSource()
        {
            if (_src != null) return true;
            var host = new GameObject("UiSfx");
            host.hideFlags = HideFlags.DontSave;
            Object.DontDestroyOnLoad(host);          // 跨场景常驻(菜单↔战斗都播)
            _src = host.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            return true;
        }

        /// <summary>频率 f0→f1 线性滑音 + 指数衰减包络;square=true 时方波(错误提示更"硬")。</summary>
        private static AudioClip Synth(float dur, float f0, float f1, bool square)
        {
            int n = Mathf.CeilToInt(dur * SampleRate);
            var data = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2f * Mathf.PI * f / SampleRate;
                float s = square ? (Mathf.Sin(phase) >= 0f ? 0.6f : -0.6f) : Mathf.Sin(phase) * 0.9f;
                data[i] = s * Mathf.Pow(1f - t, 2.2f);          // 指数感衰减
                if (i < n * 0.02f) data[i] *= i / (n * 0.02f);  // 2% 防咔哒起音
            }
            var clip = AudioClip.Create("uisfx", n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
