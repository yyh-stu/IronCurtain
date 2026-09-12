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

        private VisualElement _root, _dim, _panel, _sheet;
        private Label _title, _sheetTitle;
        private Image _logo;
        private VisualElement _blueCol, _redCol;

        private static Texture2D _lgWin, _lgLose, _lgDraw;
        /// <summary>胜负美术大字(2026-09-12 串联改造:参考视频"胜利二字为美术图");缺图回退纯色文字。</summary>
        private Texture2D LogoTexture()
        {
            if (!_sim.Winner.HasValue) return _lgDraw != null ? _lgDraw : (_lgDraw = UnityEngine.Resources.Load<Texture2D>("UI/draw_logo"));
            if (_sim.Winner.Value == _playerSide) return _lgWin != null ? _lgWin : (_lgWin = UnityEngine.Resources.Load<Texture2D>("UI/victory_logo"));
            return _lgLose != null ? _lgLose : (_lgLose = UnityEngine.Resources.Load<Texture2D>("UI/defeat_logo"));
        }

        public void Init(SimCore sim, Side playerSide)
        {
            _sim = sim;
            _playerSide = playerSide;
            if (UiRoot.Current != null && UiRoot.Current.Screen != null) Build(UiRoot.Current.Screen);
        }

        /// <summary>取证/排障用:面板当前关键状态一行(显示/透明度/大字/包围盒+布局链)。</summary>
        public string DebugState()
        {
            if (_root == null) return "result:no-root";
            var scr = UiRoot.Current != null ? UiRoot.Current.Screen : null;
            var rve = scr != null && scr.parent != null ? scr.parent : null;
            return string.Format(
                "result:disp={0} dim={1:F2} titleOp={2:F2} title=[{3}] panelOp={4:F2} box={5:F0}x{6:F0}" +
                " | screen={7:F0}x{8:F0} rve={9:F0}x{10:F0} screenKids={11}",
                _root.resolvedStyle.display, _dim.resolvedStyle.opacity, _title.resolvedStyle.opacity,
                _title.text, _panel.resolvedStyle.opacity, _root.worldBound.width, _root.worldBound.height,
                scr != null ? scr.worldBound.width : -1f, scr != null ? scr.worldBound.height : -1f,
                rve != null ? rve.worldBound.width : -1f, rve != null ? rve.worldBound.height : -1f,
                scr != null ? scr.childCount : -1);
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

            // 美术大字层(有图时用它替掉纯色文字;两段缓入:先大字后面板,对齐参考视频的转场节奏)
            _logo = new Image();
            _logo.style.position = Position.Absolute;
            _logo.style.left = Length.Percent(50);
            _logo.style.top = Length.Percent(30);
            _logo.style.width = 820; _logo.style.height = 461;
            _logo.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            _logo.style.scale = new Scale(new Vector2(1.18f, 1.18f));
            _logo.style.opacity = 0f;
            _logo.style.display = DisplayStyle.None;
            _logo.scaleMode = ScaleMode.ScaleToFit;
            _logo.pickingMode = PickingMode.Ignore;
            _logo.style.transitionProperty = props;
            _logo.style.transitionDuration = new List<TimeValue> { new TimeValue(0.55f), new TimeValue(0.55f) };
            _logo.style.transitionTimingFunction = new List<EasingFunction> { EasingMode.EaseOut };
            _root.Add(_logo);

            // 2026-09-12 制作人要求:胜利大字独立一段(战场仍可见),随后切到**单独的结算画面**(不透明底),
            // 结算画面底部给"返回大厅"。_sheet = 全屏不透明底板,第二段才出现。
            _sheet = new VisualElement();
            _sheet.style.position = Position.Absolute;
            _sheet.style.left = 0; _sheet.style.right = 0; _sheet.style.top = 0; _sheet.style.bottom = 0;
            _sheet.style.backgroundColor = new Color(0.055f, 0.070f, 0.098f, 1f);
            _sheet.style.opacity = 0f;
            _sheet.style.display = DisplayStyle.None;
            _sheet.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
            _sheet.style.transitionDuration = new List<TimeValue> { new TimeValue(0.30f) };
            _root.Add(_sheet);

            _sheetTitle = UiKit.MakeLabel("对局结算", "victory-title");
            _sheetTitle.style.position = Position.Absolute;
            _sheetTitle.style.left = 0; _sheetTitle.style.right = 0;
            _sheetTitle.style.top = 26; _sheetTitle.style.height = 108f;
            _sheetTitle.style.fontSize = 56f;
            _sheetTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _sheet.Add(_sheetTitle);

            // 战绩面板(整体在下半屏,滑入)
            _panel = new VisualElement();
            _panel.AddToClassList("panel");
            _panel.style.position = Position.Absolute;
            _panel.style.left = Length.Percent(50);
            _panel.style.translate = new Translate(Length.Percent(-50), Length.Percent(0));
            _panel.style.top = Length.Percent(30);
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
            var again = MakeResultButton("再来一局", () => UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));
            again.style.width = 150; again.style.height = 36;
            var watch = MakeResultButton("继续观战", () => { _dismissed = true; });
            watch.style.width = 150; watch.style.height = 36; watch.style.marginLeft = 12;
            btnRow.Add(again); btnRow.Add(watch);
            _panel.Add(btnRow);

            var credit = UiKit.MakeLabel("Music: Netherplace / Tower Defense / Our Mountain by Eric Matyas (soundimage.org)", "label-dim");
            credit.style.fontSize = 11;
            credit.style.marginTop = 6;
            _panel.Add(credit);

            _sheet.Add(_panel);

            // 结算画面底部:返回大厅(主 CTA,2026-09-12 制作人指定)
            var backRow = new VisualElement();
            backRow.style.position = Position.Absolute;
            backRow.style.left = 0; backRow.style.right = 0; backRow.style.bottom = 60;
            backRow.style.justifyContent = Justify.Center;
            backRow.style.flexDirection = FlexDirection.Row;
            var toHall = MakeResultButton("返回大厅", () => AppFlow.EnterMenu());
            toHall.style.width = 240; toHall.style.height = 52;
            toHall.AddToClassList("btn-accent");
            backRow.Add(toHall);
            _sheet.Add(backRow);

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
                var tex = LogoTexture();
                if (tex != null)
                {
                    _logo.image = tex;
                    _logo.style.display = DisplayStyle.Flex;
                    _title.style.display = DisplayStyle.None;   // 有美术字就不显示纯色文字
                }
                else
                {
                    _logo.style.display = DisplayStyle.None;
                    _title.style.display = DisplayStyle.Flex;
                }
                FillStats();
                _sheetTitle.text = WinnerText();
                _sheetTitle.style.color = TitleColor();
                _root.BringToFront();   // 结算遮罩/底板要盖住其余面板(小地图等),否则边角会漏出来
                _root.style.display = DisplayStyle.Flex;
                return;
            }
            float t = Time.unscaledTime - _shownAt;
            if (t > 0.05f) _dim.style.opacity = 1f;
            // 第一段:胜利/失败美术大字入场(战场仍可见)
            if (t > 0.18f) { _logo.style.scale = new Scale(Vector2.one); _logo.style.opacity = 1f; }
            if (t > 0.25f) { _title.style.scale = new Scale(Vector2.one); _title.style.opacity = 1f; }
            // 第二段:切到单独结算画面(不透明底),大字淡出、卡片面板滑入
            if (t > 1.55f)
            {
                _logo.style.opacity = 0f;
                if (_sheet.style.display != DisplayStyle.Flex) _sheet.style.display = DisplayStyle.Flex;
                _sheet.style.opacity = 1f;
            }
            if (t > 1.90f) { _panel.style.opacity = 1f; _panel.style.translate = new Translate(Length.Percent(-50), 0); }
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
            // 2026-09-12 平安京式卡片:头像 + 名字 + MVP 评分 + 四项战绩;按评分降序
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<HeroId, MatchStats.Entry>>(entries);
            list.Sort((a, b) => Score(b.Value).CompareTo(Score(a.Value)));
            bool hasMvp = list.Count > 0;
            var mvp = hasMvp ? list[0].Key : default(HeroId);
            foreach (var kv in list)
            {
                var card = new VisualElement();
                card.AddToClassList("panel");
                card.style.flexDirection = FlexDirection.Row;
                card.style.marginTop = 6;
                card.style.paddingTop = 5; card.style.paddingBottom = 5;
                card.style.paddingLeft = 7; card.style.paddingRight = 7;
                card.style.alignItems = Align.Center;

                var por = new Image();
                var tex = UnityEngine.Resources.Load<Texture2D>("UI/portraits/" + kv.Key);
                if (tex != null) por.image = tex;
                por.style.width = 48; por.style.height = 48;
                por.style.marginRight = 8;
                card.Add(por);

                var info = new VisualElement();
                info.style.flexGrow = 1;
                var nameRow = new VisualElement();
                nameRow.style.flexDirection = FlexDirection.Row;
                nameRow.style.alignItems = Align.Center;
                var name = UiKit.MakeLabel(HeroNameZh(kv.Key), "label-title");
                name.style.fontSize = 16;
                nameRow.Add(name);
                if (hasMvp && kv.Key == mvp)
                {
                    var badge = UiKit.MakeLabel("MVP " + Score(kv.Value).ToString("0.0"), "label-title");
                    badge.style.fontSize = 12;
                    badge.style.color = new Color(1f, 0.84f, 0.35f);
                    badge.style.marginLeft = 8;
                    nameRow.Add(badge);
                }
                info.Add(nameRow);

                var r1 = new VisualElement(); r1.style.flexDirection = FlexDirection.Row;
                r1.Add(Pair("伤害", kv.Value.HeroDamage.ToString("0")));
                r1.Add(Pair("承伤", kv.Value.DamageTaken.ToString("0")));
                info.Add(r1);
                var r2 = new VisualElement(); r2.style.flexDirection = FlexDirection.Row;
                r2.Add(Pair("建筑", kv.Value.BuildingDamage.ToString("0")));
                r2.Add(Pair("巷战", kv.Value.AlleyKills.ToString()));
                info.Add(r2);

                card.Add(info);
                col.Add(card);
            }
        }

        /// <summary>结算按钮(2026-09-12):本面板里 Button 自带的 text 不渲染(菜单同法可渲染,成因未定),
        /// 改为显式挂 Label 子元素承载文案——与面板内其他 Label 同一条字体链,实测可渲染。</summary>
        private static Button MakeResultButton(string text, System.Action onClick)
        {
            var b = UiKit.MakeButton("", onClick);
            var l = UiKit.MakeLabel(text);
            l.pickingMode = PickingMode.Ignore;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.position = Position.Absolute;
            l.style.left = 0; l.style.right = 0; l.style.top = 0; l.style.bottom = 0;
            l.style.fontSize = 16;
            b.Add(l);
            return b;
        }

        /// <summary>MVP 评分(平安京式单项分):对英雄伤害/承伤/对建筑伤害/巷战杀敌加权。</summary>
        private static float Score(MatchStats.Entry e)
        {
            return e.HeroDamage * 0.6f + e.DamageTaken * 0.15f + e.BuildingDamage * 0.3f + e.AlleyKills * 60f;
        }

        private static VisualElement Pair(string k, string v)
        {
            var w = new VisualElement();
            w.style.flexDirection = FlexDirection.Row;
            w.style.marginRight = 12;
            var kl = UiKit.MakeLabel(k + " ", "label-dim");
            kl.style.fontSize = 12;
            w.Add(kl);
            var vl = UiKit.MakeLabel(v);
            vl.style.fontSize = 13;
            w.Add(vl);
            return w;
        }
    }
}
