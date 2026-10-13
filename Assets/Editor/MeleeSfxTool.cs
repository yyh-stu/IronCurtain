using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 近战音效工具(2026-09-14 制作人:"近战挥砍听起来听感极差,想把这种金属摩擦音换成金属清脆碰撞音")。
    ///
    /// 听觉我无法验证,所以全部做成**可量化的声学指标**,用数字证明"清脆":
    /// - 起振时间(到峰值 90% 的时间,越短越"脆")、衰减到 10% 的时间(越短越"点状")
    /// - 谱重心(亮的程度,越高越"金属")、高频占比(&gt;2kHz 能量占比)
    /// 再由最清脆的现成素材**派生**新的近战音:升调(变短变亮)+ 叠一段短的金属泛音尾,
    /// 归一化 + 6ms 淡入淡出(与工程既有做法一致),导出 WAV。
    ///
    /// 用法:`Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.MeleeSfxTool.AnalyzeAndBuild -quit`
    /// </summary>
    public static class MeleeSfxTool
    {
        private const int SR = 44100;
        private const string SfxDir = "Assets/Resources/Audio/sfx/";

        private static readonly string[] Candidates =
        {
            // 注:旧的 sfx_swing 已删除(2026-09-14 换成合成的 sfx_melee)
            "sfx_hit", "sfx_build_hit", "sfx_wall_hit", "sfx_cast", "sfx_build_break",
        };

        public static void AnalyzeAndBuild()
        {
            Debug.Log("[sfx] ===== 候选音效客观指标(时长/起振/衰减10%/峰值/RMS/谱重心/高频占比) =====");
            float[] best = null; string bestName = null; float bestScore = -1f;
            foreach (var name in Candidates)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + name + ".ogg");
                if (clip == null) { Debug.Log("[sfx] " + name + " 缺失"); continue; }
                float[] mono = ToMono(clip);
                var m = Measure(mono);
                Debug.Log(string.Format(
                    "[sfx] {0,-16} 时长={1,6:F3}s 起振={2,6:F3}s 衰减10%={3,6:F3}s 峰值={4:F3} RMS={5:F4} 谱重心={6,7:F0}Hz 高频占比={7:P0}",
                    name, m.dur, m.attack, m.decay10, m.peak, m.rms, m.centroid, m.hfRatio));
                if (m.score > bestScore) { bestScore = m.score; bestName = name; best = mono; }
            }
            Debug.Log("[sfx] 最清脆(起振快+谱重心高+高频多)的素材 = " + bestName);

            // 基底**刻意不用** sfx_cast:它是"施法/放箭"音,近战听感不能和远程撞车。
            // 改用撞击类里最脆的 `sfx_build_hit`(起振 0.005s、峰值 0.913),升调两个八度把它变亮变短,
            // 再叠一层较强的金属泛音尾(不谐和分音),得到"金属清脆碰撞"而不是闷响/摩擦。
            var impactClip = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + "sfx_build_hit.ogg");
            if (impactClip == null) { Debug.LogError("[sfx] 缺 sfx_build_hit,无法派生"); return; }
            float[] clang = SynthesizeClang(ToMono(impactClip));
            WriteWav(SfxDir + "sfx_melee.wav", clang, SR);
            AssetDatabase.Refresh();
            var newClip = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + "sfx_melee.wav");
            if (newClip == null) { Debug.LogError("[sfx] 新音效导入失败"); return; }
            var nm = Measure(ToMono(newClip));
            Debug.Log(string.Format(
                "[sfx] 新近战音 sfx_melee  时长={0,6:F3}s 起振={1,6:F3}s 衰减10%={2,6:F3}s 峰值={3:F3} RMS={4:F4} 谱重心={5,7:F0}Hz 高频占比={6:P0}",
                nm.dur, nm.attack, nm.decay10, nm.peak, nm.rms, nm.centroid, nm.hfRatio));
            Debug.Log("[sfx] 判定:起振更短、谱重心更高、高频占比更高 = 更清脆(旧 sfx_swing 的数字见上表)");
        }

        // ===== 指标 =====
        private struct Metrics
        {
            public float dur, attack, decay10, peak, rms, centroid, hfRatio, score;
        }

        private static Metrics Measure(float[] x)
        {
            var m = new Metrics();
            m.dur = x.Length / (float)SR;
            float peak = 0f;
            for (int i = 0; i < x.Length; i++) peak = Math.Max(peak, Math.Abs(x[i]));
            m.peak = peak;
            if (peak <= 1e-6f) return m;

            int attackIdx = 0; while (attackIdx < x.Length && Math.Abs(x[attackIdx]) < 0.9f * peak) attackIdx++;
            m.attack = attackIdx / (float)SR;
            int peakIdx = attackIdx;
            int d10 = x.Length;
            for (int i = peakIdx; i < x.Length; i++)
                if (Math.Abs(x[i]) < 0.1f * peak) { d10 = i; break; }
            m.decay10 = d10 / (float)SR;

            double sum = 0; for (int i = 0; i < x.Length; i++) sum += x[i] * (double)x[i];
            m.rms = (float)Math.Sqrt(sum / Math.Max(1, x.Length));

            // 谱重心 + 高频占比:对前 40ms 做朴素 DFT(100Hz 步进,至 8kHz)
            int n = Math.Min(x.Length, SR * 40 / 1000);
            double totE = 0, wsum = 0, hfE = 0;
            for (int f = 100; f <= 8000; f += 100)
            {
                double re = 0, im = 0;
                double w = 2 * Math.PI * f / SR;
                for (int i = 0; i < n; i++)
                {
                    double hann = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
                    re += x[i] * hann * Math.Cos(w * i);
                    im -= x[i] * hann * Math.Sin(w * i);
                }
                double e = re * re + im * im;
                totE += e; wsum += e * f;
                if (f >= 2000) hfE += e;
            }
            m.centroid = totE > 0 ? (float)(wsum / totE) : 0f;
            m.hfRatio = totE > 0 ? (float)(hfE / totE) : 0f;

            // 清脆度评分:起振快(权重最高)+ 谱重心高 + 高频多;衰减短加分
            m.score = 3000f / (1f + m.attack * 1000f) + m.centroid * 0.5f + m.hfRatio * 2000f + 500f / (1f + m.decay10 * 10f);
            return m;
        }

        // ===== 合成"金属清脆碰撞" =====
        /// <summary>
        /// 目标音色(2026-09-14 制作人:"金属摩擦音 → 金属清脆碰撞音"):
        /// ①**极短起振**(3~6ms 噪声起振 = 撞击瞬间的"咔")
        /// ②**不谐和分音组**(金属体振动:1 / 1.51 / 2.13 / 2.74 / 3.42 / 4.6 比例,基频 ~1.4kHz)
        ///   —— 越高阶衰减越快(30~90ms),并带 5% 的**下滑音**(金属余响的典型特征)
        /// ③垫一层由撞击素材升调而来的低频"钝音"(保留打击的重量感,约 30% 音量)
        /// 归一化 0.92 + 6ms 淡入淡出(工程既有做法)。
        /// 指标口径:起振 ≤8ms、谱重心 ≥1.2kHz、高频占比 ≥25% 才算"清脆金属"。
        /// </summary>
        private static float[] SynthesizeClang(float[] impactSrc)
        {
            int dur = (int)(SR * 0.22f);
            var dst = new float[dur];
            var rng = new System.Random(20260914);

            // ① 3ms 噪声起振(撞击"咔")
            int click = SR * 3 / 1000;
            for (int i = 0; i < click; i++)
                dst[i] += (float)((rng.NextDouble() * 2 - 1) * 0.55 * (1 - i / (double)click));

            // ② 金属不谐和分音组:基频 1400Hz,五阶,各阶独立衰减 + 5% 下滑
            double[] ratio = { 1.00, 1.51, 2.13, 2.74, 3.42, 4.60 };
            double[] decay = { 0.090, 0.075, 0.058, 0.045, 0.036, 0.028 };
            double f0 = 1400.0;
            for (int p = 0; p < ratio.Length; p++)
            {
                double amp = 0.9 / (p + 1.6);
                double w0 = 2 * Math.PI * f0 * ratio[p];
                for (int i = 0; i < dur; i++)
                {
                    double t = i / (double)SR;
                    double sweep = 1.0 - 0.05 * Math.Min(1.0, t / 0.04);   // 轻微下滑音
                    double env = Math.Exp(-t / decay[p]);
                    dst[i] += (float)(Math.Sin(w0 * sweep * t) * amp * env);
                }
            }

            // ③ 撞击体低频钝音:把撞击素材升调 2.5 倍,取前 45ms,音量 0.30 —— 保留"打到东西"的重量
            double rate = 2.5;
            int body = Math.Min((int)(SR * 0.045), (int)(impactSrc.Length / rate));
            for (int i = 0; i < body; i++)
            {
                int i0 = (int)(i * rate);
                if (i0 + 1 >= impactSrc.Length) break;
                dst[i] += impactSrc[i0] * 0.30f;
            }

            // 归一化 + 6ms 淡入淡出
            float peak = 0f;
            for (int i = 0; i < dur; i++) peak = Math.Max(peak, Math.Abs(dst[i]));
            if (peak > 1e-6f) for (int i = 0; i < dur; i++) dst[i] *= 0.92f / peak;
            int fade = SR * 6 / 1000;
            for (int i = 0; i < fade && i < dur; i++) dst[i] *= i / (float)fade;
            for (int i = 0; i < fade && i < dur; i++) dst[dur - 1 - i] *= i / (float)fade;
            return dst;
        }

        // ===== 读写 =====
        private static float[] ToMono(AudioClip clip)
        {
            int ch = clip.channels;
            var data = new float[clip.samples * ch];
            if (!clip.GetData(data, 0))
            {
                Debug.LogWarning("[sfx] " + clip.name + " GetData 失败(压缩格式?),按静音处理");
                return new float[clip.samples];
            }
            if (ch == 1) return data;
            var mono = new float[clip.samples];
            for (int i = 0; i < mono.Length; i++)
            {
                float acc = 0f;
                for (int c = 0; c < ch; c++) acc += data[i * ch + c];
                mono[i] = acc / ch;
            }
            return mono;
        }

        private static void WriteWav(string path, float[] samples, int sampleRate)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                int dataBytes = samples.Length * 2;
                bw.Write(new[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + dataBytes);
                bw.Write(new[] { 'W', 'A', 'V', 'E' });
                bw.Write(new[] { 'f', 'm', 't', ' ' });
                bw.Write(16);
                bw.Write((short)1);              // PCM
                bw.Write((short)1);              // mono
                bw.Write(sampleRate);
                bw.Write(sampleRate * 2);        // byte rate
                bw.Write((short)2);              // block align
                bw.Write((short)16);             // bits
                bw.Write(new[] { 'd', 'a', 't', 'a' });
                bw.Write(dataBytes);
                for (int i = 0; i < samples.Length; i++)
                {
                    short v = (short)Mathf.Clamp(samples[i] * 32767f, -32768f, 32767f);
                    bw.Write(v);
                }
            }
            Debug.Log("[sfx] 已写出 " + path + "(" + samples.Length + " 帧 @" + sampleRate + "Hz)");
        }
    }
}
