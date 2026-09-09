using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // 诊断:Play 中段枚举全部 EntityView,转储 士兵/主将 各自的渲染配置
    // (是否挂 Animator=FBX 主将路径;士兵应为无 Animator 的程序化模型)。
    // 2026-09-09 制作人目击"士兵=磐石模型+攻击动画"案定性用。
    // 域重载安全:[InitializeOnLoad] 重订阅(诊断标记走 SessionState)。
    [InitializeOnLoad]
    public static class DiagSoldierViews
    {
        private static double _start = -1;
        private static bool _done;

        static DiagSoldierViews()
        {
            if (SessionState.GetBool("ic.diag", false))
                EditorApplication.update += OnUpdate;
        }

        public static void Run()
        {
            SessionState.SetBool("ic.diag", true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnUpdate()
        {
            if (_done || !EditorApplication.isPlaying) return;
            if (_start < 0) { _start = EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup - _start < 15.0) return;
            _done = true;
            SessionState.SetBool("ic.diag", false);

            var fAnim = typeof(IronCurtain.View.EntityView).GetField("_anim", BindingFlags.NonPublic | BindingFlags.Instance);
            var fEnt = typeof(IronCurtain.View.EntityView).GetField("_e", BindingFlags.NonPublic | BindingFlags.Instance);
            int soldiers = 0, soldiersWithAnim = 0, generals = 0, buildings = 0;
            var heroNames = new StringBuilder();
            foreach (var v in Object.FindObjectsByType<IronCurtain.View.EntityView>(FindObjectsSortMode.None))
            {
                var e = fEnt.GetValue(v) as IronCurtain.Sim.Entity;
                if (e == null) continue;
                bool anim = fAnim.GetValue(v) != null;
                if (e.Kind == IronCurtain.Sim.EntityKind.Building) { buildings++; continue; }
                if (e.IsSoldier)
                {
                    soldiers++;
                    if (anim) { soldiersWithAnim++; if (soldiersWithAnim <= 3) Debug.Log("[diag] FBX-SOLDIER id=" + e.Id + " hero=" + e.Hero + " gen=" + e.GeneralId); }
                }
                else
                {
                    generals++;
                    heroNames.Append(e.Hero).Append(',');
                }
            }
            Debug.Log("[diag] RESULT soldiers=" + soldiers + " soldiersWithFBXAnim=" + soldiersWithAnim
                + " generals=" + generals + " buildings=" + buildings + " generalHeroes=" + heroNames);
            EditorApplication.Exit(0);
        }
    }
}
