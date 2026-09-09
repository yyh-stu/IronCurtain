using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>M6.2 顶栏:左己方基地血量+人口 / 中对局时间 / 右敌方基地血量(迷雾口径,未侦察显示 ???)。</summary>
    public sealed class TopBarPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = Rect.zero;

        private SimCore _sim;
        private Side _side;
        private Entity _myBase, _enemyBase;
        private float _nextQuery;

        public void Init(SimCore sim, Side playerSide)
        {
            _sim = sim;
            _side = playerSide;
            _nextQuery = -1f;
        }

        private void OnGUI()
        {
            if (_sim == null) return;
            var rect = new Rect(0f, 0f, Screen.width, 40f);
            LastPanelRect = rect;
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            if (Time.realtimeSinceStartup >= _nextQuery)
            {
                _nextQuery = Time.realtimeSinceStartup + 0.5f;
                _myBase = TopBarModel.FindBase(_sim, _side);
                var other = _side == Side.Blue ? Side.Red : Side.Blue;
                _enemyBase = TopBarModel.FindBase(_sim, other);
            }

            float y = 13f;
            var label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            label.normal.textColor = Color.white;
            GUI.Label(new Rect(10f, y - 7f, 70f, 24f), "大本营", label);
            if (_myBase != null)
            {
                DrawHpBar(new Rect(80f, y, 140f, 14f), _myBase.Hp, _myBase.MaxHp);
                GUI.Label(new Rect(228f, y - 4f, 110f, 22f), TopBarModel.HpText(_myBase.Hp, _myBase.MaxHp), label);
            }
            GUI.Label(new Rect(344f, y - 7f, 110f, 24f), TopBarModel.PopText(_sim, _side), label);
            GUI.Label(new Rect(458f, y - 7f, 180f, 24f), TopBarModel.EpText(_sim, _side), label);

            var clockStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            clockStyle.normal.textColor = Color.white;
            if (_sim.Phase == MatchPhase.Overtime) clockStyle.normal.textColor = new Color(1f, 0.55f, 0.2f); // 加时橙红
            GUI.Label(new Rect(Screen.width * 0.5f - 90f, 4f, 180f, 26f),
                TopBarModel.FormatCountdown(_sim), clockStyle);
            var sub = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            sub.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(Screen.width * 0.5f - 90f, 28f, 180f, 14f), TopBarModel.PhaseText(_sim), sub);

            var snap = _enemyBase != null
                ? VisionSystem.LastKnownBuilding(_sim, _side, _enemyBase.Id) : null;
            float right = Screen.width - 10f;
            if (snap == null)
            {
                var q = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                q.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                var qs = new Vector2(GUI.skin.label.CalcSize(new GUIContent("敌方大本营 ???")).x + 40f, 20f);
                GUI.Label(new Rect(right - qs.x, y - 3f, qs.x, 22f), "敌方大本营 ???", q);
            }
            else
            {
                GUI.Label(new Rect(right - 330f, y - 7f, 110f, 24f), "敌方大本营", label);
                DrawHpBar(new Rect(right - 210f, y, 140f, 14f), snap.Hp, snap.MaxHp);
                var hp = TopBarModel.HpText(snap.Hp, snap.MaxHp);
                var w = GUI.skin.label.CalcSize(new GUIContent(hp)).x + 6f;
                GUI.Label(new Rect(right - w, y - 4f, w, 22f), hp, label);
            }
        }

        private static void DrawHpBar(Rect r, float hp, float maxHp)
        {
            var prev = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            float t = maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f;
            GUI.color = t > 0.5f ? Color.green : (t > 0.25f ? new Color(1f, 0.85f, 0f) : Color.red);
            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * t, r.height - 2f), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
