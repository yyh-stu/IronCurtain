using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>
    /// 2026-09-14 制作人:"当游戏中**没有需要 esc 关闭的东西**的时候,按下 esc → 大字:游戏暂停
    /// (仅 ai、训练场生效),下面还要一个按钮:**返回大厅**"。
    ///
    /// 生效范围天然只在对局场景:这个浮层由 `GameBootstrap.BuildView` 创建,菜单/BP 场景根本没有它。
    /// ESC 由 `UiRoot` **统一路由**(先问各面板有没有要关的东西,没人接手才落到这里),
    /// 所以"关中央面板/关建筑面板"与"弹暂停"不会互相打架。再按一次 ESC = 继续。
    /// 暂停走既有的 `GameBootstrap.Paused`(它本来就 gate 住 Sim.Step 与视图 Tick)。
    /// </summary>
    public sealed class PauseOverlayPanel : MonoBehaviour
    {
        /// <summary>全屏遮罩:登记进输入拦截链(见 SelectionInteractionCoordinator),暂停期间不误发战场指令。</summary>
        public static Rect LastPanelRect = Rect.zero;

        private GameBootstrap _boot;
        private VisualElement _root;
        private VisualElement _backdrop;
        private bool _paused;

        public bool IsPaused { get { return _paused; } }

        public void Init(GameBootstrap boot)
        {
            _boot = boot;
            var ui = UiRoot.Current;
            if (ui == null || ui.Screen == null) return;
            Build(ui.Screen);
            // ESC 的**兜底**消费者:没有任何面板接手时才轮到这里(见 UiRoot.HandleEscPressed)
            ui.SetEscFallback(HandleEsc);
        }

        private void Build(VisualElement parent)
        {
            _root = new VisualElement { name = "PauseOverlay" };
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.right = 0; _root.style.top = 0; _root.style.bottom = 0;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            _root.style.display = DisplayStyle.None;
            _root.pickingMode = PickingMode.Ignore;

            // 半透明深色底(全屏吃指针,避免暂停时误点战场)
            _backdrop = new VisualElement { name = "PauseBackdrop" };
            _backdrop.style.position = Position.Absolute;
            _backdrop.style.left = 0; _backdrop.style.right = 0; _backdrop.style.top = 0; _backdrop.style.bottom = 0;
            _backdrop.style.backgroundColor = new Color(0.03f, 0.05f, 0.08f, 0.72f);
            _root.Add(_backdrop);

            var box = new VisualElement();
            box.style.alignItems = Align.Center;
            box.style.justifyContent = Justify.Center;
            _root.Add(box);

            var title = UiKit.MakeLabel("游戏暂停", "label-title");
            title.style.fontSize = 64;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.97f, 0.98f, 1f);
            box.Add(title);

            var hint = UiKit.MakeLabel("再按 ESC 继续", "label-dim");
            hint.style.marginTop = 6;
            box.Add(hint);

            var back = UiKit.MakeButton("返回大厅", ReturnToLobby);
            back.style.marginTop = 18;
            back.style.width = 200;
            back.style.height = 44;
            box.Add(back);

            parent.Add(_root);
        }

        private void ReturnToLobby()
        {
            SetPaused(false);
            AppFlow.EnterMenu();
        }

        /// <summary>ESC 兜底:暂停/继续(兜底是最后一道,不再需要"是否消费"的返回)。</summary>
        public void HandleEsc()
        {
            SetPaused(!_paused);
        }

        public void SetPaused(bool on)
        {
            _paused = on;
            if (_root != null) _root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            // 暂停 = 既有 GameBootstrap.Paused(Sim.Step 与视图 Tick 都停);恢复对局状态不动
            if (_boot != null) _boot.Paused = on;
#if UNITY_EDITOR
            InputAudit.Log("pause paused=" + on);
#endif
        }

        private void Update()
        {
            // 全屏遮罩参与输入拦截(暂停时战场点击无效);2026-09-15:仍要真的挂着可见才算,
            // 否则暂停态残留会把整屏输入吞掉(和"点敌建/框选失灵"同一类幽灵矩形)。
            LastPanelRect = _paused ? UiKit.VisibleRectOrZero(_root) : Rect.zero;
        }

        private void OnDisable() { LastPanelRect = Rect.zero; }
        private void OnDestroy()
        {
            LastPanelRect = Rect.zero;
            if (_boot != null) _boot.Paused = false;   // 换边重建/退场时别把暂停状态留下
        }
    }
}
