using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public sealed partial class EntityView
    {
        // CountLabel 相关(主将兵数标签)
        private TextMesh _countLabel;
        private float _countRefresh;
        private System.Func<int> _soldierCountOf;

        private Transform _hpFill, _shFill;
        private Transform _hpBack;
        private float _barH; // 实际条高:主将=_h,建筑=_h×2(2026-09-10 制作人拍板)
        private System.Collections.Generic.List<Transform> _ticks;   // VR01:刻度缓存,布局按 D 动态排位

        // 2026-09-09 制作人拍板:血条=阵营识别层(己方蓝/敌方红/中立灰,盾=灰),主将常显,血量读条长
        private static Material _hpMatBlue, _hpMatRed, _hpMatNeutral, _hpMatShield;
        private static void EnsureHpMats()
        {
            if (_hpMatBlue != null) return;
            _hpMatBlue = ViewAssets.Unlit(new Color(0.25f, 0.55f, 1f));
            _hpMatRed = ViewAssets.Unlit(new Color(0.98f, 0.28f, 0.22f));
            _hpMatNeutral = ViewAssets.Unlit(new Color(0.62f, 0.62f, 0.62f));
            _hpMatShield = ViewAssets.Unlit(new Color(0.78f, 0.80f, 0.84f));
        }
        private Material FactionHpMat()
        {
            EnsureHpMats();
            return _side == Side.Blue ? _hpMatBlue : (_side == Side.Red ? _hpMatRed : _hpMatNeutral);
        }

        public void SetHp(float hp, float maxHp)
        {
            if (_hpFill == null) return;
            float t = maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f;
            // 主将(非士兵单位)常显血条作阵营识别;建筑常显;血量损失读条长
            bool show = _e.Kind == EntityKind.Building || t < 0.999f
                || (_e.Kind == EntityKind.Unit && !_e.IsSoldier);
            _hpRoot.gameObject.SetActive(show);
            LayoutBar();
        }

        private void UpdateShield()
        {
            if (_shFill == null) return;
            LayoutBar();
        }

        // VR01(2026-09-21 返工单):血盾一体布局 —— 生命与护盾共用同一外框、同一高度、同一起点
        // (槽左端)。H=可见生命,S=护盾,D=max(MaxHp,H+S) 为唯一分母:生命段 W*H/D,护盾段
        // W*S/D 紧接其后,剩余为空槽;超额盾也不越框。SetHp 与 UpdateShield 都走这一个方法,
        // 不会出现一方用旧分母覆盖另一方。刻度(每格=per 点生命)按同一 D 换算屏幕位置,
        // 盾值波动时刻度随生命段一起缩放,不再错位。
        private void LayoutBar()
        {
            if (_hpFill == null || _shFill == null) return;
            var L = ComputeHpShieldBar(_w, _e.Hp, _e.MaxHp, _e.Shield, _e.ShieldMax);
            if (L.D <= 0f) return;   // MaxHp<=0:安全隐藏(外层 SetActive 已处理)
            _hpFill.localScale = new Vector3(L.LifeW, _barH, 1f);
            _hpFill.localPosition = new Vector3(L.LifeX, 0f, 0.005f);   // z 偏移:与槽底共面会 z-fighting(制作人报'黑色部分')
            _shFill.gameObject.SetActive(L.ShowShield);
            if (L.ShowShield)
            {
                _shFill.localScale = new Vector3(L.ShieldW, _barH, 1f);
                _shFill.localPosition = new Vector3(L.ShieldX, 0f, 0.006f);
            }
            if (_ticks != null)
            {
                int per = _e.Kind == EntityKind.Building ? 40 : 10;
                for (int i = 0; i < _ticks.Count; i++)
                    _ticks[i].localPosition = new Vector3(TickX(_w, (i + 1) * per, L.D), 0f, 0.01f);
            }
        }

        /// <summary>刻度(每格=per 点生命)在血槽内的屏幕横坐标:按含盾分母 D 换算,无盾时退化为等分。</summary>
        public static float TickX(float w, float value, float d)
            => d <= 0f ? 0f : -w * 0.5f + w * Mathf.Clamp01(value / d);

        /// <summary>血盾一体布局结果(局部坐标,槽宽 w、槽左端 -w/2)。纯函数,供 EditMode 断言。</summary>
        public readonly struct BarLayout
        {
            public readonly float LifeW, ShieldW, LifeX, ShieldX, D;
            public readonly bool ShowShield;
            public BarLayout(float lifeW, float shieldW, float lifeX, float shieldX, float d, bool showShield)
            { LifeW = lifeW; ShieldW = shieldW; LifeX = lifeX; ShieldX = shieldX; D = d; ShowShield = showShield; }
        }

        /// <summary>
        /// VR01 布局公式:H=clamp(hp,0,maxHp),S=max(shield,0),D=max(maxHp,H+S)。
        /// 生命段宽 w·H/D、护盾段宽 w·S/D,两段从槽左端同一起点依次排列;超额盾不越框。
        /// </summary>
        public static BarLayout ComputeHpShieldBar(float w, float hp, float maxHp, float shield, float shieldMax)
        {
            if (maxHp <= 0f || w <= 0f) return new BarLayout(0f, 0f, 0f, 0f, 0f, false);   // MaxHp<=0:整条安全隐藏
            float H = Mathf.Clamp(hp, 0f, maxHp);
            float S = Mathf.Max(shield, 0f);
            float D = Mathf.Max(maxHp, H + S);
            float lifeW = w * (H / D);
            float shW = w * (S / D);
            bool show = S > 0.01f && shieldMax > 0.01f && maxHp > 0f;
            return new BarLayout(lifeW, shW,
                -w * 0.5f + lifeW * 0.5f,
                -w * 0.5f + lifeW + shW * 0.5f,
                D, show);
        }

        private void BuildHpBar()
        {
            var root = new GameObject("HpBar");
            root.transform.SetParent(transform, false);
            // 2026-09-14:单位血条原来写死 2.7m。英雄模型改为贴地之后(见 EntityView.Building.AddPiece
            // 的说明:六英雄 fbx 枢轴在脚底,旧写法把模型整体抬高 targetH/2),这个写死值就比头顶高出
            // 一米多 —— 血条浮在人上面。改为按模型实测顶面定位,与模型高度解耦。
            root.transform.localPosition = new Vector3(0f,
                _e.Kind == EntityKind.Building ? _topY + 0.9f : ModelTopLocalY() + 0.45f, 0f);
            _hpRoot = root.transform;
            // 王者样式:深色背板略大一圈 + 阵营色血条;建筑条高×2(2026-09-10 制作人拍板)
            _barH = _e.Kind == EntityKind.Building ? _h * 2f : _h;
            // 2026-09-10 血条质感:贴图槽底(灰度木框渐变条×暗 tint),fill 内缩微边
            var slateMat = UiSkin.HpSlate();
            _hpBack = (slateMat != null
                ? ViewAssets.DecoTextured(slateMat, Vector3.zero, new Vector2(_w + 0.10f, _barH + 0.06f), root.transform, "Back")
                : ViewAssets.DecoQuad(Instantiate(_bk), Vector3.zero, new Vector2(_w + 0.08f, _barH + 0.05f), root.transform, "Back")).transform;
            _hpFill = ViewAssets.DecoQuad(FactionHpMat(), Vector3.zero, new Vector2(_w, _barH), root.transform, "Fill").transform;
            EnsureHpMats();
            // 刻度:主将 10 血一格,建筑 40 血一格(制作人拍板,格数随血量非百分比);
            // VR01:位置由 LayoutBar 按 D(含盾分母)动态排,这里只建格子并缓存
            int per = _e.Kind == EntityKind.Building ? 40 : 10;
            int seg = _e.MaxHp > 0f ? Mathf.Max(1, Mathf.FloorToInt(_e.MaxHp / per)) : 1;
            Material tickMat = ViewAssets.Unlit(new Color(0.62f, 0.64f, 0.68f));   // 2026-09-13:近黑刻度线读成"黑块",改浅灰
            _ticks = new System.Collections.Generic.List<Transform>(seg);
            for (int i = 1; i < seg; i++)
            {
                var tick = ViewAssets.DecoQuad(tickMat, new Vector3(0f, 0f, 0.01f), new Vector2(0.035f, _barH), root.transform, "Tick" + i);
                _ticks.Add(tick.transform);
            }
            // VR01:盾段=同一血槽内的灰白段(紧接可见生命段,LayoutBar 驱动长度/位置)
            _shFill = ViewAssets.DecoQuad(_hpMatShield, Vector3.zero, new Vector2(_w, _barH), root.transform, "Shield").transform;
            _shFill.gameObject.SetActive(false);
            LayoutBar();
        }
    }
}
