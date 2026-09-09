using System.Text;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // 诊断:兵团凝聚力——每主将名下士兵数、距主将距离分布、远离(>25m)士兵数与位置。
    // 2026-09-09 制作人质询"主将不动士兵为什么全跑了"定性用。屏外窗口跑。
    [InitializeOnLoad]
    public static class DiagRegimentCohesion
    {
        private static double _start = -1;
        private static bool _done;

        static DiagRegimentCohesion()
        {
            if (SessionState.GetBool("ic.cohesion", false))
                EditorApplication.update += OnUpdate;
        }

        public static void Run()
        {
            SessionState.SetBool("ic.cohesion", true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnUpdate()
        {
            if (_done || !EditorApplication.isPlaying) return;
            if (_start < 0) { _start = EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup - _start < 90.0) return; // t≈90s:河道已接战
            _done = true;
            SessionState.SetBool("ic.cohesion", false);

            var simObj = Object.FindObjectsByType<IronCurtain.View.BoardView>(FindObjectsSortMode.None);
            var fSim = typeof(IronCurtain.View.BoardView).GetField("_sim",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var sim = fSim != null && simObj.Length > 0 ? fSim.GetValue(simObj[0]) as IronCurtain.Sim.SimCore : null;
            if (sim == null) { Debug.Log("[coh] no sim"); EditorApplication.Exit(0); return; }

            var sb = new StringBuilder();
            foreach (var kv in sim.Entities)
            {
                var g = kv.Value;
                if (g == null || g.Kind != IronCurtain.Sim.EntityKind.Unit || g.IsSoldier
                    || g.State == IronCurtain.Sim.EntityState.Dead) continue;
                int own = 0, near = 0, far = 0; float maxD = 0;
                float fx = 0;
                foreach (var kv2 in sim.Entities)
                {
                    var s = kv2.Value;
                    if (s == null || !s.IsSoldier || s.GeneralId != g.Id || s.State == IronCurtain.Sim.EntityState.Dead) continue;
                    own++;
                    float d = System.MathF.Sqrt((s.PosF.X - g.PosF.X) * (s.PosF.X - g.PosF.X) + (s.PosF.Y - g.PosF.Y) * (s.PosF.Y - g.PosF.Y));
                    if (d <= 25f) near++; else { far++; fx += s.PosF.X; }
                    if (d > maxD) maxD = d;
                }
                sb.Append(g.Side).Append('/').Append(g.Hero).Append(" id=").Append(g.Id)
                  .Append(" pos=(").Append((int)g.PosF.X).Append(',').Append((int)g.PosF.Y).Append(')')
                  .Append(" state=").Append(g.State)
                  .Append(" soldiers=").Append(own).Append(" near=").Append(near).Append(" far=").Append(far)
                  .Append(" maxD=").Append((int)maxD);
                if (far > 0) sb.Append(" farAvgX=").Append((int)(fx / far));
                sb.Append('\n');
            }
            Debug.Log("[coh] RESULT\n" + sb);
            EditorApplication.Exit(0);
        }
    }
}
