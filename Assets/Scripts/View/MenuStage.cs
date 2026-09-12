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
            else if (HeroCtrlOf(hero) != null) anim.runtimeAnimatorController = HeroCtrlOf(hero);
            // 漂移补偿基准:必须在归一化与上马摆位之后取(2026-09-12:否则动画会把英雄带离展示台)
            _body = body.transform;
            _bodyBase = body.transform.localPosition;
            _hips = null; _driftInit = false;
        }

        private Transform _body, _hips;
        private Vector3 _hips0, _bodyBase;
        private bool _driftInit;

        /// <summary>与战斗侧 EntityView 同口径的动画漂移补偿:AI 动画骨骼带全局位移(非原地),
        /// 每帧把骨盆 XZ 位移从体位里减掉,英雄才会稳稳站在展示台上(2026-09-12 实测被带偏约一个台面)。</summary>
        private void LateUpdate()
        {
            if (_body == null) return;
            if (!_driftInit)
            {
                var anim = _body.GetComponent<Animator>();
                if (anim == null || !anim.isInitialized) return;
                _hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                if (_hips == null) return;
                _hips0 = _hips.localPosition;
                _bodyBase = _body.localPosition;
                _driftInit = true;
                return;
            }
            var d = _hips.localPosition - _hips0;
            _body.localPosition = _bodyBase - new Vector3(d.x, 0f, d.z);
        }

        private static RuntimeAnimatorController HeroCtrlOf(HeroId hero) => EntityView.HeroAnimatorFor(hero);

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

        // 2026-09-12 展示版骑马重写(原生多模态取证:骑手站在马前地面、马头重影级错位)。
        // 旧实现把骑手 SetParent 到马下并用马局部空间写鞍位 → 骑手继承了马的归一化缩放与偏移。
        // 现改为「人与马同为展示根的子物体」,互不继承缩放;马保留 fbx root 旋转。
        private const float HorseHeight = 1.70f;   // 马体目标高度(米)
        private const float SaddleLift = 0.62f;    // 骑手相对台面的抬升(骑坐上身的经验起点,按截图迭代)

        private static void MountHorse(Transform root, GameObject body, Animator anim)
        {
            var horse = PlacePiece(root, "Models/Weapons/逐风_战马", HorseHeight, new Vector3(0f, 0.5f, 0f), "Weapons");
            if (horse != null) horse.name = "Horse";
            // 骑手只抬 Y(水平居中已由 Normalize 完成),不再挂在马下 → 不受马的缩放影响
            var lp = body.transform.localPosition;
            body.transform.localPosition = new Vector3(lp.x, 0.5f + SaddleLift, lp.z);
            var rider = Resources.Load<RuntimeAnimatorController>("Models/Heroes/RiderAnimator");
            if (anim != null && rider != null) anim.runtimeAnimatorController = rider;
        }

        /// <summary>把资源模型摆进展示根:按高度归一、保留 fbx root 旋转、水平居中并让底部落在给定高度,
        /// 直挂对应材质(建筑走 BuildingMat_/武器走 WeaponMat_)。供展示台与大厅背景共用,避免多处重复归一化。</summary>
        internal static GameObject PlacePiece(Transform parent, string resPath, float targetHeight,
            Vector3 targetFoot, string matFolder)
        {
            var prefab = Resources.Load<GameObject>(resPath);
            if (prefab == null) { Debug.LogWarning("[MenuStage] 资源缺失 " + resPath); return null; }
            var go = Instantiate(prefab, parent);
            go.name = resPath.Substring(resPath.LastIndexOf('/') + 1);
            go.transform.localRotation = prefab.transform.rotation;   // 保留 fbx root 自带旋转(Blender X-90 坑)
            var rr = go.GetComponentsInChildren<Renderer>();
            if (rr.Length == 0) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); return null; }
            var b = rr[0].bounds;
            for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
            float s = b.size.y > 0.0001f ? targetHeight / b.size.y : 1f;
            go.transform.localScale *= s;
            b = rr[0].bounds;
            for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
            Vector3 c = parent.InverseTransformPoint(b.center);
            Vector3 foot = parent.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
            go.transform.localPosition += new Vector3(targetFoot.x - c.x, targetFoot.y - foot.y, targetFoot.z - c.z);
            // 材质:建筑 BuildingMat_* / 武器 WeaponMat_*(同名资源直挂,与 EntityView 同口径)
            string matName = matFolder == "Buildings" ? "BuildingMat_" : "WeaponMat_";
            var mat = Resources.Load<Material>("Models/" + matFolder + "/" + matName + go.name);
            if (mat != null)
                for (int i = 0; i < rr.Length; i++)
                {
                    var ms = rr[i].sharedMaterials;
                    for (int k = 0; k < ms.Length; k++) ms[k] = mat;
                    rr[i].sharedMaterials = ms;
                }
            return go;
        }

        private void Update()
        {
            if (_heroRoot != null) _heroRoot.transform.Rotate(0f, 14f * Time.deltaTime, 0f); // 展示台缓转
        }
    }
}
