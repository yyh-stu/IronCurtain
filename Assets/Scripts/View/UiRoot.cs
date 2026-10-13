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
        /// <summary>2026-09-13:底部居中的入口条(各命令面板折叠态并排在这里)。</summary>
        public VisualElement BottomBar { get; private set; }
        /// <summary>2026-09-13:屏幕中央宿主(命令面板展开态显示在这里)。</summary>
        public VisualElement Center { get; private set; }

        /// <summary>2026-09-15 制作人口径:按钮点开的面板**尺寸定死**(内容超出内部滚动,不再随内容变大小)。</summary>
        public const float PanelWidth = 560f;
        public const float PanelHeight = 520f;

        /// <summary>最近一次打开的中央框矩形(实机自检:四个面板尺寸必须一致且定死)。</summary>
        public static Rect LastCenterRect;
        private bool _centerFrameDirty;


        private VisualElement _rve;
        private UIDocument _doc;
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
            _doc = doc;

            var theme = Resources.Load<StyleSheet>("UI/theme");
            if (theme != null) doc.rootVisualElement.styleSheets.Add(theme);
            else Debug.LogWarning("[UiRoot] Resources/UI/theme.uss 缺失,UI 将无主题");

            _rve = doc.rootVisualElement;

            // 2026-09-12 字体硬保障:无值守抓帧实测默认字体(Inter)+OS 回退链不光栅化 CJK 字形
            // (按钮/标题零白像素,交互会话正常)。显式创建中文字体 FontAsset 直挂根。
            // 2026-09-13 加强:原来只试 "Microsoft YaHei" **一个**族名,而部分系统族名是
            // "Microsoft YaHei UI";命中失败时默认字体不含 CJK → 表现就是"中文一个字都不显示"。
            // 现按候选族名逐个尝试并打明结果,全败则 LogError 而不是静默。
            var fontCandidates = new[]
            {
                "Microsoft YaHei UI", "Microsoft YaHei", "SimHei",
                "Microsoft JhengHei UI", "SimSun", "Noto Sans CJK SC", "PingFang SC",
            };
            bool fontBound = false;
            foreach (var family in fontCandidates)
            {
                try
                {
                    var osFont = Font.CreateDynamicFontFromOSFont(family, 16);
                    if (osFont == null) continue;
                    var fa = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(osFont);
                    if (fa == null) continue;
                    _rve.style.unityFontDefinition = new StyleFontDefinition(fa);
                    Debug.Log("[UiRoot] CJK font asset bound: " + family);
                    fontBound = true;
                    break;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[UiRoot] CJK font bind failed for '" + family + "': " + ex.Message);
                }
            }
            if (!fontBound)
                Debug.LogError("[UiRoot] 全部 CJK 字体候选绑定失败 —— 中文会渲染成空白。请检查系统字体。");

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

            // 2026-09-13 制作人:"不要让他们常态化展开,而是在屏幕下方中间并列,点击后在屏幕中央展开"。
            // BottomBar = 底部居中横排,只放各面板的**折叠入口**;Center = 屏幕中央,展开时的内容宿主。
            // 两者都居中,所以天然不会像贴着左右边那样被推到屏幕外。
            BottomBar = new VisualElement { name = "BottomBar" };
            BottomBar.AddToClassList("bottom-bar");
            BottomBar.pickingMode = PickingMode.Ignore;   // 自身不吃点击,子元素照常可点
            // 2026-09-13 布局改用**内联样式**定死:实测 theme.uss 里这几条规则没生效
            // (总览面板铺满宽度压在屏幕上方 → `.center-host` 的居中没应用),内联样式不依赖
            // USS 解析/缓存,和这套代码主体一致,且可直接读代码验证。
            BottomBar.style.position = Position.Absolute;
            BottomBar.style.left = 0; BottomBar.style.right = 0;
            BottomBar.style.bottom = 14;
            BottomBar.style.flexDirection = FlexDirection.Row;
            BottomBar.style.justifyContent = Justify.Center;
            BottomBar.style.alignItems = Align.Center;
            Screen.Add(BottomBar);

            Center = new VisualElement { name = "CenterHost" };
            Center.AddToClassList("center-host");
            Center.pickingMode = PickingMode.Ignore;      // 空的时候绝不能挡住战场点击
            Center.style.position = Position.Absolute;
            Center.style.left = 0; Center.style.right = 0;
            Center.style.top = 0; Center.style.bottom = 0;
            Center.style.alignItems = Align.Center;
            Center.style.justifyContent = Justify.Center;
            Screen.Add(Center);
        }

