using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>M6.4a 结算面板(D10);2026-09-11 制作人定稿改版(模仿决战平安京):
    /// 推掉大本营 → 全屏遮罩 + 中央**大字胜负**(缓入:scale 1.8→1 + 淡入,不生硬),
    /// 随后战绩面板滑入——双方**出场主将**各一行:伤害(对英雄)/承伤/建筑伤害/巷战杀敌
    /// (数据来自 MatchStats,按(方,英雄)聚合,含阵亡将)。再来一局/继续观战与 BGM 署名保留。
    /// UITK 实现;Phase==Ended 轮询口径不变、不 Step、不监听事件。</summary>
    public sealed class ResultPanel : MonoBehaviour
    {
        private SimCore _sim;
        private Side _playerSide;
        private bool _dismissed;
        private float _shownAt = -1f;

        private VisualElement _root, _dim, _panel;
        private Label _title;
        private VisualElement _blueCol, _redCol;

        public void Init(SimCore sim, Side playerSide)
        {
            _sim = sim;
            _playerSide = playerSide;
            if (UiRoot.Current != null && UiRoot.Current.Screen != null) Build(UiRoot.Current.Screen);
        }

        public static string VerdictText(Side player, SimCore sim)
        {
            if (!sim.Winner.HasValue) return "平局";
            return sim.Winner.Value == player ? "胜利!" : "失败";
        }

        private void Build(VisualElement parent)
        {
            _root = new VisualElement { name = "ResultRoot" };
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.right = 0; _root.style.top = 0; _root.style.bottom = 0;
            _root.style.display = DisplayStyle.None;
            _root.pickingMode = PickingMode.Ignore;   // 遮罩不挡(按钮自己挡)

            _dim = new VisualElement();               // 全屏遮罩
            _dim.style.position = Position.Absolute;
            _dim.style.left = 0; _dim.style.right = 0; _dim.style.top = 0; _dim.style.bottom = 0;
            _dim.style.backgroundColor = new Color(0f, 0f, 0f, 0.62f);
            _dim.style.opacity = 0f;
            _dim.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
            _dim.style.transitionDuration = new List<TimeValue> { new TimeValue(0.5f) };
            _root.Add(_dim);

            // 中央大字(缓入:scale 1.8→1 + 淡入)
            _title = new Label("");
            _title.AddToClassList("victory-title");
            _title.style.position = Position.Absolute;
            _title.style.left = 0; _title.style.right = 0;
            _title.style.top = Length.Percent(30);
            _title.style.translate = new Translate(Length.Percent(0), Length.Percent(-50));
            _title.style.scale = new Scale(new Vector2(1.8f, 1.8f));
            _title.style.opacity = 0f;
            var props = new List<StylePropertyName> { new StylePropertyName("scale"), new StylePropertyName("opacity") };
            _title.style.transitionProperty = props;
            _title.style.transitionDuration = new List<TimeValue> { new TimeValue(0.45f), new TimeValue(0.45f) };
            _title.style.transitionTimingFunction = new List<EasingFunction> { EasingMode.EaseOut };
            _root.Add(_title);

            // 战绩面板(整体在下半屏,滑入)
            _panel = new VisualElement();
            _panel.AddToClassList("panel");
            _panel.style.position = Position.Absolute;
            _panel.style.left = Length.Percent(50);
            _panel.style.translate = new Translate(Length.Percent(-50), Length.Percent(0));
            _panel.style.top = Length.Percent(42);
            _panel.style.width = 760;
            _panel.style.opacity = 0f;
            _panel.style.translate = new Translate(Length.Percent(-50), 26);
            _panel.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity"), new StylePropertyName("translate") };
            _panel.style.transitionDuration = new List<TimeValue> { new TimeValue(0.4f), new TimeValue(0.4f) };
            _panel.style.transitionTimingFunction = new List<EasingFunction> { EasingMode.EaseOut };

            var cols = new VisualElement();
            cols.style.flexDirection = FlexDirection.Row;
            _blueCol = MakeSideColumn("蓝方");
            _redCol = MakeSideColumn("红方");
            cols.Add(_blueCol); cols.Add(_redCol);
            _panel.Add(cols);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.Center;
            btnRow.style.marginTop = 10;
            var again = UiKit.MakeButton("再来一局", () => UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));
            again.style.width = 150; again.style.height = 36;
            var watch = UiKit.MakeButton("继续观战", () => { _dismissed = true; });
            watch.style.width = 150; watch.style.height = 36; watch.style.marginLeft = 12;
            btnRow.Add(again); btnRow.Add(watch);
            _panel.Add(btnRow);

            var credit = UiKit.MakeLabel("Music: Netherplace / Tower Defense / Our Mountain by Eric Matyas (soundimage.org)", "label-dim");
            credit.style.fontSize = 11;
            credit.style.marginTop = 6;
            _panel.Add(credit);

            _root.Add(_panel);
            parent.Add(_root);
        }

        private VisualElement MakeSideColumn(string header)
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.paddingLeft = 10; col.style.paddingRight = 10;
            var h = UiKit.MakeLabel(header, "label-title");
            h.style.unityTextAlign = TextAnchor.MiddleCenter;
            col.Add(h);
            return col;
        }

        private static readonly string[] HeroNamesZh =
            { "磐石", "镇岳", "断峰", "逐风", "坠日", "星落" };

        private static string HeroNameZh(HeroId h)
        {
            var arr = new[] { HeroId.PanShi, HeroId.ZhenYue, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
            int i = System.Array.IndexOf(arr, h);
            return i >= 0 ? HeroNamesZh[i] : h.ToString();
        }

        private bool _demoDone;
        private void Update()
        {
            if (_sim == null || _root == null) return;
            // 取证开关 IC_UI_RESULT=1:t15s 强制结束对局(蓝胜)拍结算全流程
            if (!_demoDone && System.Environment.GetEnvironmentVariable("IC_UI_RESULT") == "1"
                && _sim.Phase != MatchPhase.Ended && _sim.Tick > 150)
            {
                _demoDone = true;
                _sim.SetWinner(Side.Blue);
                _sim.Phase = MatchPhase.Ended;
            }
            if (_sim.Phase != MatchPhase.Ended || _dismissed)
            {
                if (_root.resolvedStyle.display != DisplayStyle.None) _root.style.display = DisplayStyle.None;
                return;
            }
            if (_root.style.display == DisplayStyle.None)
            {
                // 首帧出现:先摆好"未入场"状态,下一帧再放动画(UITK transition 需要先布局一帧)
                _shownAt = Time.unscaledTime;
                _title.text = WinnerText();
                _title.style.color = TitleColor();
                FillStats();
                _root.style.display = DisplayStyle.Flex;
                return;
            }
            float t = Time.unscaledTime - _shownAt;
            if (t > 0.05f) _dim.style.opacity = 1f;
            if (t > 0.25f) { _title.style.scale = new Scale(Vector2.one); _title.style.opacity = 1f; }   // 大字缓入
            if (t > 0.85f) { _panel.style.opacity = 1f; _panel.style.translate = new Translate(Length.Percent(-50), 0); } // 面板滑入
        }

        private string WinnerText()
        {
            if (!_sim.Winner.HasValue) return "平局";
            return _sim.Winner.Value == _playerSide ? "胜 利" : "失 败";
        }

        private Color TitleColor()
        {
            if (!_sim.Winner.HasValue) return new Color(1f, 0.95f, 0.5f);
            return _sim.Winner.Value == _playerSide ? new Color(0.42f, 0.95f, 0.55f) : new Color(1f, 0.36f, 0.30f);
        }

        private void FillStats()
        {
            FillSide(_blueCol, Side.Blue, _sim.Winner == Side.Blue);
            FillSide(_redCol, Side.Red, _sim.Winner == Side.Red);
        }

        private void FillSide(VisualElement col, Side side, bool winner)
        {
            for (int i = col.childCount - 1; i >= 1; i--) col.RemoveAt(i);   // 清旧,留标题
            var head = col.ElementAt(0) as Label;
            head.text = (winner ? "★ " : "") + (side == Side.Blue ? "蓝方" : "红方");

            var entries = MatchStats.SideEntries(side);
            if (entries.Count == 0)
            {
                var none = UiKit.MakeLabel("(无出场主将)", "label-dim");
                none.style.marginTop = 4;
                col.Add(none);
                return;
            }
            foreach (var kv in entries)
            {
                var block = new VisualElement();
                block.style.marginTop = 6;
                block.style.paddingTop = 4;
                block.style.borderTopWidth = 1;
                block.style.borderTopColor = new Color(1f, 1f, 1f, 0.10f);
                var name = UiKit.MakeLabel(HeroNameZh(kv.Key), "label-title");
                name.style.fontSize = 15;
                block.Add(name);
                block.Add(StatRow("伤害", kv.Value.HeroDamage.ToString("0")));
                block.Add(StatRow("承伤", kv.Value.DamageTaken.ToString("0")));
                block.Add(StatRow("建筑伤害", kv.Value.BuildingDamage.ToString("0")));
                block.Add(StatRow("巷战杀敌", kv.Value.AlleyKills.ToString()));
                col.Add(block);
            }
        }

        private static VisualElement StatRow(string k, string v)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var kl = UiKit.MakeLabel(k + "：", "label-dim");
            kl.style.width = 96;
            var vl = UiKit.MakeLabel(v);
            row.Add(kl); row.Add(vl);
            return row;
        }
    }
}
