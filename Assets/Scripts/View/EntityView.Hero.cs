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

        // 2026-09-12 按英雄取动画控制器:远程各用专属攻击动作(坠日拉弓/星落举杖),其余共用近战三段;
        // 待机统一改为新生成的"站立待机"(替掉原先六人共用的镇岳跪姿)。
        private static readonly System.Collections.Generic.Dictionary<HeroId, RuntimeAnimatorController> _heroCtrls
            = new System.Collections.Generic.Dictionary<HeroId, RuntimeAnimatorController>();
        public static RuntimeAnimatorController HeroAnimatorFor(HeroId hero)
        {
            RuntimeAnimatorController c;
            if (_heroCtrls.TryGetValue(hero, out c)) return c;
            string n = hero == HeroId.ZhuiRi ? "ZhuiRiAnimator"
                     : (hero == HeroId.XingLuo ? "XingLuoAnimator" : "HeroAnimator");
            c = UnityEngine.Resources.Load<RuntimeAnimatorController>("Models/Heroes/" + n);
            if (c == null) c = UnityEngine.Resources.Load<RuntimeAnimatorController>("Models/Heroes/HeroAnimator");
            _heroCtrls[hero] = c;
            return c;
        }
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
            /// <summary>
            /// 绕武器**自身纵轴**的滚转角(度)。纵轴对齐只决定"指向",滚转本来是不确定的
            /// (见 AttachWeapons 注释);扁平件(盾)和弓弦这类"面朝向"就靠这个数定。
            /// </summary>
            public float RollDeg;
            // ---- VR02/VR03(2026-09-21 返工单)语义化挂接 ----
            /// <summary>挂接骨:Hand(默认,原行为)/ForeArm(盾贴前臂竖持)。</summary>
            public Skt Bone;
            /// <summary>socket 在挂骨局部空间的平移:掌心偏移(VR03)/前臂绑带点(VR02)。标定批处理产出。</summary>
            public Vector3 SocketOffset;
            /// <summary>三维握点(武器包围盒 min+size 比例);null=旧一维 Grip 沿最长轴。</summary>
            public Vector3? GripVec;
            /// <summary>
            /// VR02:Bind 后首次动画求值时,把武器纵轴一次性对齐世界竖直并固化(默认 false)。
            /// 标定基准坑:编辑器标定在绑定姿态下取样,与 runtime 待机姿态不同 → 静态欧拉失配
            /// (实测差 60°)。校准只在首帧 LateUpdate 做一次,之后盾随臂局部固定 —— 攻击/走动
            /// 产生合理倾斜,不构成"每帧硬锁世界竖直"。
            /// </summary>
            public bool CalibVertical;
            public WeaponSocketCfg(string m, bool l, Vector3 r, float len, float grip, float rollDeg = 0f,
                Skt bone = Skt.Hand, Vector3? socketOffset = null, Vector3? gripVec = null, bool calibVertical = false)
            { Model = m; Left = l; Rot = r; Len = len; Grip = grip; RollDeg = rollDeg; Bone = bone; SocketOffset = socketOffset ?? Vector3.zero; GripVec = gripVec; CalibVertical = calibVertical; }
        }
        /// <summary>VR02:武器挂接骨选择。</summary>
        public enum Skt { Hand, ForeArm }
        private static readonly System.Collections.Generic.Dictionary<HeroId, WeaponSocketCfg[]> HeroWeapons
            = new System.Collections.Generic.Dictionary<HeroId, WeaponSocketCfg[]>
            {
                // 握持标定(2026-09-12 全量扫描后按眼选档;Rot=手骨局部帧,武器纵轴对齐到该帧 +Y):
                // 站立待机下 +Z ⇒ 竖直(器头朝上)、-Z ⇒ 竖直(器头朝下)、±X/±Y ⇒ 水平。
                // 第 6 参 RollDeg = 绕武器自身纵轴的滚转(0=按模型原始朝向),修"面朝向"问题。
                { HeroId.ZhenYue, new[] { new WeaponSocketCfg("镇岳_大锤", false, new Vector3(90f, 0f, 0f), 2.3f, 0.45f,
                    // VR03(2026-09-21 返工单):socket 原点=手骨(腕关节) → 锤柄落在腕上、掌外漂移。
                    // 标定(VrCalibBatch,已求值待机):掌心=腕沿指尖方向 0.124m+掌面方向 0.042m。
                    // 配 ZhenYue_grip 握拳 mesh 变体(MeshGripFist),手指围柄不是穿腕。
                    socketOffset: new Vector3(-0.0015f, 0.1238f, 0.0422f)) } },
                { HeroId.PanShi, new[]
                    {
                        // VR02(2026-09-21 返工单):盾从 LeftHand 单点改挂 LeftForeArm(贴臂本体)。
                        // offset=-X 即臂外侧(局部 +X 世界朝上前方=身体内侧,推 -X 才向外);
                        // Roll=180 由 roll0/90/180/270 四档特写判读选出(绑带连接可见、竖直、无穿模)。
                        // CalibVertical=首帧动画求值后一次性对齐世界竖直(编辑器静态欧拉以绑定姿态为基准,
                        // 与 runtime 待机姿态实测差 60°,已弃用);之后盾随臂局部固定,动作期合理倾斜。
                        new WeaponSocketCfg("磐石_盾", true, new Vector3(314.8f, 45.3f, 270f), 1.9f, 0.50f, 180f,
                            Skt.ForeArm, new Vector3(0f, 0.13f, -0.08f), null, true),   // 骨局部-Z=解剖外侧(实机dump:-X在runtime待机指向下后,盾"偏后"的根因;-Z 随臂固定)
                        // 刀:握点 0.22→0.72 后特写确认手握在握柄上(柄与剑首在手上方、护手在下方),已正确
                        new WeaponSocketCfg("磐石_大刀", false, new Vector3(-90f, 0f, 0f), 1.7f, 0.72f)
                    } },
                { HeroId.DuanFeng, new[] { new WeaponSocketCfg("断峰_长矛", false, new Vector3(90f, 0f, 0f), 2.5f, 0.70f) } },
                { HeroId.ZhuFeng, new[] { new WeaponSocketCfg("逐风_长枪", false, new Vector3(0f, 0f, 90f), 3.0f, 0.70f) } },
                // 弓:原 Rot=(180,0,0) 落在"±X/±Y ⇒ 水平"档 → 弓臂横伸、弦横穿身体。
                // 改 (90,0,0)(与锤/矛同档)后特写确认为**竖直**;但弦仍在身体外侧,
                // 故滚转再翻 180°(180→0)把弦转到朝内(与出箭口同侧)。
                { HeroId.ZhuiRi, new[] { new WeaponSocketCfg("坠日_大弓", true, new Vector3(90f, 0f, 0f), 1.9f, 0.50f, 0f) } },
                { HeroId.XingLuo, new[] { new WeaponSocketCfg("星落_法杖", false, new Vector3(-90f, 0f, 0f), 2.0f, 0.60f) } },
            };
        /// <summary>握持标定扫描用覆盖(仅 Editor 标定工具设置;空=用配置表)。</summary>
        public static Vector3? WeaponAimOverride;
        /// <summary>握持标定扫描用握点比例覆盖(&lt;0 = 用配置表)。</summary>
        public static float WeaponGripOverride = -1f;
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

        /// <summary>
        /// VR03(2026-09-21 返工单):把英雄 SMR 的网格换成握拳变体(Models/Heroes/{hero}_grip)。
        /// mesh 级修改不经过骨骼 —— Animator 每帧蒙皮用的还是同一套权重/骨骼,握拳形状不会被覆盖。
        /// 变体由 Editor 工具 MeshGripFist 从原 fbx 顶点卷曲生成;资产缺席则跳过(不影响其他英雄)。
        /// </summary>
        private static void ApplyGripMesh(HeroId hero, GameObject body)
        {
            var m = UnityEngine.Resources.Load<Mesh>("Models/Heroes/" + hero + "_grip");
            if (m == null) return;
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (smr.sharedMesh != null && smr.sharedMesh.vertexCount == m.vertexCount)
                    smr.sharedMesh = m;
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

        /// <summary>VR02:按配置选挂骨(Hand 或 ForeArm,带名字扫描回退)。</summary>
        private static Transform SocketBoneFor(Animator anim, Transform root, Skt bone, bool left)
        {
            if (bone == Skt.ForeArm && anim != null)
            {
                var t = anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                if (t != null) return t;
            }
            if (bone == Skt.ForeArm)
            {
                string want = left ? "LeftForeArm" : "RightForeArm";
                foreach (var x in root.GetComponentsInChildren<Transform>())
                    if (x.name.Replace("mixamorig:", string.Empty).Contains(want)) return x;
            }
            return HandBone(anim, root, left);
        }

        /// <summary>W01:武器缺失计数(批处理/自检读取;0=全部就位)。reset 后可重复统计。</summary>
        public static int MissingWeaponCount { get; private set; }
        public static void ResetMissingWeaponCount() => MissingWeaponCount = 0;

        public static List<Transform> AttachWeapons(HeroId hero, GameObject body)
        {
            var calibPending = new List<Transform>();
            WeaponSocketCfg[] cfgs;
            if (!HeroWeapons.TryGetValue(hero, out cfgs)) return calibPending;
            var anim = body.GetComponent<Animator>();
            foreach (var c in cfgs)
            {
                var prefab = UnityEngine.Resources.Load<GameObject>("Models/Weapons/" + c.Model);
                if (prefab == null)
                {
                    // W01 修复:不再静默——记录错误并计数(批处理可检测 MissingWeaponCount>0 阻断)
                    Debug.LogError("[EntityView] ★武器模型缺失: " + c.Model + " (hero=" + hero + ") 路径=Models/Weapons/" + c.Model);
                    MissingWeaponCount++;
                    continue;
                }
                var hand = SocketBoneFor(anim, body.transform, c.Bone, c.Left);
                if (hand == null)
                {
                    Debug.LogError("[EntityView] ★挂骨缺失: " + c.Model + " (hero=" + hero + " bone=" + c.Bone + " left=" + c.Left + ")");
                    MissingWeaponCount++;
                    continue;
                }
                var socket = new GameObject("WS_" + c.Model);
                socket.transform.SetParent(hand, false);
                socket.transform.localPosition = c.SocketOffset;   // VR02/VR03:掌心偏移/前臂绑带点(骨局部)
                socket.transform.localRotation = Quaternion.Euler(WeaponAimOverride ?? c.Rot);
                var w = Instantiate(prefab, socket.transform);
                w.name = c.Model;
                w.transform.localRotation = prefab.transform.rotation; // 保留 fbx root 自带旋转(Blender X-90,同建筑坑)
                var rr = w.GetComponentsInChildren<Renderer>();
                if (rr.Length == 0)
                {
                    Debug.LogError("[EntityView] ★武器无渲染器: " + c.Model + " (hero=" + hero + ")");
                    MissingWeaponCount++;
                    Destroy(w);
                    continue;
                }
                var b = rr[0].bounds;
                for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float s = longest > 0.0001f ? c.Len / longest : 1f;
                w.transform.localScale = Vector3.one * s;
                // 2026-09-12 握持修正(原生多模态取证驱动),两步:
                // ①把武器自身纵轴(局部最长轴)旋到 socket 的 +Y ⇒ 武器指向只由 socket 帧决定,与模型自身朝向解耦;
                // ②把纵轴上 grip 比例处锚定到 socket 原点(=手骨) ⇒ 握点真正落在手里。
                Vector3 la = LongAxisDirLocal(w.transform);
                w.transform.localRotation = Quaternion.FromToRotation(w.transform.localRotation * la, Vector3.up)
                                          * w.transform.localRotation;
                // 2026-09-13 滚转修正(制作人报"磐石盾是横着的""坠日弓弦朝外"):
                // 上面那步只确定纵轴**指向**,绕纵轴的滚转是**未定义**的 —— FromToRotation 在纵轴与
                // +Y 近似同向/反向时会退化成任意取轴,结果不可复现。细长兵器看不出,扁平件(盾)和
                // 弓弦这类"面朝向"就直接暴露。
                // ★关键:追加的必须是**绕武器自身纵轴 la 的纯滚转**(post-multiply AngleAxis(deg, la)),
                // 这样 la 的指向不变、上面的对齐不会被破坏。我第一版图省事又套了一次任意轴
                // FromToRotation,结果把纵轴对齐整个转掉,盾反而变成横躺的 —— 教训记在这里。
                if (Mathf.Abs(c.RollDeg) > 0.01f)
                    w.transform.localRotation = w.transform.localRotation * Quaternion.AngleAxis(c.RollDeg, la);
                Vector3 anchor = GripAnchorLocal(w.transform, WeaponGripOverride >= 0f ? WeaponGripOverride : c.Grip, c.GripVec);
                w.transform.localPosition = -(w.transform.localRotation *
                    Vector3.Scale(w.transform.localScale, anchor));
                var mat = WeaponMaterial(c.Model);
                if (mat != null)
                    for (int i = 0; i < rr.Length; i++)
                    {
                        var ms = rr[i].sharedMaterials;
                        for (int k = 0; k < ms.Length; k++) ms[k] = mat;
                        rr[i].sharedMaterials = ms;
                    }
                if (c.CalibVertical) calibPending.Add(socket.transform);
            }
            return calibPending;
        }

        /// <summary>
        /// 武器握点在"武器自身空间"的坐标:gripVec 非空时为包围盒 min+size*gripVec(三维);
        /// 否则沿局部包围盒最长轴取 grip 比例(旧口径,其余英雄沿用)。全程只读局部 TRS。
        /// </summary>
        private static Vector3 GripAnchorLocal(Transform weapon, float grip, Vector3? gripVec)
        {
            bool any = false;
            Bounds box = new Bounds(Vector3.zero, Vector3.zero);
            AccumulateLocal(weapon, true, Matrix4x4.identity, ref box, ref any);
            if (!any) return Vector3.zero;
            if (gripVec.HasValue)
            {
                Vector3 g = gripVec.Value;
                return new Vector3(
                    Mathf.Lerp(box.min.x, box.max.x, Mathf.Clamp01(g.x)),
                    Mathf.Lerp(box.min.y, box.max.y, Mathf.Clamp01(g.y)),
                    Mathf.Lerp(box.min.z, box.max.z, Mathf.Clamp01(g.z)));
            }
            Vector3 size = box.size;
            int ax = (size.x >= size.y && size.x >= size.z) ? 0 : ((size.y >= size.z) ? 1 : 2);
            Vector3 axis = Vector3.zero; axis[ax] = 1f;
            return axis * (box.min[ax] + size[ax] * Mathf.Clamp01(grip));
        }

        /// <summary>武器自身空间里最长轴的方向(单位向量,与 GripAnchorLocal 同一包围盒口径)。</summary>
        private static Vector3 LongAxisDirLocal(Transform weapon)
        {
            bool any = false;
            Bounds box = new Bounds(Vector3.zero, Vector3.zero);
            AccumulateLocal(weapon, true, Matrix4x4.identity, ref box, ref any);
            if (!any) return Vector3.up;
            Vector3 size = box.size;
            int ax = (size.x >= size.y && size.x >= size.z) ? 0 : ((size.y >= size.z) ? 1 : 2);
            Vector3 dir = Vector3.zero; dir[ax] = 1f;
            return dir;
        }

        /// <summary>累加子树 Renderer 的 localBounds 到"武器根自身空间"(根用 identity,不读 world 矩阵)。</summary>
        private static void AccumulateLocal(Transform node, bool isRoot, Matrix4x4 parent,
            ref Bounds box, ref bool any)
        {
            Matrix4x4 m = isRoot ? Matrix4x4.identity
                : parent * Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale);
            var r = node.GetComponent<Renderer>();
            if (r != null)
            {
                Bounds lb = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = m.MultiplyPoint3x4(lb.center + Vector3.Scale(lb.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
                    if (!any) { box = new Bounds(corner, Vector3.zero); any = true; }
                    else box.Encapsulate(corner);
                }
            }
            for (int i = 0; i < node.childCount; i++)
                AccumulateLocal(node.GetChild(i), false, m, ref box, ref any);
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
            VertCalibOnce();
            ApplyRidePose();
            CompensateAnimDrift();
        }

        // VR02:CalibVertical 件(盾)的一次性竖直校准。倒计时 2 帧 = 跳过 Bind 当帧,
        // 保证至少经过一次完整 Animator 求值(runtime 待机姿态),再按当时世界竖直对齐并固化。
        private int _vertCalibCountdown = -1;
        private readonly List<Transform> _vertCalib = new List<Transform>();
        private void VertCalibOnce()
        {
            if (_vertCalibCountdown < 0) return;
            if (_vertCalibCountdown > 0) { _vertCalibCountdown--; return; }
            foreach (var ws in _vertCalib)
                if (ws != null)
                {
                    // 世界系最短弧:把武器纵轴(socket.up)转到竖直;绕纵轴滚转不变(面朝向保留)
                    ws.rotation = Quaternion.FromToRotation(ws.up, Vector3.up) * ws.rotation;
                }
            _vertCalibCountdown = -1;
            _vertCalib.Clear();
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
                var ctrl = HeroAnimatorFor(hero);
                if (ctrl != null) _anim.runtimeAnimatorController = ctrl;
                _hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
                if (_hips != null) _hips0 = _hips.localPosition;
                // 2026-09-10 武器分离件挂手骨;逐风骑战马(资产缺失静默跳过)
                ApplyGripMesh(hero, body);
                var calib = AttachWeapons(hero, body);
                if (calib != null && calib.Count > 0)
                {
                    _vertCalib.AddRange(calib);
                    _vertCalibCountdown = 2;
                }
                if (hero == HeroId.ZhuFeng) MountHorse(body);
            }
            AddGlowDisc(_model.transform, 0.7f); // 阵营环(2026-09-10 拍板,弃个体主色)
        }
    }
}
