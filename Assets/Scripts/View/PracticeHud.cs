using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>练习场专用 HUD(2026-09-12):顶部中央显示当前操控阵营 + 重载式换边按钮。
    /// 仅练习场(无 AI)挂载;点击后翻转 AppFlow.PlayerSide 并重载战斗场景。</summary>
    public sealed class PracticeHud : MonoBehaviour
    {
        private Label _sideLabel;

        public void Init(Side player)
        {
            if (UiRoot.Current == null || UiRoot.Current.Screen == null) return;
            var root = UiRoot.Current.Screen;

            var box = new VisualElement();
            box.AddToClassList("panel");
            box.style.position = Position.Absolute;
            box.style.top = 8;
            box.style.left = Length.Percent(50);
            box.style.translate = new Translate(Length.Percent(-50), 0);
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignItems = Align.Center;
            box.style.paddingTop = 6; box.style.paddingBottom = 6;
            box.style.paddingLeft = 10; box.style.paddingRight = 10;

            var tag = UiKit.MakeLabel("练习场·操控：", "label-dim");
            _sideLabel = UiKit.MakeLabel(player == Side.Blue ? "蓝方" : "红方", "label-title");
            _sideLabel.style.marginRight = 10;
            _sideLabel.style.color = player == Side.Blue
                ? new Color(0.45f, 0.75f, 1f) : new Color(1f, 0.55f, 0.45f);
            var btn = UiKit.MakeButton("切换操控", () =>
            {
                Debug.Log("[PracticeHud] switch side from " + AppFlow.PlayerSide);
                AppFlow.SwitchPracticeSide();
            });
            btn.style.width = 150; btn.style.height = 30;

            box.Add(tag); box.Add(_sideLabel); box.Add(btn);
            root.Add(box);
        }
    }
}
