using System;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>主界面 UI(2026-09-12 制作人定稿):左上头像占位、左列英雄选择(6 英雄自选切换,
    /// 纯展示)、右列入口(商城/活动占位 + AI 对战简单/困难 + 练习场)。UITK 纯代码构建,复用 UiKit/theme。</summary>
    public sealed class MenuPanel : MonoBehaviour
    {
        private static readonly HeroId[] Heroes =
            { HeroId.PanShi, HeroId.ZhenYue, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
        private static readonly string[] HeroNames = { "磐石", "镇岳", "断峰", "逐风", "坠日", "星落" };
        private static readonly string[] HeroTitles =
            { "重装·磐石之壁", "重装·镇岳铁军", "战士·断峰之矛", "骑兵·逐风长枪", "射手·坠日之弓", "法师·星落之杖" };

        private Label _heroName;
        private Label _heroTitle;
        private VisualElement _heroRow;
        private readonly Color _pickOn = new Color(1f, 0.85f, 0.35f);

        /// <summary>英雄切换回调(由 MenuBootstrap 注入,驱动 MenuStage.Show)。</summary>
        public void Init(Action<HeroId> onHeroPick, HeroId initial)
        {
            if (UiRoot.Current == null || UiRoot.Current.Screen == null) return;
            var root = UiRoot.Current.Screen;

            // 顶部中央:游戏标题
            var title = UiKit.MakeLabel("铁 幕 :前 线", "victory-title");
            title.style.position = Position.Absolute;
            title.style.top = 26; title.style.left = 0; title.style.right = 0;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.Add(title);

            // 左上:头像占位(制作人定稿:占位,不做系统)
            var avatar = new VisualElement();
            avatar.style.position = Position.Absolute;
            avatar.style.top = 16; avatar.style.left = 16;
            avatar.style.width = 76; avatar.style.height = 76;
            avatar.style.borderTopLeftRadius = 38; avatar.style.borderTopRightRadius = 38;
            avatar.style.borderBottomLeftRadius = 38; avatar.style.borderBottomRightRadius = 38;
            avatar.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            avatar.style.borderTopWidth = 2; avatar.style.borderBottomWidth = 2;
            avatar.style.borderLeftWidth = 2; avatar.style.borderRightWidth = 2;
            avatar.style.borderTopColor = new Color(1f, 1f, 1f, 0.5f);
            avatar.style.borderBottomColor = new Color(1f, 1f, 1f, 0.5f);
            avatar.style.borderLeftColor = new Color(1f, 1f, 1f, 0.5f);
            avatar.style.borderRightColor = new Color(1f, 1f, 1f, 0.5f);
            var avLabel = UiKit.MakeLabel("头像\n占位", "label-dim");
            avLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            avLabel.style.position = Position.Absolute;
            avLabel.style.top = 0; avLabel.style.bottom = 0;
            avLabel.style.left = 0; avLabel.style.right = 0;
            avatar.Add(avLabel);
            root.Add(avatar);

            // 右列:商城/活动占位 + 对战入口(制作人定稿布局)
            var right = new VisualElement();
            right.style.position = Position.Absolute;
            right.style.right = 24;
            right.style.top = Length.Percent(24);
            right.style.width = 250;
            right.style.flexDirection = FlexDirection.Column;
            var panel = new VisualElement();
            panel.AddToClassList("panel");
            panel.style.paddingTop = 14; panel.style.paddingBottom = 14;
            panel.style.paddingLeft = 14; panel.style.paddingRight = 14;

            panel.Add(MakeEntry("商 城(未开放)", null));
            panel.Add(MakeEntry("活 动(未开放)", null));
            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.backgroundColor = new Color(1f, 1f, 1f, 0.2f);
            divider.style.marginTop = 8; divider.style.marginBottom = 8;
            panel.Add(divider);
            panel.Add(MakeEntry("AI 对战 · 简单", () => AppFlow.StartAi(false)));
            panel.Add(MakeEntry("AI 对战 · 困难", () => AppFlow.StartAi(true)));
            panel.Add(MakeEntry("练 习 场(无 AI · 可换边)", () => AppFlow.StartPractice()));
            right.Add(panel);
            root.Add(right);

            // 左下:英雄选择列(点击切换展示台英雄)
            var left = new VisualElement();
            left.style.position = Position.Absolute;
            left.style.left = 24;
            left.style.bottom = 24;
            left.style.width = 250;
            left.style.flexDirection = FlexDirection.Column;
            var heroPanel = new VisualElement();
            heroPanel.AddToClassList("panel");
            heroPanel.style.paddingTop = 12; heroPanel.style.paddingBottom = 12;
            heroPanel.style.paddingLeft = 12; heroPanel.style.paddingRight = 12;
            var tag = UiKit.MakeLabel("英 雄 图 鉴(点击切换展示)", "label-dim");
            heroPanel.Add(tag);
            _heroName = UiKit.MakeLabel("", "label-title");
            _heroName.style.fontSize = 22;
            _heroName.style.marginTop = 4;
            heroPanel.Add(_heroName);
            _heroTitle = UiKit.MakeLabel("", "label-dim");
            _heroTitle.style.marginTop = 2; _heroTitle.style.marginBottom = 8;
            heroPanel.Add(_heroTitle);
            _heroRow = new VisualElement();
            _heroRow.style.flexDirection = FlexDirection.Row;
            _heroRow.style.flexWrap = Wrap.Wrap;
            heroPanel.Add(_heroRow);
            for (int i = 0; i < Heroes.Length; i++)
            {
                int idx = i;
                var b = UiKit.MakeButton(HeroNames[i], () =>
                {
                    MarkPicked(idx);
                    if (onHeroPick != null) onHeroPick(Heroes[idx]);
                });
                b.style.width = 68; b.style.height = 34;
                b.style.marginRight = 6; b.style.marginTop = 6;
                _heroRow.Add(b);
            }
            left.Add(heroPanel);
            root.Add(left);

            MarkPicked(Array.IndexOf(Heroes, initial) < 0 ? 1 : Array.IndexOf(Heroes, initial));
        }

        private void MarkPicked(int idx)
        {
            for (int i = 0; i < _heroRow.childCount; i++)
            {
                var b = _heroRow[i] as Button;
                if (b == null) continue;
                bool on = i == idx;
                b.style.borderTopColor = on ? _pickOn : new Color(1f, 1f, 1f, 0f);
                b.style.borderBottomColor = on ? _pickOn : new Color(1f, 1f, 1f, 0f);
                b.style.borderLeftColor = on ? _pickOn : new Color(1f, 1f, 1f, 0f);
                b.style.borderRightColor = on ? _pickOn : new Color(1f, 1f, 1f, 0f);
                b.style.borderTopWidth = on ? 2 : 0;
                b.style.borderBottomWidth = on ? 2 : 0;
                b.style.borderLeftWidth = on ? 2 : 0;
                b.style.borderRightWidth = on ? 2 : 0;
            }
            _heroName.text = HeroNames[idx];
            _heroTitle.text = HeroTitles[idx];
        }

        private static Button MakeEntry(string text, Action onClick)
        {
            var b = onClick != null ? UiKit.MakeButton(text, onClick) : UiKit.MakeButton(text, null, false);
            b.style.height = 44;
            b.style.fontSize = 16;
            b.style.marginBottom = 6;
            return b;
        }
    }
}
