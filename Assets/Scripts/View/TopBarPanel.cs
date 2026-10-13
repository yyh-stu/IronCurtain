using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>M6.2 顶栏:左己方基地血量+人口+EP / 中对局时间 / 右敌方基地血量(迷雾口径,未侦察显示 ???)。
    /// 2026-09-11 demo→成品迁移:IMGUI OnGUI → UI Toolkit(弹性三段布局,多分辨率/安全区原生);
    /// 数据口径全部不变(TopBarModel/VisionSystem.LastKnownBuilding)。
    /// LastPanelRect 语义保留:每帧回填 worldBound,SelectionInteractionCoordinator 输入拦截链不变。</summary>
    public sealed class TopBarPanel : MonoBehaviour
    {
        public static Rect LastPanelRect = Rect.zero;

        private SimCore _sim;
        private Side _side;
        private Entity _myBase, _enemyBase;
        private float _nextQuery;

        private VisualElement _root;
        private VisualElement _myHpFill, _enemyHpFill;
        private Label _myHpText, _popText, _epText, _clock, _phase, _enemyLabel, _enemyHpText;
        private VisualElement _enemyHpShell;

        public void Init(SimCore sim, Side playerSide)
        {
            _sim = sim;
            _side = playerSide;
            _nextQuery = -1f;
            if (UiRoot.Current == null) return;
            Build(UiRoot.Current.Screen);
        }

        private void Build(VisualElement parent)
        {
            _root = new VisualElement { name = "TopBar" };
            _root.AddToClassList("topbar");
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.right = 0; _root.style.top = 0;
            _root.style.height = 44;
            _root.style.flexDirection = FlexDirection.Row;
            _root.style.alignItems = Align.Center;
            _root.style.paddingLeft = 12; _root.style.paddingRight = 12;

            // 三段式:左(信息) — 中(时钟,flex 居中) — 右(敌方);窄屏自动挤压不再裁切
            var left = MakeGroup(Justify.FlexStart);
            left.Add(UiKit.MakeLabel("大本营"));
            var shell = new VisualElement();
            shell.AddToClassList("hpbar-shell");
            shell.style.width = 150; shell.style.marginLeft = 8; shell.style.marginRight = 8;
            _myHpFill = new VisualElement();
            _myHpFill.AddToClassList("hpbar-fill");
            shell.Add(_myHpFill);
            left.Add(shell);
            _myHpText = UiKit.MakeLabel("--"); _myHpText.style.marginRight = 14; left.Add(_myHpText);
            _popText = UiKit.MakeLabel(""); _popText.style.marginRight = 14; left.Add(_popText);
            _epText = UiKit.MakeLabel(""); left.Add(_epText);

            var center = MakeGroup(Justify.Center);
            center.style.flexGrow = 1;
            var centerCol = new VisualElement();
            _clock = UiKit.MakeLabel("--:--", "label-clock");
            _phase = UiKit.MakeLabel("", "label-phase");
            centerCol.Add(_clock); centerCol.Add(_phase);
            center.Add(centerCol);

            var right = MakeGroup(Justify.FlexEnd);
            _enemyLabel = UiKit.MakeLabel("敌方大本营 ???");
            _enemyHpShell = new VisualElement();
            _enemyHpShell.AddToClassList("hpbar-shell");
            _enemyHpShell.style.width = 150; _enemyHpShell.style.marginLeft = 8;
            _enemyHpFill = new VisualElement();          // 修复:忘加填充子元素 → ElementAt(0) 越界(Play 日志实测)
            _enemyHpFill.AddToClassList("hpbar-fill");
            _enemyHpShell.Add(_enemyHpFill);
            _enemyHpText = UiKit.MakeLabel(""); _enemyHpText.style.marginLeft = 8;
            right.Add(_enemyLabel); right.Add(_enemyHpShell); right.Add(_enemyHpText);

            _root.Add(left); _root.Add(center); _root.Add(right);
            parent.Add(_root);
        }

        private static VisualElement MakeGroup(Justify j)
        {
            var g = new VisualElement();
            g.style.flexDirection = FlexDirection.Row;
            g.style.justifyContent = j;
            g.style.alignItems = Align.Center;
            return g;
        }

        private void Update()
        {
            if (_sim == null || _root == null) return;

            // 时钟逐帧;数据 0.5s 一查(口径与旧版一致)
            _clock.text = TopBarModel.FormatCountdown(_sim);
            bool overtime = _sim.Phase == MatchPhase.Overtime;
            _clock.EnableInClassList("label-clock-overtime", overtime);   // 加时橙红
            _phase.text = TopBarModel.PhaseText(_sim);

            if (Time.unscaledTime >= _nextQuery)
            {
                _nextQuery = Time.unscaledTime + 0.5f;
                _myBase = TopBarModel.FindBase(_sim, _side);
                var other = _side == Side.Blue ? Side.Red : Side.Blue;
                _enemyBase = TopBarModel.FindBase(_sim, other);

                if (_myBase != null)
                {
                    ApplyHp(_myHpFill, _myHpText, _myBase.Hp, _myBase.MaxHp);
                    _popText.text = TopBarModel.PopText(_sim, _side);
                    _epText.text = TopBarModel.EpText(_sim, _side);
                }
                var snap = _enemyBase != null ? VisionSystem.LastKnownBuilding(_sim, _side, _enemyBase.Id) : null;
                if (snap == null)
                {
                    _enemyLabel.text = "敌方大本营 ???";
                    _enemyHpShell.style.display = DisplayStyle.None;
                    _enemyHpText.text = "";
                }
                else
                {
                    _enemyLabel.text = "敌方大本营";
                    _enemyHpShell.style.display = DisplayStyle.Flex;
                    ApplyHp(_enemyHpFill, _enemyHpText, snap.Hp, snap.MaxHp);
                }
            }
        }

        private static void ApplyHp(VisualElement fill, Label text, float hp, float maxHp)
        {
            float t = maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f;
            fill.style.width = Length.Percent(t * 100f);
            fill.style.backgroundColor = t > 0.5f ? new Color(0.30f, 0.85f, 0.40f)
                : (t > 0.25f ? new Color(1f, 0.85f, 0f) : new Color(1f, 0.30f, 0.25f));
            text.text = TopBarModel.HpText(hp, maxHp);
        }

        private void LateUpdate()
        {
            if (_root != null) LastPanelRect = UiKit.VisibleRectOrZero(_root);   // 输入拦截桥
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;
    }
}
