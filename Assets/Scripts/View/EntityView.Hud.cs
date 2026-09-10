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
            _hpFill.localScale = new Vector3(_w * t, _barH, 1f);
            _hpFill.localPosition = new Vector3(-_w * (1f - t) * 0.5f, 0f, 0f);
        }

        private void UpdateShield()
        {
            if (_shFill == null) return;
            bool show = _e.Shield > 0.01f && _e.ShieldMax > 0.01f && _e.MaxHp > 0f;
            _shFill.gameObject.SetActive(show);
            if (show)
            {
                // 2026-09-10 制作人拍板:盾条=血条右侧灰色段,长度与血同尺度(盾值/最大生命)
                float len = _w * Mathf.Clamp01(_e.Shield / _e.MaxHp);
                _shFill.localScale = new Vector3(len, _barH * 0.8f, 1f);
                _shFill.localPosition = new Vector3(_w * 0.5f + len * 0.5f + 0.04f, 0f, 0f);
            }
        }

        private void BuildHpBar()
        {
            var root = new GameObject("HpBar");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, _e.Kind == EntityKind.Building ? _topY + 0.9f : 2.7f, 0f);
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
            // 刻度:主将 10 血一格,建筑 40 血一格(制作人拍板,格数随血量非百分比)
            int per = _e.Kind == EntityKind.Building ? 40 : 10;
            int seg = _e.MaxHp > 0f ? Mathf.Max(1, Mathf.FloorToInt(_e.MaxHp / per)) : 1;
            Material tickMat = ViewAssets.Unlit(new Color(0.08f, 0.09f, 0.11f));
            for (int i = 1; i < seg; i++)
            {
                float x = -_w * 0.5f + _w * i / seg;
                ViewAssets.DecoQuad(tickMat, new Vector3(x, 0f, 0.01f), new Vector2(0.035f, _barH), root.transform, "Tick" + i);
            }
            // 盾条:血条右侧灰色(UpdateShield 驱动长度/位置)
            _shFill = ViewAssets.DecoQuad(_hpMatShield, Vector3.zero, new Vector2(_w, _barH * 0.8f), root.transform, "Shield").transform;
            _shFill.gameObject.SetActive(false);
        }
    }
}
