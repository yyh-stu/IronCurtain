using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>M6.4a 简易结算面板(D10):Phase==Ended 轮询弹出,积分明细 + 再来一局/继续观战。纯 IMGUI,不 Step、不监听事件。</summary>
    public sealed class ResultPanel : MonoBehaviour
    {
        private SimCore _sim;
        private Side _playerSide;
        private bool _dismissed;

        public void Init(SimCore sim, Side playerSide)
        {
            _sim = sim;
            _playerSide = playerSide;
        }

        public static string VerdictText(Side player, SimCore sim)
        {
            if (!sim.Winner.HasValue) return "平局";
            return sim.Winner.Value == player ? "胜利!" : "失败";
        }

        private void OnGUI()
        {
            if (_sim == null) return;
            if (_sim.Phase != MatchPhase.Ended || _dismissed) return;

            // 全屏半透明遮罩挡操作
            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prevColor;

            float x = Screen.width * 0.5f - 280f;
            float y = Screen.height * 0.5f - 170f;
            GUI.Box(new Rect(x, y, 560f, 340f), "");

            // 标题
            var title = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            string verdict = VerdictText(_playerSide, _sim);
            if (!_sim.Winner.HasValue) title.normal.textColor = new Color(1f, 0.95f, 0.5f);
            else title.normal.textColor = _sim.Winner.Value == _playerSide ? Color.green : Color.red;
            GUI.Label(new Rect(x, y + 18f, 560f, 40f), verdict, title);

            float textY = y + 62f;
            if (!_sim.Winner.HasValue)
            {
                var tie = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                tie.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                GUI.Label(new Rect(x, textY, 560f, 20f), "总分、人口、阵亡全同 —— 平局", tie);
                textY += 22f;
            }

            // 积分明细两列
            var headStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            headStyle.normal.textColor = Color.white;
            var rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            rowStyle.normal.textColor = Color.white;

            MatchClockSystem.ScoreOf(_sim, Side.Blue, out int bBase, out int bHero, out int bEp, out int bBld, out int bTotal);
            MatchClockSystem.ScoreOf(_sim, Side.Red, out int rBase, out int rHero, out int rEp, out int rBld, out int rTotal);

            float lx = x + 40f, rx = x + 300f;
            GUI.Label(new Rect(lx, textY, 200f, 24f), (_sim.Winner == Side.Blue ? "★ " : "") + "蓝方", headStyle);
            GUI.Label(new Rect(rx, textY, 200f, 24f), (_sim.Winner == Side.Red ? "★ " : "") + "红方", headStyle);
            textY += 28f;

            GUI.Label(new Rect(lx, textY, 200f, 22f), "基地 5×血 = " + bBase, rowStyle);
            GUI.Label(new Rect(rx, textY, 200f, 22f), "基地 5×血 = " + rBase, rowStyle);
            textY += 24f;
            GUI.Label(new Rect(lx, textY, 200f, 22f), "主将 3×n = " + bHero, rowStyle);
            GUI.Label(new Rect(rx, textY, 200f, 22f), "主将 3×n = " + rHero, rowStyle);
            textY += 24f;
            GUI.Label(new Rect(lx, textY, 200f, 22f), "EP 2×n = " + bEp, rowStyle);
            GUI.Label(new Rect(rx, textY, 200f, 22f), "EP 2×n = " + rEp, rowStyle);
            textY += 24f;
            GUI.Label(new Rect(lx, textY, 200f, 22f), "建筑 1×n = " + bBld, rowStyle);
            GUI.Label(new Rect(rx, textY, 200f, 22f), "建筑 1×n = " + rBld, rowStyle);
            textY += 24f;
            var totalStyle = new GUIStyle(rowStyle) { fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(lx, textY, 200f, 22f), "总分 " + bTotal, totalStyle);
            GUI.Label(new Rect(rx, textY, 200f, 22f), "总分 " + rTotal, totalStyle);

            // 底部按钮
            float by = y + 340f - 48f;
            if (GUI.Button(new Rect(x + 105f, by, 150f, 34f), "再来一局"))
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            if (GUI.Button(new Rect(x + 305f, by, 150f, 34f), "继续观战"))
                _dismissed = true;
        }
    }
}
