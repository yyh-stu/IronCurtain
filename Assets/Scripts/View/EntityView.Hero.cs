using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public sealed partial class EntityView
    {
        // 动画漂移补偿(2026-09-10):身体基准位+骨盆初始位
        private GameObject _body;
        private Vector3 _bodyBase;
        private Transform _hips;
        private Vector3 _hips0;

        private static RuntimeAnimatorController _heroCtrl;
        private static RuntimeAnimatorController HeroCtrl =>
            _heroCtrl != null ? _heroCtrl : (_heroCtrl = UnityEngine.Resources.Load<RuntimeAnimatorController>("Models/Heroes/HeroAnimator"));
        // 身高档:A(镇岳)/B(男标)/C(磐石·星落)。2026-09-09 用户拍板:将整体放大 20%
        public static float HeroTargetHeight(HeroId hero)
        {
            switch (hero)
            {
                case HeroId.ZhenYue: return 2.30f;
                case HeroId.PanShi:
                case HeroId.XingLuo: return 1.94f;
                default: return 2.14f;
            }
        }

        // ===== 单位:英雄 → Kenney 角色映射(后续替换机甲模型只改此处)=====

        public static string HeroModelName(HeroId hero)
        {
            switch (hero)
            {
                case HeroId.PanShi: return "Heroes/PanShi";
                case HeroId.ZhenYue: return "Heroes/ZhenYue";
                case HeroId.DuanFeng: return "Heroes/DuanFeng";
                case HeroId.ZhuFeng: return "Heroes/ZhuFeng";
                case HeroId.ZhuiRi: return "Heroes/ZhuiRi";
                case HeroId.XingLuo:
                default: return "Heroes/XingLuo";
            }
        }

        // 英雄主色(兵种识别,与士兵配色同源)。2026-09-09 制作人拍板:不得偏红偏蓝(避免阵营歧义)
        private static Color HeroAccent(HeroId h)
        {
            switch (h)
            {
                case HeroId.PanShi: return new Color(0.72f, 0.68f, 0.55f);   // 石色
                case HeroId.ZhenYue: return new Color(0.78f, 0.80f, 0.84f);  // 钢银(去蓝)
                case HeroId.DuanFeng: return new Color(0.88f, 0.88f, 0.90f); // 亮银(中性)
                case HeroId.ZhuFeng: return new Color(0.42f, 0.90f, 0.84f);  // 淡青
                case HeroId.ZhuiRi: return new Color(1.00f, 0.78f, 0.16f);   // 琥珀金(去红)
                default: return new Color(0.70f, 0.55f, 1.00f);              // 星空紫(星落)
            }
        }

        // 管线修复后的英雄材质(FBX 内嵌彩色贴图直挂;懒加载)
        private static readonly System.Collections.Generic.Dictionary<HeroId, Material> HeroMats
            = new System.Collections.Generic.Dictionary<HeroId, Material>();
        public static Material HeroMaterial(HeroId hero)
        {
            Material m;
            if (HeroMats.TryGetValue(hero, out m)) return m;
            var modelName = HeroModelName(hero);
            var idx = modelName.LastIndexOf('/');
            m = UnityEngine.Resources.Load<Material>("Models/Heroes/HeroMat_" + (idx >= 0 ? modelName.Substring(idx + 1) : modelName));
            HeroMats[hero] = m;
            return m;
        }

        // ===== 2026-09-10 武器分离件挂接:Models/Weapons/{名}.fbx 挂手骨,WeaponMat 直挂 =====
        // 朝向/握点为初版估值,按截图验收迭代;资产未生成时静默跳过(不阻断)。
        private sealed class WeaponSocketCfg
        {
            public string Model; public bool Left; public Vector3 Rot; public float Len; public float Grip;
            public WeaponSocketCfg(string m, bool l, Vector3 r, float len, float grip)
            { Model = m; Left = l; Rot = r; Len = len; Grip = grip; }
        }
        private static readonly System.Collections.Generic.Dictionary<HeroId, WeaponSocketCfg[]> HeroWeapons
            = new System.Collections.Generic.Dictionary<HeroId, WeaponSocketCfg[]>
            {
                { HeroId.ZhenYue, new[] { new WeaponSocketCfg("镇岳_大锤", false, new Vector3(180f, 0f, -15f), 2.3f, 0.28f) } },
                { HeroId.PanShi, new[]
                    {
                        new WeaponSocketCfg("磐石_盾", true, new Vector3(0f, 0f, 0f), 1.9f, 0.5f),
                        new WeaponSocketCfg("磐石_大刀", false, new Vector3(180f, 0f, -20f), 1.7f, 0.15f)
                    } },
                { HeroId.DuanFeng, new[] { new WeaponSocketCfg("断峰_长矛", false, new Vector3(180f, 0f, -10f), 3.0f, 0.35f) } },
                { HeroId.ZhuFeng, new[] { new WeaponSocketCfg("逐风_长枪", false, new Vector3(180f, 0f, -15f), 3.2f, 0.35f) } },
                { HeroId.ZhuiRi, new[] { new WeaponSocketCfg("坠日_大弓", true, new Vector3(0f, 0f, 0f), 1.9f, 0.5f) } },
                { HeroId.XingLuo, new[] { new WeaponSocketCfg("星落_法杖", false, new Vector3(180f, 0f, -12f), 2.4f, 0.4f) } },
            };
        private static readonly System.Collections.Generic.Dictionary<string, Material> WpnMats
            = new System.Collections.Generic.Dictionary<string, Material>();
        private static Material WeaponMaterial(string name)
        {
            Material m;
            if (WpnMats.TryGetValue(name, out m)) return m;
            m = UnityEngine.Resources.Load<Material>("Models/Weapons/WeaponMat_" + name);
            WpnMats[name] = m;
            return m;
        }

        private static Transform HandBone(Animator anim, Transform root, bool left)
        {
            if (anim != null)
            {
                var t = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (t != null) return t;
            }
            string want = left ? "LeftHand" : "RightHand";
            foreach (var x in root.GetComponentsInChildren<Transform>())
                if (x.name.Replace("mixamorig:", string.Empty).Contains(want)) return x;
            return null;
        }

        public static void AttachWeapons(HeroId hero, GameObject body)
        {
            WeaponSocketCfg[] cfgs;
            if (!HeroWeapons.TryGetValue(hero, out cfgs)) return;
            var anim = body.GetComponent<Animator>();
            foreach (var c in cfgs)
            {
                var prefab = UnityEngine.Resources.Load<GameObject>("Models/Weapons/" + c.Model);
                if (prefab == null) continue;
                var hand = HandBone(anim, body.transform, c.Left);
                if (hand == null) { Debug.LogWarning("[EntityView] no hand bone for " + c.Model); continue; }
                var socket = new GameObject("WS_" + c.Model);
                socket.transform.SetParent(hand, false);
                socket.transform.localRotation = Quaternion.Euler(c.Rot);
                var w = Instantiate(prefab, socket.transform);
                w.name = c.Model;
                w.transform.localRotation = prefab.transform.rotation; // 保留 fbx root 自带旋转(Blender X-90,同建筑坑)
                var rr = w.GetComponentsInChildren<Renderer>();
                if (rr.Length == 0) { Destroy(w); continue; }
                var b = rr[0].bounds;
                for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float s = longest > 0.0001f ? c.Len / longest : 1f;
                w.transform.localScale = Vector3.one * s;
                // 握点:武器沿自身纵轴向下平移 grip 比例(近似,截图迭代)
                w.transform.localPosition = Vector3.down * (c.Grip * c.Len);
                var mat = WeaponMaterial(c.Model);
                if (mat != null)
                    for (int i = 0; i < rr.Length; i++)
                    {
                        var ms = rr[i].sharedMaterials;
                        for (int k = 0; k < ms.Length; k++) ms[k] = mat;
                        rr[i].sharedMaterials = ms;
                    }
            }
        }

        // ===== 2026-09-10 逐风骑乘:战马模型+骑乘动画三段+行军 bob =====
        private Transform _mountRoot;
        private Transform _upLegL, _upLegR, _lowLegL, _lowLegR;
        private Quaternion _ul0, _ur0, _ll0, _lr0;
        private bool _mounted;
        private float _bobT;
        // 骑乘专用动画(骑坐/冲锋/前刺,同 state 语义);缺失时退回代码摆腿
        private static RuntimeAnimatorController _riderCtrl;
        private static RuntimeAnimatorController RiderCtrl =>
            _riderCtrl != null ? _riderCtrl : (_riderCtrl = UnityEngine.Resources.Load<RuntimeAnimatorController>("Models/Heroes/RiderAnimator"));

        private void MountHorse(GameObject body)
        {
            var prefab = UnityEngine.Resources.Load<GameObject>("Models/Weapons/逐风_战马");
            if (prefab == null) return;
            _mountRoot = new GameObject("Mount").transform;
            _mountRoot.transform.SetParent(_model.transform, false);
            var horse = Instantiate(prefab, _mountRoot.transform);
            horse.name = "Horse";
            horse.transform.localRotation = prefab.transform.rotation;
            var rr = horse.GetComponentsInChildren<Renderer>();
            if (rr.Length == 0) { Destroy(horse); _mountRoot = null; return; }
            var b = rr[0].bounds;
            for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
            // 归一化:马体最长轴≈2.6m(肩高约1.5),贴地+水平居中(算法同建筑件)
            float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float s = longest > 0.0001f ? 2.6f / longest : 1f;
            horse.transform.localScale = Vector3.one * s;
            b = rr[0].bounds;
            for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
            horse.transform.localPosition = new Vector3(_mountRoot.position.x - b.center.x,
                _mountRoot.position.y - b.min.y, _mountRoot.position.z - b.center.z);
            var mat = WeaponMaterial("逐风_战马");
            if (mat != null)
                for (int i = 0; i < rr.Length; i++)
                {
                    var ms = rr[i].sharedMaterials;
                    for (int k = 0; k < ms.Length; k++) ms[k] = mat;
                    rr[i].sharedMaterials = ms;
                }
            // 骑手上马:脚底抬到鞍位(肩高1.5 的约 0.7,截图迭代),整体随马 bob
            body.transform.SetParent(_mountRoot.transform, true);
            body.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            _bodyBase = body.transform.localPosition; // 骑乘基准位(漂移补偿用)
            var anim = body.GetComponent<Animator>();
            // 骑乘动画三段接管(骑坐/冲锋/前刺);controller 缺席时退回代码摆腿兜底
            if (anim != null && RiderCtrl != null)
            {
                anim.runtimeAnimatorController = RiderCtrl;
                return;
            }
            if (anim != null)
            {
                _upLegL = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                _upLegR = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                _lowLegL = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                _lowLegR = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            }
            if (_upLegL != null) _ul0 = _upLegL.localRotation;
            if (_upLegR != null) _ur0 = _upLegR.localRotation;
            if (_lowLegL != null) _ll0 = _lowLegL.localRotation;
            if (_lowLegR != null) _lr0 = _lowLegR.localRotation;
            _mounted = _upLegL != null && _upLegR != null;
        }

        private void LateUpdate()
        {
            // play 模式:动画更新后覆盖骑姿/补偿漂移;edit 模式(CaptureBatch)由 Tick 补调
            ApplyRidePose();
            CompensateAnimDrift();
        }

        // 动画漂移补偿(2026-09-10):AI 动画骨骼带全局位移(非原地),模型会漂离脚下光环/血条。
        // 每帧把骨盆 XZ 位移从身体局部位置中扣除(绝对式,不累计);Y 不补(保留步伐起伏)。
        private void CompensateAnimDrift()
        {
            if (_body == null || _hips == null) return;
            var d = _hips.localPosition - _hips0;
            _body.transform.localPosition = _bodyBase - new Vector3(d.x, 0f, d.z);
        }

        private void ApplyRidePose()
        {
            if (!_mounted || _upLegL == null) return;
            // 骑姿近似:大腿前抬外分+小腿内收(正式骑乘动作待管线)
            _upLegL.localRotation = _ul0 * Quaternion.Euler(50f, 0f, -35f);
            _upLegR.localRotation = _ur0 * Quaternion.Euler(50f, 0f, 35f);
            if (_lowLegL != null) _lowLegL.localRotation = _ll0 * Quaternion.Euler(-55f, 0f, 0f);
            if (_lowLegR != null) _lowLegR.localRotation = _lr0 * Quaternion.Euler(-55f, 0f, 0f);
        }

        private void BuildUnit(HeroId hero)
        {
            _model = NewModelRoot();
            var body = AddPiece(_model.transform, HeroModelName(hero), HeroTargetHeight(hero), 0f);
            // 2026-09-10 制作人目击修复:AI 动画非原地(骨骼带全局位移,移动中模型漂离脚下光环/血条
            // 约一个身位)。补偿:每帧把骨盆(Hips)的 XZ 动画位移从身体上减去,前进交给 Sim 根驱动。
            _body = body;
            if (body != null) _bodyBase = body.transform.localPosition;
            if (body != null)
            {
                // 2026-09-09 管线修复:美术彩色贴图直挂(绑骨 FBX 内嵌贴图 Unity 解不开,已抽取生成 HeroMat);
                // 找不到材质时回退旧 tint 路径。阵营识别走血条,不再整体染色。
                var heroMat = HeroMaterial(hero);
                if (heroMat != null)
                {
                    foreach (var rr in body.GetComponentsInChildren<Renderer>())
                    {
                        var ms = rr.sharedMaterials;
                        for (int i = 0; i < ms.Length; i++) ms[i] = heroMat;
                        rr.sharedMaterials = ms;
                    }
                }
                else
                {
                    Color faction = _side == Side.Blue ? new Color(0.75f, 0.9f, 1f)
                        : (_side == Side.Red ? new Color(1f, 0.72f, 0.66f) : Color.white);
                    TintRenderers(body, Color.Lerp(faction, HeroAccent(hero), 0.5f));
                }
                _anim = body.GetComponent<Animator>();
                if (_anim == null) _anim = body.AddComponent<Animator>();
                if (HeroCtrl != null) _anim.runtimeAnimatorController = HeroCtrl;
                _hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
                if (_hips != null) _hips0 = _hips.localPosition;
                // 2026-09-10 武器分离件挂手骨;逐风骑战马(资产缺失静默跳过)
                AttachWeapons(hero, body);
                if (hero == HeroId.ZhuFeng) MountHorse(body);
            }
            AddGlowDisc(_model.transform, 0.7f); // 阵营环(2026-09-10 拍板,弃个体主色)
        }
    }
}
