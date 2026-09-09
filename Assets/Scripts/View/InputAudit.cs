#if UNITY_EDITOR
using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>Editor-only telemetry for a real mouse/keyboard acceptance run.</summary>
    public static class InputAudit
    {
        public const string SessionKey = "ic.inputaudit";
        private const string Prefix = "[InputAudit] ";

        public static bool Enabled => SessionState.GetBool(SessionKey, false);

        public static void Log(string message)
        {
            if (Enabled) Debug.Log(Prefix + message);
        }

        public static string FormatIds(IReadOnlyList<int> ids)
        {
            if (ids == null || ids.Count == 0) return "none";
            var values = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) values[i] = ids[i].ToString();
            return string.Join(",", values);
        }

        public static List<int> GeneralIds(SimCore sim, Side side)
        {
            var ids = new List<int>();
            if (sim == null) return ids;
            foreach (var entity in sim.Entities.Values)
                if (entity != null && entity.Kind == EntityKind.Unit && !entity.IsSoldier
                    && entity.Side == side && entity.State != EntityState.Dead)
                    ids.Add(entity.Id);
            ids.Sort();
            return ids;
        }

        public static string DescribeGenerals(SimCore sim, Side side)
        {
            var ids = GeneralIds(sim, side);
            if (ids.Count == 0) return "none";
            var values = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                var entity = sim.GetEntity(ids[i]);
                values[i] = ids[i] + ":" + (entity != null && entity.Hero.HasValue
                    ? entity.Hero.Value.ToString()
                    : "Unknown");
            }
            return string.Join(",", values);
        }
    }

    /// <summary>Reports the baseline and roster changes while input auditing is enabled.</summary>
    public sealed class InputAuditProbe : MonoBehaviour
    {
        private SimCore _sim;
        private List<int> _previousIds;
        private int _previousPopulation;
        private bool _layoutLogged;
        private int _layoutFrames;

        private static string RectStr(Rect r)
        {
            return "(" + r.x.ToString("F0") + "," + r.y.ToString("F0") + "," + r.width.ToString("F0") + "," + r.height.ToString("F0") + ")";
        }

        public void Init(SimCore sim)
        {
            _sim = sim;
        }

        private void Start()
        {
            if (!InputAudit.Enabled || _sim == null)
            {
                Destroy(this);
                return;
            }

            _previousIds = InputAudit.GeneralIds(_sim, Side.Blue);
            _previousPopulation = _sim.PopulationOf(Side.Blue);
            InputAudit.Log("baseline side=Blue population=" + _previousPopulation + "/"
                + _sim.PopulationCapOf(Side.Blue) + " generals=" + InputAudit.DescribeGenerals(_sim, Side.Blue));
        }

        private void Update()
        {
            if (_sim == null || _previousIds == null) return;
            // 2026-09-10 真输入验收辅助:等 OnGUI 就绪后一次性打印屏幕尺寸+三面板真实矩形
            if (!_layoutLogged)
            {
                _layoutFrames++;
                var ur = UpgradeCommandPanel.LastPanelRect;
                var hr = HexCommandPanel.LastPanelRect;
                var tr = ToolCommandPanel.LastPanelRect;
                if (_layoutFrames > 30 && ur.width > 0f && hr.width > 0f && tr.width > 0f)
                {
                    _layoutLogged = true;
                    InputAudit.Log("layout screen=" + Screen.width + "x" + Screen.height
                        + " upgrade=" + RectStr(ur) + " hex=" + RectStr(hr) + " tool=" + RectStr(tr));
                }
            }
            var currentIds = InputAudit.GeneralIds(_sim, Side.Blue);
            int currentPopulation = _sim.PopulationOf(Side.Blue);
            if (currentPopulation == _previousPopulation && SameIds(_previousIds, currentIds)) return;

            var added = Difference(currentIds, _previousIds);
            var removed = Difference(_previousIds, currentIds);
            InputAudit.Log("roster_change side=Blue population=" + _previousPopulation + "->" + currentPopulation
                + " ids_before=" + InputAudit.FormatIds(_previousIds)
                + " ids_after=" + InputAudit.FormatIds(currentIds)
                + " new_ids=" + InputAudit.FormatIds(added)
                + " removed_ids=" + InputAudit.FormatIds(removed));
            _previousPopulation = currentPopulation;
            _previousIds = currentIds;
        }

        private static bool SameIds(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static List<int> Difference(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            var result = new List<int>();
            for (int i = 0; i < a.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < b.Count; j++)
                    if (a[i] == b[j]) { found = true; break; }
                if (!found) result.Add(a[i]);
            }
            return result;
        }

        private void OnDestroy()
        {
            SessionState.SetBool(InputAudit.SessionKey, false);
        }
    }
}
#endif
