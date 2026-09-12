using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>BP 选将界面(2026-09-13 制作人需求;视觉按 DeepSeek 审查指令 v2 调整):
    /// 左右半屏蓝红半透明屏障色 + 中央分隔线;中间按 重装/战士/射手 分组展示六英雄(头像=八视图右前45);
    /// 每轮 10s 倒计时(顶部胶囊),双方锁完或超时进入 3s 揭示(揭示浮层);同方已选置灰禁点。
    /// 三态差异(DeepSeek):选将中=蓝描边可选/金框本轮锁定;揭示=当前轮发光其余降透明;完成=中央卡片压暗+"选择完成"。</summary>
    public sealed class BpPanel : MonoBehaviour
    {
        private BpSession _s;
        private System.Action _onDone;

        private Label _pill;
        private VisualElement _reveal, _revealBlue, _revealRed;
        private Label _revealRound;
        private readonly List<VisualElement> _blueSlots = new List<VisualElement>();
        private readonly List<VisualElement> _redSlots = new List<VisualElement>();
        private readonly Dictionary<HeroId, VisualElement> _cards = new Dictionary<HeroId, VisualElement>();
        private Label _doneLabel;
        private VisualElement _doneDim;
        private float _doneAt = -1f;

        private static readonly HeroId[] Heroes =
            { HeroId.PanShi, HeroId.ZhenYue, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
        private static readonly string[] Names = { "磐石", "镇岳", "断峰", "逐风", "坠日", "星落" };
        private static readonly string[] Roles = { "重装", "重装", "战士", "战士", "射手", "射手" };
        private static readonly string[] RoleOrder = { "重装", "战士", "射手" };

        // 屏障色(DeepSeek 指令:半透明蓝红对半,非斜切实色)
        private static readonly Color BlueTint = new Color(0x2B / 255f, 0x6C / 255f, 0xB0 / 255f, 0.32f);
        private static readonly Color RedTint = new Color(0xC5 / 255f, 0x30 / 255f, 0x30 / 255f, 0.32f);
        private static readonly Color PickOutline = new Color(0x58 / 255f, 0xA6 / 255f, 0xFF / 255f, 1f);   // 可选描边
        private static readonly Color LockGold = new Color(0xFF / 255f, 0xD9 / 255f, 0x66 / 255f, 1f);      // 锁定/发光

        public void Init(BpSession session, System.Action onDone)
        {
            _s = session;
            _onDone = onDone;
            if (UiRoot.Current == null || UiRoot.Current.Screen == null) return;
            var root = UiRoot.Current.Screen;
            float half = UnityEngine.Screen.width / 2f;

            // 左右半屏屏障式半透明色 + 中央分隔线(DeepSeek:斜切实色改半透矩形)
            AddHalf(root, 0f, half, BlueTint);
            AddHalf(root, half, half, RedTint);
            var seam = new VisualElement();
            seam.style.position = Position.Absolute;
            seam.style.left = half - 1; seam.style.width = 2;
            seam.style.top = 0; seam.style.bottom = 0;
            seam.style.backgroundColor = new Color(0xD8/255f, 0xE2/255f, 0xF0/255f, 0.65f);
            seam.pickingMode = PickingMode.Ignore;
            root.Add(seam);

            // 顶部胶囊:轮次 + 倒计时(DeepSeek:带黑底胶囊,距顶 12)
            _pill = UiKit.MakeLabel("", "bp-pill");
            _pill.style.fontSize = 30; _pill.style.height = 52;
            _pill.style.position = Position.Absolute;
            _pill.style.left = half - 260; _pill.style.width = 520;
            _pill.style.top = 12;
            root.Add(_pill);

            BuildFactionBadge(root, true);
            BuildFactionBadge(root, false);
            BuildSlots(root, true, _blueSlots);
            BuildSlots(root, false, _redSlots);

            // 中间:分组英雄卡(DeepSeek:分组标签 18 白描边 + 蓝色短线;卡片 #1E2A44 底/白细边/名称条)
            var cards = new VisualElement();
            cards.style.position = Position.Absolute;
            cards.style.left = half - 340; cards.style.width = 680;
            cards.style.top = 130;
            cards.style.flexDirection = FlexDirection.Column;
            foreach (var role in RoleOrder)
            {
                var wrap = new VisualElement();
                var line = new VisualElement();
                line.AddToClassList("bp-group-line");
                line.style.marginLeft = 64 + 24;
                wrap.Add(line);
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginTop = 4;
                var tag = UiKit.MakeLabel(role, "bp-group-tag");
                tag.style.width = 64; tag.style.marginLeft = 24;
                tag.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(tag);
                for (int i = 0; i < Heroes.Length; i++)
                {
                    if (Roles[i] != role) continue;
                    row.Add(MakeCard(Heroes[i], Names[i]));
                }
                wrap.Add(row);
                cards.Add(wrap);
            }
            root.Add(cards);

            _doneDim = new VisualElement();
            _doneDim.style.position = Position.Absolute;
            _doneDim.style.left = 0; _doneDim.style.right = 0; _doneDim.style.top = 0; _doneDim.style.bottom = 0;
            _doneDim.style.backgroundColor = new Color(0.03f, 0.05f, 0.09f, 0.62f);
            _doneDim.style.display = DisplayStyle.None;
            _doneDim.pickingMode = PickingMode.Ignore;
            root.Add(_doneDim);

            _doneLabel = UiKit.MakeLabel("选 择 完 成", "bp-done");
            _doneLabel.style.position = Position.Absolute;
            _doneLabel.style.left = half - 220; _doneLabel.style.width = 440;
            _doneLabel.style.top = 300;
            _doneLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _doneLabel.style.display = DisplayStyle.None;
            root.Add(_doneLabel);

            // 揭示浮层(本轮双方所选同时展示,3s)
            _reveal = new VisualElement();
            _reveal.style.position = Position.Absolute;
            _reveal.style.left = 0; _reveal.style.right = 0; _reveal.style.top = 0; _reveal.style.bottom = 0;
            _reveal.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _reveal.style.display = DisplayStyle.None;
            _reveal.style.alignItems = Align.Center;
            _reveal.style.justifyContent = Justify.Center;
            _revealRound = UiKit.MakeLabel("", "bp-pill");
            _reveal.Add(_revealRound);
            var vsRow = new VisualElement();
            vsRow.style.flexDirection = FlexDirection.Row;
            vsRow.style.alignItems = Align.Center;
            vsRow.style.justifyContent = Justify.Center;
            vsRow.style.marginTop = 18;
            _revealBlue = RevealSide(true);
            var vs = UiKit.MakeLabel("VS", "victory-title");
            vs.style.fontSize = 54; vs.style.width = 140;
            vs.style.unityTextAlign = TextAnchor.MiddleCenter;
            _revealRed = RevealSide(false);
            vsRow.Add(_revealBlue); vsRow.Add(vs); vsRow.Add(_revealRed);
            _reveal.Add(vsRow);
            root.Add(_reveal);
        }

        /// <summary>阵营徽标(2026-09-13 制作人批准):左上蓝/右上红胶囊。</summary>
        private static void BuildFactionBadge(VisualElement root, bool blue)
        {
            var b = UiKit.MakeLabel(blue ? "◈ 蓝 方" : "红 方 ◈", "bp-group-tag");
            b.style.position = Position.Absolute;
            b.style.fontSize = 24;
            b.style.top = 16;
            b.style.height = 44;
            b.style.width = 168;
            if (blue) b.style.left = 24; else b.style.right = 24;
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.style.borderTopLeftRadius = 22; b.style.borderTopRightRadius = 22;
            b.style.borderBottomLeftRadius = 22; b.style.borderBottomRightRadius = 22;
            b.style.backgroundColor = blue ? new Color(0.17f, 0.42f, 0.69f, 0.55f) : new Color(0.77f, 0.19f, 0.19f, 0.55f);
            b.style.color = blue ? new Color(0.85f, 0.93f, 1f) : new Color(1f, 0.88f, 0.85f);
            root.Add(b);
        }

        private static void AddHalf(VisualElement root, float left, float width, Color c)
        {
            var e = new VisualElement();
            e.style.position = Position.Absolute;
            e.style.top = 0; e.style.bottom = 0;
            e.style.left = left; e.style.width = width;
            e.style.backgroundColor = c;
            e.pickingMode = PickingMode.Ignore;
            root.Add(e);
        }

        private void BuildSlots(VisualElement root, bool blue, List<VisualElement> slots)
        {
            var col = new VisualElement();
            col.style.position = Position.Absolute;
            col.style.top = 170;
            col.style.width = 96 + 8;   // DeepSeek:槽位 96×96(头像版放 168 便于看清,边距 24)
            if (blue) col.style.left = 24; else col.style.right = 24;
            col.style.flexDirection = FlexDirection.Column;
            var head = UiKit.MakeLabel(blue ? "蓝 方" : "红 方", "bp-group-tag");
            head.style.fontSize = 20;
            head.style.unityTextAlign = TextAnchor.MiddleCenter;
            head.style.color = blue ? new Color(0x6B / 255f, 0xB6 / 255f, 0xFF / 255f) : new Color(0xFF / 255f, 0x6B / 255f, 0x6B / 255f);
            col.Add(head);
            for (int i = 0; i < BpSession.Rounds; i++)
            {
                var slot = new VisualElement();
                slot.AddToClassList("bp-slot");
                slot.style.width = 148; slot.style.height = 150;
                slot.style.marginTop = 8;
                var q = UiKit.MakeLabel("?", "label-dim");
                q.style.fontSize = 36;
                q.style.color = new Color(0x8A / 255f, 0x9B / 255f, 0xB5 / 255f);
                q.style.position = Position.Absolute;
                q.style.left = 0; q.style.right = 0; q.style.top = 44;
                q.style.unityTextAlign = TextAnchor.MiddleCenter;
                slot.Add(q);
                col.Add(slot);
                slots.Add(slot);
            }
            root.Add(col);
        }

        private VisualElement MakeCard(HeroId hero, string name)
        {
            var card = new VisualElement();
            card.AddToClassList("bp-card");
            card.style.width = 128; card.style.height = 150;
            card.style.marginLeft = 14;
            var por = new Image();
            var tex = Resources.Load<Texture2D>("UI/portraits_bp/" + hero);
            if (tex != null) por.image = tex;
            por.style.width = 96; por.style.height = 96;
            por.style.marginTop = 8;
            por.style.borderTopLeftRadius = 12; por.style.borderTopRightRadius = 12;
            por.style.borderBottomLeftRadius = 12; por.style.borderBottomRightRadius = 12;
            por.scaleMode = ScaleMode.ScaleAndCrop;
            card.Add(por);
            var bar = new VisualElement();
            bar.AddToClassList("bp-namebar");
            bar.style.width = 112;
            bar.style.marginTop = 6;
            var nl = UiKit.MakeLabel(name, "bp-card-name");
            nl.style.unityTextAlign = TextAnchor.MiddleCenter;
            bar.Add(nl);
            card.Add(bar);
            card.RegisterCallback<ClickEvent>(_ =>
            {
                if (_s == null || _s.State != BpSession.Phase.Picking) return;
                if (_s.PickBlue(hero)) { UiSfx.Click(); Refresh(); }
                else UiSfx.Error();
            });
            _cards[hero] = card;
            return card;
        }

        private static VisualElement RevealSide(bool blue)
        {
            var e = new VisualElement();
            e.style.width = 210;
            e.style.alignItems = Align.Center;
            var img = new Image();
            img.style.width = 120; img.style.height = 120;
            img.scaleMode = ScaleMode.ScaleToFit;
            img.name = "img";
            e.Add(img);
            var label = UiKit.MakeLabel("", "bp-card-name");
            label.style.fontSize = 22;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.name = "label";
            e.Add(label);
            var side = UiKit.MakeLabel(blue ? "蓝方" : "红方", "label-dim");
            side.style.unityTextAlign = TextAnchor.MiddleCenter;
            e.Add(side);
            return e;
        }

        private void Update()
        {
            if (_s == null) return;
            _s.Tick(Time.unscaledDeltaTime);
            Refresh();

            if (_s.State == BpSession.Phase.Done && _doneAt < 0f)
            {
                _doneAt = Time.unscaledTime + 1.4f;
                _reveal.style.display = DisplayStyle.None;
                _doneLabel.style.display = DisplayStyle.Flex;
                _pill.text = "选 择 完 成 · 即将进入战斗…";   // DS r3:完成态并入胶囊体系
            }
            if (_doneAt > 0f && Time.unscaledTime >= _doneAt && _onDone != null)
            {
                var cb = _onDone; _onDone = null;
                cb();
            }
        }

        private void Refresh()
        {
            if (_pill == null) return;
            if (_s.State == BpSession.Phase.Picking)
            {
                _pill.text = string.Format("第 {0} / {1} 轮 · {2}s", _s.Round + 1, BpSession.Rounds,
                    Mathf.CeilToInt(Mathf.Max(0f, _s.Timer)));
                _reveal.style.display = DisplayStyle.None;
                _doneLabel.style.display = DisplayStyle.None;
                if (_doneDim != null) _doneDim.style.display = DisplayStyle.None;
                foreach (var kv in _cards)
                {
                    bool taken = _s.Blue.Contains(kv.Key);
                    bool locked = _s.BlueLock == kv.Key;
                    kv.Value.style.opacity = taken ? 0.30f : 1f;
                    SetBorder(kv.Value, locked ? 3 : 2, locked ? LockGold : PickOutline);
                }
            }
            else if (_s.State == BpSession.Phase.Reveal)
            {
                _pill.text = string.Format("第 {0} / {1} 轮 · 揭示 {2}s", _s.Round + 1, BpSession.Rounds, Mathf.CeilToInt(Mathf.Max(0f, _s.Timer)));
                _reveal.style.display = DisplayStyle.Flex;
                _revealRound.text = string.Format("第 {0} / {1} 轮 · 选择结果", _s.Round + 1, BpSession.Rounds);
                FillReveal(_revealBlue, _s.RevealedBlue);
                FillReveal(_revealRed, _s.RevealedRed);
                foreach (var kv in _cards)
                {
                    bool cur = kv.Key == _s.RevealedBlue || kv.Key == _s.RevealedRed;
                    kv.Value.style.opacity = cur ? 1f : 0.48f;   // DS r2:非当前轮压到 48%(grayscale 用透明度近似)
                    SetBorder(kv.Value, cur ? 4 : 2, cur ? LockGold : new Color(1f, 1f, 1f, 0.12f));
                }
            }
            else
            {
                // DS r2:完成态全屏压暗,只留槽位与完成文案高亮
                _revealRound.text = "";
                _reveal.style.display = DisplayStyle.None;
                var dim = _doneDim;
                if (dim != null) dim.style.display = DisplayStyle.Flex;
                foreach (var kv in _cards)
                    kv.Value.style.opacity = 0.25f;
            }
            RefreshSlots(_blueSlots, _s.Blue);
            RefreshSlots(_redSlots, _s.Red);
        }

        private static void SetBorder(VisualElement e, float w, Color c)
        {
            e.style.borderTopWidth = w; e.style.borderBottomWidth = w;
            e.style.borderLeftWidth = w; e.style.borderRightWidth = w;
            e.style.borderTopColor = c; e.style.borderBottomColor = c;
            e.style.borderLeftColor = c; e.style.borderRightColor = c;
        }

        private static void FillReveal(VisualElement side, HeroId hero)
        {
            var img = side.Q<Image>(name: "img");
            var label = side.Q<Label>(name: "label");
            if (img != null) img.image = Resources.Load<Texture2D>("UI/portraits_bp/" + hero);
            if (label != null) label.text = NameOf(hero);
        }

        private static void RefreshSlots(List<VisualElement> slots, List<HeroId> picks)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (i >= picks.Count) continue;
                var img = slot.Q<Image>(name: "por");
                if (img == null)
                {
                    img = new Image { name = "por" };
                    img.style.width = 104; img.style.height = 104;
                    img.style.position = Position.Absolute;
                    img.style.left = 22; img.style.top = 8;
                    img.scaleMode = ScaleMode.ScaleAndCrop;
                    slot.Add(img);
                    var q = slot.Q<Label>();
                    if (q != null) q.style.display = DisplayStyle.None;
                    var nl = UiKit.MakeLabel("", "bp-card-name");
                    nl.name = "nm";
                    nl.style.fontSize = 14;
                    nl.style.position = Position.Absolute;
                    nl.style.left = 0; nl.style.right = 0; nl.style.bottom = 2;
                    nl.style.unityTextAlign = TextAnchor.MiddleCenter;
                    slot.Add(nl);
                }
                img.image = Resources.Load<Texture2D>("UI/portraits_bp/" + picks[i]);
                var nameL = slot.Q<Label>(name: "nm");
                if (nameL != null) nameL.text = NameOf(picks[i]);
            }
        }

        private static string NameOf(HeroId h)
        {
            int i = System.Array.IndexOf(Heroes, h);
            return i >= 0 ? Names[i] : h.ToString();
        }
    }
}
