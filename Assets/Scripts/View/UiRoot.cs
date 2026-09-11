using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>
    /// UI Toolkit 根(2026-09-11 UI demo→成品迁移第 0 步):
    /// - PanelSettings/主题从 Resources 加载(由 GenerateUiAssets 编辑器生成——运行时 CreateInstance
    ///   会因缺序列化初始化抛 ArgumentOutOfRangeException,实测并连锁打断 GameBootstrap.Awake);
    /// - 面板纯代码构建 VisualElement 挂到 Screen 容器(含安全区),不走 UXML;
    /// - **输入拦截桥**:各 UITK 面板把 worldBound 回填自己的 LastPanelRect 静态字段,
    ///   SelectionInteractionCoordinator.IsPointerOverUi 的矩形判定链**一行不改**继续生效。
    /// IMGUI 面板迁移期间与其并存,逐批替换直至清零。
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        public static UiRoot Current { get; private set; }
        public VisualElement Screen { get; private set; }
        /// <summary>右侧列(升级/海克斯等入口面板):自动堆叠,展开互不遮挡。</summary>
        public VisualElement Right { get; private set; }

        private void Awake()
        {
            if (Current != null && Current != this) { Destroy(this); return; }
            Current = this;

            var settings = Resources.Load<PanelSettings>("UI/PanelSettings");
            if (settings == null)
            {
                Debug.LogError("[UiRoot] Resources/UI/PanelSettings.asset 缺失(先跑 IronCurtain/Generate UI Assets),UI 不加载");
                enabled = false;
                return;
            }

            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = settings;

            var theme = Resources.Load<StyleSheet>("UI/theme");
            if (theme != null) doc.rootVisualElement.styleSheets.Add(theme);
            else Debug.LogWarning("[UiRoot] Resources/UI/theme.uss 缺失,UI 将无主题");

            Screen = new VisualElement { name = "Screen" };
            Screen.style.position = Position.Absolute;
            Screen.style.left = 0; Screen.style.right = 0; Screen.style.top = 0; Screen.style.bottom = 0;
            doc.rootVisualElement.Add(Screen);

            Right = new VisualElement { name = "RightColumn" };
            Right.style.position = Position.Absolute;
            Right.style.right = 12;
            Right.style.top = 56;
            Right.style.width = 328;
            Right.style.flexDirection = FlexDirection.Column;
            Screen.Add(Right);
        }

        private void Start()
        {
            if (Screen == null) return;
            // 安全区(带鱼屏刘海/系统栏):按屏幕安全区留白(Screen 属性遮蔽了 UnityEngine.Screen,须全限定)
            var sa = UnityEngine.Screen.safeArea;
            if (sa.x > 0 || sa.y > 0 || sa.width < UnityEngine.Screen.width || sa.height < UnityEngine.Screen.height)
            {
                Screen.style.paddingLeft = sa.x;
                Screen.style.paddingRight = UnityEngine.Screen.width - sa.xMax;
                Screen.style.paddingTop = UnityEngine.Screen.height - sa.yMax;
                Screen.style.paddingBottom = sa.y;
            }
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }
    }

    /// <summary>UITK 控件工厂与工具(主题类名集中在 theme.uss)。</summary>
    public static class UiKit
    {
        public static Button MakeButton(string text, System.Action onClick, bool enabled = true)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("btn");
            b.SetEnabled(enabled);
            return b;
        }

        public static Label MakeLabel(string text, string cls = "label")
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return l;
        }

        /// <summary>元素当前屏幕矩形(IMGUI 兼容口径:输入拦截桥用)。</summary>
        public static Rect WorldRect(VisualElement e)
        {
            var b = e.worldBound;
            return new Rect(b.xMin, b.yMin, b.width, b.height);
        }
    }
}
