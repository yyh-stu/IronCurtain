using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>主界面英雄展示台(2026-09-12 制作人定稿):左侧 3D 展示位,六英雄可自选切换,纯展示。
    /// 复用战斗侧 EntityView 的英雄加载口径(fbx root 旋转保留/身高归一/HeroMat 直挂/武器挂手骨/
    /// 逐风骑马+RiderAnimator);展示位缓慢旋转便于全角度检视。模型缺席时显示占位立柱不阻断。</summary>
    public sealed class MenuStage : MonoBehaviour
    {
        private GameObject _heroRoot;
        private GameObject _pedestal;
        private HeroId _current;

        public HeroId Current => _current;

        public void Init()
        {
            _pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _pedestal.name = "Pedestal";
            _pedestal.transform.SetParent(transform, false);
            _pedestal.transform.localScale = new Vector3(2.6f, 0.25f, 2.6f);
            _pedestal.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            _pedestal.GetComponent<Renderer>().sharedMaterial = ViewAssets.Flat(
                new Color(0.62f, 0.58f, 0.52f), 0.15f);
        }

        public void Show(HeroId hero)
        {
            _current = hero;
            if (_heroRoot != null)
            {
                if (Application.isPlaying) Destroy(_heroRoot);
                else DestroyImmediate(_heroRoot);   // 编辑模式(ClipAuditBatch 取证)必须立即销毁
            }
            _heroRoot = new GameObject("Showcase_" + hero);
            _heroRoot.transform.SetParent(transform, false);

            var prefab = Resources.Load<GameObject>("Models/" + EntityView.HeroModelName(hero));
            var body = prefab != null ? Instantiate(prefab) : null;
            if (body == null)
            {
                Debug.LogWarning("[MenuStage] model missing for " + hero + ", placeholder shown");
                var ph = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                ph.transform.SetParent(_heroRoot.transform, false);
                ph.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);
                ph.transform.localPosition = new Vector3(0f, 1.25f, 0f);
                return;
            }
            body.name = "Body";
            body.transform.SetParent(_heroRoot.transform, false);
            body.transform.localRotation = prefab.transform.rotation; // 保留 fbx root 自带旋转(Blender X-90 坑,同武器/建筑)
            Normalize(body, EntityView.HeroTargetHeight(hero));

            var mat = EntityView.HeroMaterial(hero);
            if (mat != null)
                foreach (var rr in body.GetComponentsInChildren<Renderer>())
                {
                    var ms = rr.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) ms[i] = mat;
                    rr.sharedMaterials = ms;
                }

            var anim = body.GetComponent<Animator>();
            if (anim == null) anim = body.AddComponent<Animator>();
            EntityView.AttachWeapons(hero, body);
            if (hero == HeroId.ZhuFeng) MountHorse(_heroRoot.transform, body, anim);
            else if (HeroCtrlOf() != null) anim.runtimeAnimatorController = HeroCtrlOf();
        }

        private static RuntimeAnimatorController HeroCtrlOf() =>
            Resources.Load<RuntimeAnimatorController>("Models/Heroes/HeroAnimator");

        /// <summary>身高归一+立于展示台面(算法口径同 EntityView.AddPiece:包围盒贴地水平居中)。</summary>
        private static void Normalize(GameObject go, float targetHeight)
        {
            var rr = go.GetComponentsInChildren<Renderer>();
            if (rr.Length == 0) return;
            var b = rr[0].bounds;
            for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
            float s = b.size.y > 0.0001f ? targetHeight / b.size.y : 1f;
            go.transform.localScale *= s;
            b = rr[0].bounds;
            for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
            go.transform.localPosition += new Vector3(-b.center.x, 0.5f - b.min.y, -b.center.z); // 台面 y=0.5
        }

        /// <summary>逐风骑马(展示版,静态上马+骑坐动画;口径同战斗侧 MountHorse 简化)。</summary>
        private static void MountHorse(Transform root, GameObject body, Animator anim)
        {
            var prefab = Resources.Load<GameObject>("Models/Weapons/逐风_战马");
            var horse = prefab != null ? Instantiate(prefab, root) : null;
            if (horse == null) return;
            horse.name = "Horse";
            var rr = horse.GetComponentsInChildren<Renderer>();
            if (rr.Length > 0)
            {
                var b = rr[0].bounds;
                for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float s = longest > 0.0001f ? 2.6f / longest : 1f;
                horse.transform.localScale *= s;
                b = rr[0].bounds;
                for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
                horse.transform.localPosition = new Vector3(-b.center.x, 0.5f - b.min.y, -b.center.z);
                var m = Resources.Load<Material>("Models/Weapons/WeaponMat_逐风_战马");
                if (m != null)
                    for (int i = 0; i < rr.Length; i++)
                    {
                        var ms = rr[i].sharedMaterials;
                        for (int k = 0; k < ms.Length; k++) ms[k] = m;
                        rr[i].sharedMaterials = ms;
                    }
            }
            body.transform.SetParent(horse.transform, true);
            body.transform.localPosition = new Vector3(0f, 1.05f, 0f); // 鞍位=马根上方1.05(同战斗侧口径)
            var rider = Resources.Load<RuntimeAnimatorController>("Models/Heroes/RiderAnimator");
            if (anim != null && rider != null) anim.runtimeAnimatorController = rider;
        }

        private void Update()
        {
            if (_heroRoot != null) _heroRoot.transform.Rotate(0f, 14f * Time.deltaTime, 0f); // 展示台缓转
        }
    }
}
