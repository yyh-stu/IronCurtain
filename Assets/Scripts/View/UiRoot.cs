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

        private VisualElement _rve;
        private bool _heightPatched;

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

            _rve = doc.rootVisualElement;

            // 2026-09-12 字体硬保障:无值守抓帧实测默认字体(Inter)+OS 回退链不光栅化 CJK 字形
            // (按钮/标题零白像素,交互会话正常)。显式创建微软雅黑动态 FontAsset 直挂根,
            // 不再依赖回退链;对打包版同样是去 OS 字体依赖的加固。失败时退回默认链不阻断。
            try
            {
                var osFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16);
                var fa = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(osFont);
                if (fa != null)
                {
                    _rve.style.unityFontDefinition = new StyleFontDefinition(fa);
                    Debug.Log("[UiRoot] CJK font asset bound (Microsoft YaHei)");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[UiRoot] CJK font bind failed (fallback chain in use): " + ex.Message);
            }

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

        private void Update()
        {
            // 2026-09-12 布局坑修复:实测(结算取证探针)rootVisualElement 宽度正常(缩放后面板宽)
            // 但 resolvedStyle.height==0 —— 所有依赖父容器高度的面板(结算全屏遮罩/百分比布局)全部塌陷。
            // 旧面板全用显式 top+height,overflow 可见,故从未暴露。按宽度缩放比显式补高度,命中一次即可。
            // 注意布局未就绪时 resolvedStyle 为 NaN,NaN 与任何数比较均为 false,必须显式排除。
            if (_rve == null || _heightPatched) return;
            float h = _rve.resolvedStyle.height;
            if (float.IsNaN(h)) return;               // 布局未就绪,下帧再看
            if (h >= 1f) { _heightPatched = true; return; }
            float w = _rve.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 1f) return;
            if (UnityEngine.Screen.width <= 0) return;
            float scale = w / UnityEngine.Screen.width;
            if (float.IsNaN(scale) || scale < 0.01f || scale > 100f) return;
            float target = UnityEngine.Screen.height * scale;
            if (float.IsNaN(target) || target < 1f) return;
            _rve.style.height = target;
            _heightPatched = true;
            Debug.Log("[UiRoot] panel height patched to " + target.ToString("F0") + " (was 0, scale=" + scale.ToString("F3") + ")");
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
            // 2026-09-12 程序化 UI 音:所有按钮点击统一配音(拍板 A+C 之 C)
            System.Action wrapped = onClick;
            if (onClick != null) wrapped = () => { UiSfx.Click(); onClick(); };
            else if (enabled) wrapped = UiSfx.Click;
            var b = new Button(wrapped) { text = text };
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
