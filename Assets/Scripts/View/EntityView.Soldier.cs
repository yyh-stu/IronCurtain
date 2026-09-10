using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public sealed partial class EntityView
    {
        // 士兵材质缓存(flash 审查:避免每兵 new Material×2)
        private static readonly System.Collections.Generic.Dictionary<Side, Material> SoldierBodyMats
            = new System.Collections.Generic.Dictionary<Side, Material>();
        private static Material _soldierWeaponMat;
        private static readonly System.Collections.Generic.Dictionary<HeroId, Material> SoldierAccentMats
            = new System.Collections.Generic.Dictionary<HeroId, Material>();

        // 2026-09-09 用户拍板:士兵不走美术管线,代码程序化建模——按主将兵种区分风格与配色,
        // 精细度略低(远景以剪影/配色识别),画面美术焦点留给主将
        private GameObject BuildSoldier(HeroId? hero)
        {
            var root = NewModelRoot();
            var sm = new GameObject("SoldierModel");
            sm.transform.SetParent(root.transform, false);
            sm.transform.localScale = Vector3.one * 1.25f;
            // 2026-09-09 制作人拍板·统一士兵样式:躯干=阵营色;头部=主将主色(去红蓝歧义);
            // 武器=主将武器缩小版剪影,中性灰(不再用色块/发光珠,消灭脚下色块)
            Material body;
            if (!SoldierBodyMats.TryGetValue(_side, out body))
            {
                body = _side == Side.Blue ? ViewAssets.Flat(new Color(0.30f, 0.56f, 0.96f), 0.3f)
                    : (_side == Side.Red ? ViewAssets.Flat(new Color(0.95f, 0.34f, 0.26f), 0.3f)
                    : ViewAssets.Flat(new Color(0.62f, 0.62f, 0.58f), 0.3f));
                SoldierBodyMats[_side] = body;
            }
            var style = hero ?? HeroId.PanShi;
            Material am;
            if (!SoldierAccentMats.TryGetValue(style, out am))
            {
                am = ViewAssets.Flat(HeroAccent(style), 0.3f);
                SoldierAccentMats[style] = am;
            }
            if (_soldierWeaponMat == null)
                _soldierWeaponMat = ViewAssets.Flat(new Color(0.45f, 0.46f, 0.50f), 0.35f);
            float torsoW = style == HeroId.PanShi ? 0.42f : (style == HeroId.ZhenYue ? 0.40f : 0.34f);
            // 躯干(阵营色)
            ViewAssets.DecoBox(body, new Vector3(0f, 0.42f, 0f), new Vector3(torsoW, 0.52f, 0.24f), sm.transform, "Torso");
            // 头(主将主色)
            ViewAssets.DecoBox(am, new Vector3(0f, 0.82f, 0f), Vector3.one * 0.22f, sm.transform, "Head");
            ViewAssets.DecoBox(am, new Vector3(0f, 0.95f, 0f), new Vector3(0.27f, 0.09f, 0.27f), sm.transform, "Helm");
            // 武器:主将武器缩小版(中性灰剪影)
            switch (style)
            {
                case HeroId.PanShi: // 短刀(主将=盾+大刀;盾板已按制作人要求移除)
                    ViewAssets.DecoBox(_soldierWeaponMat, new Vector3(0.26f, 0.52f, 0.18f), new Vector3(0.05f, 0.34f, 0.08f), sm.transform, "Blade");
                    break;
                case HeroId.ZhenYue: // 小锤
                    ViewAssets.DecoBox(_soldierWeaponMat, new Vector3(0.26f, 0.55f, 0.16f), new Vector3(0.05f, 0.05f, 0.85f), sm.transform, "Shaft");
                    ViewAssets.DecoBox(_soldierWeaponMat, new Vector3(0.26f, 0.80f, 0.55f), new Vector3(0.14f, 0.14f, 0.16f), sm.transform, "Hammer");
                    break;
                case HeroId.DuanFeng: // 长矛
                    ViewAssets.DecoBox(_soldierWeaponMat, new Vector3(0.24f, 0.62f, 0.10f), new Vector3(0.05f, 1.0f, 0.05f), sm.transform, "Spear");
                    ViewAssets.DecoBox(_soldierWeaponMat, new Vector3(0.24f, 1.16f, 0.10f), new Vector3(0.07f, 0.12f, 0.07f), sm.transform, "Tip");
                    break;
                case HeroId.ZhuFeng: // 平枪(骑姿)
                    var lance = new GameObject("Lance");
                    lance.transform.SetParent(sm.transform, false);
                    lance.transform.localPosition = new Vector3(0.24f, 0.55f, 0.3f);
                    ViewAssets.DecoBox(_soldierWeaponMat, Vector3.zero, new Vector3(0.05f, 0.05f, 1.05f), lance.transform, "Pole");
                    break;
                case HeroId.ZhuiRi: // 弓弧
                    var b1 = new GameObject("BowTop");
                    b1.transform.SetParent(sm.transform, false);
                    b1.transform.localPosition = new Vector3(-0.28f, 0.58f, 0.12f);
                    b1.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
                    ViewAssets.DecoBox(_soldierWeaponMat, Vector3.zero, new Vector3(0.04f, 0.5f, 0.04f), b1.transform, "B1");
                    var b2 = new GameObject("BowBot");
                    b2.transform.SetParent(sm.transform, false);
                    b2.transform.localPosition = new Vector3(-0.28f, 0.26f, 0.12f);
                    b2.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
                    ViewAssets.DecoBox(_soldierWeaponMat, Vector3.zero, new Vector3(0.04f, 0.5f, 0.04f), b2.transform, "B2");
                    break;
                default: // 法杖(星落)
                    ViewAssets.DecoBox(_soldierWeaponMat, new Vector3(0.26f, 0.55f, 0.10f), new Vector3(0.05f, 0.9f, 0.05f), sm.transform, "Staff");
                    break;
            }
            return root;
        }

        private void BuildCountLabel()
        {
            var go = new GameObject("CountLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 3.2f, 0f);
            _countLabel = go.AddComponent<TextMesh>();
            _countLabel.text = RegimentSystem.SoldiersPerGeneral + "/" + RegimentSystem.SoldiersPerGeneral;
            _countLabel.font = ViewAssets.UiFont(14);
            _countLabel.fontSize = 36;
            _countLabel.characterSize = 0.14f;
            _countLabel.fontStyle = FontStyle.Bold;
            _countLabel.color = ViewAssets.TeamColor(_side);
            _countLabel.anchor = TextAnchor.MiddleCenter;
            _countLabel.GetComponent<MeshRenderer>().sharedMaterial = ViewAssets.Flat(Color.white);
        }
    }
}