// 2026-09-13 移除 Start() 里的安全区留白:原代码把 safeArea 的**像素**值直接当**面板单位**
        // 写进 Screen 的 padding。缩放模式下像素≠单位,且编辑器里游戏视图缩放会让 safeArea 与
        // Screen 尺寸不一致 → 误触发,把整个 UI 向右下推(制作人截图:内容整体下移、贴底元素跑出屏幕)。
        // 安全区留白只对刘海/挖孔移动设备有意义(桌面恒等于整屏),现挪到 Update 里、
        // 在已知"单位/像素"换算比之后按单位写,且仅移动端生效。
        private void Start() { }


        private void Update()
        {
            // 2026-09-12 布局坑修复:实测(结算取证探针)rootVisualElement 宽度正常(缩放后面板宽)
            // 但 resolvedStyle.height==0 —— 所有依赖父容器高度的面板(结算全屏遮罩/百分比布局)全部塌陷。
            // 旧面板全用显式 top+height,overflow 可见,故从未暴露。按宽度缩放比显式补高度,命中一次即可。
            // 注意布局未就绪时 resolvedStyle 为 NaN,NaN 与任何数比较均为 false,必须显式排除。
            if (_centerFrameDirty && _centerFrame != null)
            {
                var wb = _centerFrame.worldBound;
                if (!float.IsNaN(wb.width) && wb.width > 0f) { LastCenterRect = wb; _centerFrameDirty = false; }
            }
            if (_rve == null) return;
            float w = _rve.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 1f) return;              // 布局未就绪
            float h = _rve.resolvedStyle.height;
            if (float.IsNaN(h) || h < 1f)
            {
                // 高度补丁:父容器高度恒 0/NaN(idle 探针实测),按"宽度×当前缩放比"补出来
                if (UnityEngine.Screen.width <= 0) return;
                float scale = w / UnityEngine.Screen.width;
                if (float.IsNaN(scale) || scale < 0.01f || scale > 100f) return;
                float target = UnityEngine.Screen.height * scale;
                if (float.IsNaN(target) || target < 1f) return;
                _rve.style.height = target;
                h = target;
                if (!_heightPatched)
                {
                    _heightPatched = true;
                    Debug.Log("[UiRoot] panel height patched to " + target.ToString("F0") + " (scale=" + scale.ToString("F3") + ")");
                }
            }
            // 2026-09-12 ★关键:Screen 尺寸**持续跟随**父容器,不再一次性定死。
            // 一次性定死的版本在视口尺寸/面板缩放变化后与新的缩放不匹配 —— 表现就是制作人报的
            // "UI 一个屏放不下、左右都超出"(旧的 2194 宽画布被按新的大缩放渲染 → 两边各切一截)。
            // ESC 关掉中央面板
            if (Input.GetKeyDown(KeyCode.Escape)) HandleEscPressed();
            if (!_safeAreaApplied && Application.isMobilePlatform && UnityEngine.Screen.width > 0)
            {
                float u = w / UnityEngine.Screen.width;      // 面板单位 / 像素
                if (!float.IsNaN(u) && u > 0.01f && u < 100f)
                {
                    var sa = UnityEngine.Screen.safeArea;
                    if (sa.x > 0f || sa.y > 0f
                        || sa.width < UnityEngine.Screen.width || sa.height < UnityEngine.Screen.height)
                    {
                        Screen.style.paddingLeft = sa.x * u;
                        Screen.style.paddingRight = (UnityEngine.Screen.width - sa.xMax) * u;
                        Screen.style.paddingTop = (UnityEngine.Screen.height - sa.yMax) * u;
                        Screen.style.paddingBottom = sa.y * u;
                    }
                    _safeAreaApplied = true;
                }
            }
            // 2026-09-14 ★制作人"所有面板点开在中间,过几秒没管就会往下一截(按钮还能点但点了不生效,
            // 再点才生效),改成固定中间":
            // 原来这里在**布局就绪之后**把 Screen 从"四边拉伸"改成显式 width/height(并 Auto 掉 right/bottom),
            // 这是一次**迟到的几何相位切换** —— 百分比高度(中央框的 maxHeight:84%)与居中基准跟着变,
            // 面板就"往下一截",点击命中区与视觉位置也错开一拍(第一次点不生效、再点才生效)。
            // 现在就**不再改写 Screen 的尺寸**:它保持四边拉伸(Screen 始终跟随父容器,与原意一致),
            // 只保留"父容器高度为 0/NaN 时补一个高度"这个必要补丁。几何一次定下,不再变。

        }

        private bool _safeAreaApplied;
        private System.Action _onOpen, _onClose;
        // 抓帧/自检用:按注册顺序打开第 i 个入口(带它自己的 onOpen,面板据此构建内容)
        private readonly System.Collections.Generic.List<string> _entryTitles = new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.List<VisualElement> _entryPanels = new System.Collections.Generic.List<VisualElement>();
        private readonly System.Collections.Generic.List<System.Action> _entryOpen = new System.Collections.Generic.List<System.Action>();
        private readonly System.Collections.Generic.List<System.Action> _entryClose = new System.Collections.Generic.List<System.Action>();

        public int EntryCount => _entryTitles.Count;
        /// <summary>当前是否有中央面板开着(自检/ESC 路由判据用)。</summary>
        public bool HasOpenCenter => _openPanel != null;

        // ===== 2026-09-14 制作人:"没有需要 esc 关闭的东西时,esc → 游戏暂停浮层" =====
        // ESC 统一在这里路由:先关中央面板;再按登记顺序问各面板(如建筑面板的"关掉目标"),
        // **没人接手**才执行兜底(=暂停浮层)。这样"关面板"和"弹暂停"不会在同一帧互相打架。
        private readonly System.Collections.Generic.List<System.Func<bool>> _escClaims
            = new System.Collections.Generic.List<System.Func<bool>>();
        private System.Action _escFallback;

        /// <summary>登记一个 ESC 消费者:返回 true = 已消费(例如"我关掉了自己的面板")。</summary>
        public void RegisterEscClaim(System.Func<bool> claim)
        {
            if (claim != null) _escClaims.Add(claim);
        }

        /// <summary>没有任何消费者接手时执行(暂停浮层注册它)。</summary>
        public void SetEscFallback(System.Action fallback) => _escFallback = fallback;

        /// <summary>处理一次 ESC。Update 里按键触发;自检/取证直接调它(=等价于按了 ESC)。</summary>
        public void HandleEscPressed()
        {
            if (_openPanel != null) { CloseCenter(); return; }
            for (int i = 0; i < _escClaims.Count; i++)
                if (_escClaims[i] != null && _escClaims[i]()) return;
            _escFallback?.Invoke();
        }

        public void OpenEntryAt(int i)
        {
            if (i < 0 || i >= _entryTitles.Count) return;
            ToggleCenter(_entryTitles[i], _entryPanels[i], _entryOpen[i], _entryClose[i]);
        }

        public void CloseAnyEntry() => CloseCenter();
        private VisualElement _centerFrame;
        private VisualElement _openPanel;

        /// <summary>
        /// 2026-09-13 制作人:"下面三个就做成三个按钮,点开就在屏幕中间展示面板,esc 或点 x 关闭"。
        /// 底部入口是普通按钮(不再是把整个 .panel 挂在下面那种"乱"),点开把面板放进中央框
        /// (带标题行 + ✕),ESC 也能关。面板显隐由这里统一管,面板自身不再关心定位。
        /// </summary>
        public void RegisterCenterEntry(string title, VisualElement panel,
            System.Action onOpen = null, System.Action onClose = null)
        {
            if (panel == null || BottomBar == null) return;
            // 2026-09-13 修"几个框叠在一起":面板自带 .panel 背景/边框,中央框外面还有一层 frame,
            // 两层宽度还不一致 → 看着就是两三个框叠着。进中央框的面板去掉自带皮肤,只留 frame 一层。
            panel.RemoveFromClassList("panel");
            panel.style.backgroundColor = Color.clear;
            panel.style.borderTopWidth = 0; panel.style.borderBottomWidth = 0;
            panel.style.borderLeftWidth = 0; panel.style.borderRightWidth = 0;
            panel.style.paddingLeft = 0; panel.style.paddingRight = 0;
            panel.style.paddingTop = 0; panel.style.paddingBottom = 0;
            panel.style.display = DisplayStyle.None;
            Center.Add(panel);
            _entryTitles.Add(title); _entryPanels.Add(panel);
            _entryOpen.Add(onOpen); _entryClose.Add(onClose);
            var b = UiKit.MakeButton(title, () => ToggleCenter(title, panel, onOpen, onClose));
            b.AddToClassList("center-entry");
            b.style.minWidth = 104;
            BottomBar.Add(b);
        }

        public void ToggleCenter(string title, VisualElement panel,
            System.Action onOpen = null, System.Action onClose = null)
        {
            if (_openPanel == panel) { CloseCenter(); return; }   // 再点同一个 = 收起
            CloseCenter();
            _openPanel = panel;
            _onOpen = onOpen; _onClose = onClose;
            _centerFrame = new VisualElement();
            _centerFrame.AddToClassList("center-frame");
            _centerFrame.style.backgroundColor = new Color(0.055f, 0.075f, 0.105f, 0.985f);
            _centerFrame.style.borderTopWidth = 1; _centerFrame.style.borderBottomWidth = 1;
            _centerFrame.style.borderLeftWidth = 1; _centerFrame.style.borderRightWidth = 1;
            var bc = new Color(0.55f, 0.64f, 0.78f, 0.62f);
            _centerFrame.style.borderTopColor = bc; _centerFrame.style.borderBottomColor = bc;
            _centerFrame.style.borderLeftColor = bc; _centerFrame.style.borderRightColor = bc;
            _centerFrame.style.borderTopLeftRadius = 8; _centerFrame.style.borderTopRightRadius = 8;
            _centerFrame.style.borderBottomLeftRadius = 8; _centerFrame.style.borderBottomRightRadius = 8;
            _centerFrame.style.paddingLeft = 10; _centerFrame.style.paddingRight = 10;
            _centerFrame.style.paddingTop = 8; _centerFrame.style.paddingBottom = 10;
            // 2026-09-15 制作人:"按钮点出来的面板大小变化…能不能给它定死大小,包括自己建筑的面板、敌人建筑的面板"。
            // 真因:中央框原来是**内容撑开**(minWidth 440 / maxWidth 880 / maxHeight 84%)→ 内容一多变大小 ✗。
            // 现在**尺寸定死** 560x520,内容超出在框内滚动(ScrollView 有了确定高度,才不会再被压成一坨)。
            _centerFrame.style.width = PanelWidth;
            _centerFrame.style.height = PanelHeight;
            _centerFrame.style.flexShrink = 0;
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            var t = UiKit.MakeLabel(title, "label-title");
            t.style.flexGrow = 1;
            bar.Add(t);
            var x = UiKit.MakeButton("✕", CloseCenter);
            x.AddToClassList("center-close");
            x.style.minWidth = 32;
            bar.Add(x);
            _centerFrame.Add(bar);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.Add(panel);
            _centerFrame.Add(scroll);
            panel.style.display = DisplayStyle.Flex;
            _centerFrameDirty = true;   // 矩形在 Update 里等布局就绪后再取(建框瞬间是 NaN)
            Center.Add(_centerFrame);
            _onOpen?.Invoke();   // 打开时通知面板(它据此只构建一次正文)
        }

        public void CloseCenter()
        {
            if (_openPanel != null) _openPanel.style.display = DisplayStyle.None;
            if (_centerFrame != null) { _centerFrame.RemoveFromHierarchy(); _centerFrame = null; }
            _onClose?.Invoke();
            _openPanel = null;
            _onOpen = null;
            _onClose = null;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>
        /// 换边(原地重建)专用:连 **UIDocument** 一起拆,而不只是拆 UiRoot 组件。
        /// 2026-09-14 制作人"换边问题还在"的根因之一:UIDocument 是挂在 bootstrap 上的独立组件,
        /// 只 Destroy(UiRoot) 会留下**整棵旧可视化树** —— 它照旧渲染(和新建的那棵叠在一起),
        /// 而且**照旧吃指针事件**:底部入口条上点到的其实是旧树的死按钮 → 点了没反应。
        /// </summary>
        public void Teardown()
        {
            if (_doc != null) { Destroy(_doc); _doc = null; }
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
            // 2026-09-14:把原始回调挂在 userData 上 —— 自检/验收脚本要能"点"这些按钮
            // (UITK 的 Clickable 没有公开的触发入口,反射找不到时用这个兜底,行为与真人点击同一段代码)。
            b.userData = onClick;
            return b;
        }

        public static Label MakeLabel(string text, string cls = "label")
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return l;
        }

        /// <summary>
        /// 元素当前屏幕矩形(IMGUI 兼容口径:输入拦截桥用)。
        /// ⚠ 2026-09-15:**已删除**原来那个"直接返回 worldBound"的版本 —— 它对"面板已关闭/祖先被隐藏"
        /// 完全无感(`worldBound` 在脱离树后不重置、保留旧值),是"幽灵拦截矩形"的唯一来源。
        /// 一律改用下面的 <see cref="VisibleRectOrZero"/>。
        /// </summary>
        /// <summary>
        /// 2026-09-15 修复"点敌方建筑打不开面板 / 框选自己军队失灵":
        /// 各面板在 `LateUpdate` 里有的是**无条件**把 `worldBound` 回填到静态 `LastPanelRect`(输入拦截链要用),
        /// 于是面板**关闭后**仍然留着一块矩形 → **幽灵拦截区**,把它盖住的地方的点击/框选全吞掉
        /// (与 2026-09-14 修过的 phantom LastPanelRect 同一类,但那次只修了道具面板一个)。
        /// 根因是 UI Toolkit 的 `worldBound` 在元素**脱离可视化树 / 祖先 display:none** 之后**不重置**,
        /// 保留上一次布局的旧值 —— 所以单看 `worldBound != 0` 判断不了"还visible吗"。
        /// 这里统一口径:**自身与所有祖先都挂着且可见**才返回矩形,否则 `Rect.zero`(不拦任何输入)。
        /// </summary>
        public static Rect VisibleRectOrZero(VisualElement e)
        {
            if (e == null) return Rect.zero;
            for (var p = e; p != null; p = p.parent)
            {
                if (p.panel == null) return Rect.zero;                                     // 已脱离可视化树 = 未显示
                if (p.resolvedStyle.display == DisplayStyle.None) return Rect.zero;        // 自己或祖先被隐藏
                if (!p.visible) return Rect.zero;                                          // visibility:hidden
            }
            var b = e.worldBound;
            if (float.IsNaN(b.width) || b.width <= 1f || b.height <= 1f) return Rect.zero;  // 无尺寸=无内容
            return new Rect(b.xMin, b.yMin, b.width, b.height);
        }

    }
}
