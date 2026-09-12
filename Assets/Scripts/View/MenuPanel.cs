using System;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>主界面 UI(2026-09-12 制作人简化:大厅不再展示英雄,只留大本营场景)。
    /// 现在只剩:顶部标题 + 左上头像占位 + 右列入口(商城/活动占位 + AI 两档 + 练习场)。
    /// 英雄图鉴与展示台已按制作人指令移除(连带去掉了一批布局/挂点问题)。</summary>
    public sealed class MenuPanel : MonoBehaviour
    {
        /// <summary>构建 UI(不再需要英雄切换回调)。</summary>
        public void Init()
        {
            if (UiRoot.Current == null || UiRoot.Current.Screen == null) return;
            var root = UiRoot.Current.Screen;

            // 顶部中央:游戏标题(绝对定位 + 显式高度:零高盒子会把文字顶出画面)
            var title = UiKit.MakeLabel("铁 幕 :前 线", "victory-title");
            title.style.position = Position.Absolute;
            title.style.top = 46; title.style.left = 0; title.style.right = 0;
            title.style.height = 108f;
            title.style.fontSize = 64f;
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
        }

        private static Button MakeEntry(string text, Action onClick)
        {
            var b = onClick != null ? UiKit.MakeButton(text, onClick) : UiKit.MakeButton(text, null, false);
            b.style.height = 44;
            b.style.marginTop = 4;
            b.style.fontSize = 16;
            return b;
        }
    }
}
