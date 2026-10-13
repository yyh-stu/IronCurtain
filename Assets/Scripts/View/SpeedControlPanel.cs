using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>
    /// 对局倍速控件(2026-09-17 制作人拍板:玩家可主动增减的倍速 ×1/×2/×4/×8)。
    /// 驻顶栏右侧一行三键:[−] ×N [+],点住语义 = 在 1→2→4→8 档位间步进(到头封顶/保底)。
    /// 只写 GameBootstrap.Speed(仿真步进倍率,暂停时步进本就为零);不碰 Sim、不拦其它输入。
    /// 实机自检读静态 Latest 供判据(档位文本与实际 Speed 一致性)。
    /// </summary>
    public sealed class SpeedControlPanel : MonoBehaviour
    {
        private static readonly int[] Steps = { 1, 2, 4, 8 };

        private GameBootstrap _boot;
        private Label _label;
        private VisualElement _root;

        /// <summary>实机自检读取口:当前显示档位(初始化前为 0)。</summary>
        public static int Latest { get; private set; }

        public void Init(GameBootstrap boot)
        {
            _boot = boot;
            var screen = UiRoot.Current != null ? UiRoot.Current.Screen : null;
            if (screen == null) return;
            _root = new VisualElement { name = "SpeedControl" };
            _root.style.position = Position.Absolute;
            _root.style.top = 50;                    // 顶栏(高 44)之下、右上角
            _root.style.right = 12;
            _root.style.flexDirection = FlexDirection.Row;
            _root.style.alignItems = Align.Center;
            _root.style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 0.72f);
            _root.style.paddingLeft = 6; _root.style.paddingRight = 6;
            _root.style.paddingTop = 2; _root.style.paddingBottom = 2;
            _root.style.borderTopLeftRadius = 6; _root.style.borderTopRightRadius = 6;
            _root.style.borderBottomLeftRadius = 6; _root.style.borderBottomRightRadius = 6;

            var slower = UiKit.MakeButton("−", () => Step(-1));
            slower.style.width = 30; slower.style.height = 24;
            _root.Add(slower);
            _label = UiKit.MakeLabel("×1", "label");
            _label.style.width = 34;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _root.Add(_label);
            var faster = UiKit.MakeButton("+", () => Step(1));
            faster.style.width = 30; faster.style.height = 24;
            _root.Add(faster);

            screen.Add(_root);
        }

        private void Step(int dir)
        {
            if (_boot == null) return;
            int cur = Mathf.RoundToInt(Mathf.Clamp(_boot.Speed, 1f, 8f));
            int idx = 0;
            for (int i = 0; i < Steps.Length; i++)
                if (Steps[i] <= cur) idx = i;              // 当前不落档(如截图脚本的 3/6)时取不大于它的档
            idx = Mathf.Clamp(idx + dir, 0, Steps.Length - 1);
            _boot.Speed = Steps[idx];
            SyncLabel();
#if UNITY_EDITOR
            InputAudit.Log("speed_control set=" + Steps[idx]);
#endif
        }

        private void SyncLabel()
        {
            if (_boot == null || _label == null) return;
            int v = Mathf.RoundToInt(_boot.Speed);
            _label.text = "×" + v;
            Latest = v;
        }

        private void Update()
        {
            // 自愈:截图脚本等外部改写 Speed 后,标签下一帧跟上
            if (_label != null && _boot != null)
            {
                int v = Mathf.RoundToInt(_boot.Speed);
                if (Latest != v) SyncLabel();
            }
        }

        private void OnDisable() => Hide();
        private void OnDestroy() => Hide();
        private void Hide()
        {
            Latest = 0;
            if (_root != null) _root.style.display = DisplayStyle.None;
        }
    }
}
