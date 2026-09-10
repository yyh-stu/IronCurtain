using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public sealed class EntityView : MonoBehaviour
    {
        // AI 管线士兵模型接入后改为加载 Resources 模型
        private static bool UseSoldierArtPlaceholder = true;

        private Entity _e;
        private Side _side;
        private GameObject _model, _gr, _chunks;
        // 动画漂移补偿(2026-09-10):身体基准位+骨盆初始位
        private GameObject _body;
        private Vector3 _bodyBase;
        private Transform _hips;
        private Vector3 _hips0;
        private Transform _hpRoot, _hpFill, _shFill;
        private float _w = 1.7f, _h = 0.26f, _deadT = -1f, _topY = 3.5f;
        private bool _dead, _rubbled;
        // 2026-09-10 建筑正式 fbx 已挂(BuildingMat 直挂,不走 grey/tint 回退)
        private bool _bldArt;
        private int _gf = -1, _gt = -1;
        private Material _g, _y, _r, _bk, _sh, _rubG, _gA, _gB;

        private TextMesh _countLabel;
        private float _countRefresh;
        private System.Func<int> _soldierCountOf;

        // M6.1 战争迷雾门控状态
        private bool _fogUnitHidden;
        private bool _fogBuildingUnknown;
        private bool _fogBuildingStale;
        private int? _fogGarrisonOverride;

        // M8.6b 行军组件接管位:为 true 时 Tick 跳过位置同步(外部组件驱动 transform)
        public bool MarchHold;

        // M6.2 飘字顶部锚点高度(单位默认 2.9;建筑在 Bind 时覆盖)
        public float TextTopY = 2.9f;
        private float _hitT;
        private MeshRenderer[] _modelRenderers;
        private bool _modelFlashed;
        private MaterialPropertyBlock _flashBlock;
        private Transform _hpBack;

        // M6.0f 六英雄模型接线:动画驱动与配置(懒加载:Unity 禁止静态初始化器调 Resources.Load)
        private Animator _anim;
        private static RuntimeAnimatorController _heroCtrl;
        private static RuntimeAnimatorController HeroCtrl =>
            _heroCtrl != null ? _heroCtrl : (_heroCtrl = UnityEngine.Resources.Load<RuntimeAnimatorController>("Models/Heroes/HeroAnimator"));
        // 身高档:A(镇岳)/B(男标)/C(磐石·星落)。2026-09-09 用户拍板:将整体放大 20%
        private static float HeroTargetHeight(HeroId hero)
        {
            switch (hero)
            {
                case HeroId.ZhenYue: return 2.30f;
                case HeroId.PanShi:
                case HeroId.XingLuo: return 1.94f;
                default: return 2.14f;
            }
        }

        public void Bind(Entity e, Side side, HeroId? hero, BuildingId? building, System.Func<int> soldierCountOf = null)
        {
            _e = e; _side = side;
            InitMats();
            if (e.Kind == EntityKind.Unit && e.IsSoldier)
            {
                // 士兵:程序化兵种模型(随主将风格),不建血条/光环/旗帜
                _model = BuildSoldier(hero);
                _hpFill = null; _shFill = null;
                var hb = new GameObject("HpBar");
                hb.transform.SetParent(transform, false);
                _hpRoot = hb.transform;
                hb.SetActive(false);
            }
            else
            {
                if (e.Kind == EntityKind.Unit) BuildUnit(hero.HasValue ? hero.Value : HeroId.PanShi);
                else
                {
                    BuildBuilding(building.HasValue ? building.Value : BuildingId.Tower);
                    TextTopY = _topY + 1.2f;
                }
                BuildHpBar();
                if (e.Kind == EntityKind.Unit && !e.IsSoldier && soldierCountOf != null)
                {
                    _soldierCountOf = soldierCountOf;
                    BuildCountLabel();
                }
            }
            transform.position = e.Kind == EntityKind.Building ? BoardView.WorldPos(e.Pos) : BoardView.WorldPos(e.PosF);
            if (e.Kind == EntityKind.Unit)
                transform.rotation = Quaternion.Euler(0f, side == Side.Blue ? 90f : -90f, 0f);
            // 受击模型闪白用(MPB 逐渲染器覆盖,不动共享材质)
            _modelRenderers = _model != null ? _model.GetComponentsInChildren<MeshRenderer>() : null;
        }

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

        private void InitMats()
        {
            if (_g != null) return;
            _bk = ViewAssets.Flat(new Color(0.18f, 0.18f, 0.20f));
            _g = ViewAssets.Mat(Color.green, 0f, 0.4f, Color.green, 0.8f);
            _y = ViewAssets.Mat(new Color(1f, 0.85f, 0f), 0f, 0.4f, new Color(1f, 0.85f, 0f), 0.8f);
            _r = ViewAssets.Mat(Color.red, 0f, 0.4f, Color.red, 0.8f);
            _sh = ViewAssets.Mat(new Color(0.4f, 0.8f, 1f), 0f, 0.4f, new Color(0.4f, 0.8f, 1f), 1.2f);
            _rubG = ViewAssets.Flat(new Color(0.15f, 0.14f, 0.13f));
            _gA = ViewAssets.Mat(Color.black, 0f, 0.9f, new Color(1f, 1f, 0.9f), 2.2f);
            _gB = ViewAssets.Mat(Color.black, 0f, 0.9f, new Color(0.4f, 0.4f, 0.4f), 0.7f);
        }

        public void FlashHit() { _hitT = 0.18f; }

        public void SetDead()
        {
            if (_dead) return;
            _dead = true; _deadT = 0f;
        }

        // 2026-09-09 制作人拍板:血条=阵营识别层(己方蓝/敌方红/中立灰,盾=灰),主将常显,血量读条长
        private static Material _hpMatBlue, _hpMatRed, _hpMatNeutral, _hpMatShield;
        private static void EnsureHpMats()
        {
            if (_hpMatBlue != null) return;
            _hpMatBlue = ViewAssets.Flat(new Color(0.25f, 0.55f, 1f), 0.35f);
            _hpMatRed = ViewAssets.Flat(new Color(0.98f, 0.28f, 0.22f), 0.35f);
            _hpMatNeutral = ViewAssets.Flat(new Color(0.62f, 0.62f, 0.62f), 0.35f);
            _hpMatShield = ViewAssets.Flat(new Color(0.78f, 0.80f, 0.84f), 0.35f);
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
            _hpFill.localScale = new Vector3(_w * t, _h, 1f);
            _hpFill.localPosition = new Vector3(-_w * (1f - t) * 0.5f, 0f, 0f);
        }

        public void SetFogUnitHidden(bool hidden) { _fogUnitHidden = hidden; }
        public void SetFogBuilding(bool unknown, bool stale)
        {
            _fogBuildingUnknown = unknown;
            _fogBuildingStale = stale;
        }
        public void SetFogGarrison(int? knownCount) { _fogGarrisonOverride = knownCount; }

        public void TickBillboard(Camera cam)
        {
            if (_hpRoot != null && cam != null)
                // 2026-09-09 修血条:①不可见(背对相机被剔除)→面向相机;②歪斜→以相机 up 对齐保持屏幕水平
                _hpRoot.rotation = Quaternion.LookRotation(cam.transform.position - _hpRoot.position, cam.transform.up);
        }

        public void Tick(float dt, Camera cam)
        {
            if (_e == null) return;
            bool isB = _e.Kind == EntityKind.Building;
            bool hidden = !isB && (_e.GarrisonBuildingId != 0 || _fogUnitHidden);
            bool vis = !hidden && !_rubbled && !(isB && _fogBuildingUnknown);
            if (_model != null && _model.activeSelf != vis)
            {
                // 2026-09-09 bug1 修复:雾散/出驻重现时直接落位实时坐标,
                // 禁止从雾前残留位置 Lerp 滑移(表现为"从基方向瞬移")
                if (vis && !isB) transform.position = BoardView.WorldPos(_e.PosF);
                _model.SetActive(vis); _hpRoot.gameObject.SetActive(!hidden);
            }
            if (isB && _fogBuildingUnknown)
            {
                // 建筑未知:血条与驻军点也全部隐藏
                if (_hpRoot != null && _hpRoot.gameObject.activeSelf) _hpRoot.gameObject.SetActive(false);
                if (_gr != null && _gr.activeSelf) _gr.SetActive(false);
            }
            if (!hidden && !MarchHold)
            {
                Vector3 target = isB ? BoardView.WorldPos(_e.Pos) : BoardView.WorldPos(_e.PosF);
                if (isB) transform.position = target;
                else transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-14f * dt));
                if (!(isB && _fogBuildingStale)) SetHp(_e.Hp, _e.MaxHp); // Stale=冻结血条(不刷新)
            }
            if (_hitT > 0f)
            {
                _hitT -= dt;
                if (_hpBack != null) // 士兵不建血条,无背板可闪
                {
                    float f = Mathf.Clamp01(_hitT / 0.18f);
                    var r = _hpBack.GetComponent<MeshRenderer>();
                    if (r != null && r.sharedMaterial != null)
                    {
                        Color c = Color.Lerp(new Color(0.18f, 0.18f, 0.20f), Color.white, f);
                        if (r.sharedMaterial.HasProperty("_BaseColor")) r.sharedMaterial.SetColor("_BaseColor", c);
                        else r.sharedMaterial.color = c;
                    }
                }
                // 2026-09-09 制作人拍板:受击反馈三件套之一——模型级闪白(士兵/将通用,MPB 不污染共享材质)
                if (_modelRenderers != null && _modelRenderers.Length > 0)
                {
                    if (_flashBlock == null) _flashBlock = new MaterialPropertyBlock();
                    _flashBlock.SetColor("_BaseColor", Color.white);
                    for (int i = 0; i < _modelRenderers.Length; i++)
                        if (_modelRenderers[i] != null) _modelRenderers[i].SetPropertyBlock(_flashBlock);
                    _modelFlashed = true;
                }
            }
            else if (_modelFlashed)
            {
                for (int i = 0; i < _modelRenderers.Length; i++)
                    if (_modelRenderers[i] != null) _modelRenderers[i].SetPropertyBlock(null);
                _modelFlashed = false;
            }
            if (_countLabel != null && _soldierCountOf != null)
            {
                _countLabel.gameObject.SetActive(!_fogUnitHidden);
                _countRefresh += dt;
                if (_countRefresh >= 0.5f)
                {
                    _countRefresh = 0f;
                    _countLabel.text = _soldierCountOf() + "/" + RegimentSystem.SoldiersPerGeneral;
                }
            }
            if (_anim != null && !isB)
            {
                int st = _e.State == EntityState.Moving || _e.State == EntityState.Routing ? 1
                    : (_e.State == EntityState.Attacking ? 2 : 0);
                if (_anim.GetInteger("state") != st) _anim.SetInteger("state", st);
            }
            if (_mountRoot != null)
            {
                // 骑乘 bob:行军时马体起伏,静止轻微呼吸
                bool mv = _e.State == EntityState.Moving || _e.State == EntityState.Routing;
                _bobT += dt * (mv ? 11f : 2f);
                _mountRoot.localPosition = new Vector3(0f, mv ? Mathf.Sin(_bobT) * 0.05f + 0.03f : 0f, 0f);
                ApplyRidePose(); // edit 模式无 LateUpdate,由 Tick 驱动骑姿
            }
            if (_e.Rubbled && !_rubbled) SetRubbled(true);
            if (!_e.Rubbled && _rubbled) SetRubbled(false);
            if (isB)
            {
                UpdateGarrison();
                var smoke = _model != null ? _model.GetComponentInChildren<BuildingSmokeFx>() : null;
                if (smoke != null && _e.MaxHp > 0f)
                    smoke.SetHpFraction(_e.Hp / _e.MaxHp, _e.Rubbled);
            }
            UpdateShield();
            TickBillboard(cam);
            if (_dead)
            {
                _deadT += dt;
                float s = Mathf.Max(0.01f, 1f - _deadT * 4f);
                transform.localScale = Vector3.one * s;
            }
            else transform.localScale = Vector3.one;
        }

        private void UpdateGarrison()
        {
            if (_fogBuildingUnknown) return; // 未知建筑不显示驻军点
            int f = _e.GarrisonFormal.Count, t = _e.GarrisonTemp.Count;
            int n = _fogGarrisonOverride ?? (f + t);
            if (n == _gf) return;
            _gf = n; _gt = t;
            if (_gr != null) Destroy(_gr);
            _gr = null;
            if (n == 0) return;
            _gr = new GameObject("Garrison");
            _gr.transform.SetParent(transform, false);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)Mathf.Max(1, n) * Mathf.PI * 2f;
                ViewAssets.DecoBox(i < f ? _gA : _gB,
                    new Vector3(Mathf.Cos(a) * 2.3f, _topY, Mathf.Sin(a) * 2.3f),
                    Vector3.one * 0.4f, _gr.transform, "d" + i);
            }
        }

        private void UpdateShield()
        {
            if (_shFill == null) return;
            bool show = _e.Shield > 0.01f && _e.ShieldMax > 0.01f;
            _shFill.gameObject.SetActive(show);
            if (show)
            {
                float t = Mathf.Clamp01(_e.Shield / _e.ShieldMax);
                _shFill.localScale = new Vector3(_w * t, 0.05f, 1f);
                _shFill.localPosition = new Vector3(-_w * (1f - t) * 0.5f, 0.14f, 0f);
            }
        }

        private void BuildHpBar()
        {
            var root = new GameObject("HpBar");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, _e.Kind == EntityKind.Building ? _topY + 0.9f : 2.7f, 0f);
            _hpRoot = root.transform;
            _hpBack = ViewAssets.DecoQuad(Instantiate(_bk), Vector3.zero, new Vector2(_w, _h), root.transform, "Back").transform;
            _hpFill = ViewAssets.DecoQuad(FactionHpMat(), Vector3.zero, new Vector2(_w, _h), root.transform, "Fill").transform;
            EnsureHpMats();
            _shFill = ViewAssets.DecoQuad(_hpMatShield, new Vector3(0f, 0.14f, 0f), new Vector2(_w, 0.05f), root.transform, "Shield").transform;
            _shFill.gameObject.SetActive(false);
        }

        // ===== Kenney 模型加载与组合 =====

        private GameObject NewModelRoot()
        {
            var go = new GameObject("Model");
            go.transform.SetParent(transform, false);
            return go;
        }

        private float PieceHeight(GameObject go)
        {
            bool found = false; var b = new Bounds();
            foreach (var rr in go.GetComponentsInChildren<Renderer>())
            {
                if (!found) { b = rr.bounds; found = true; }
                else b.Encapsulate(rr.bounds);
            }
            return found ? b.size.y : 0f;
        }

        private GameObject AddPiece(Transform parent, string name, float targetH, float y)
        {
            var prefab = Resources.Load<GameObject>("Models/" + name);
            if (prefab == null) { Debug.LogWarning("[EntityView] Missing model: " + name); return null; }
            var go = Instantiate(prefab, parent);
            go.name = name;
            float ph = PieceHeight(go);
            float s = ph > 0.0001f ? targetH / ph : 1f;
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(0f, y + targetH * 0.5f, 0f);
            go.transform.localRotation = Quaternion.identity;
            return go;
        }

        // ===== 2026-09-10 建筑正式模型接线:Models/Buildings/{名}.fbx + BuildingMat_{名} 直挂 =====

        private static string BuildingModelName(BuildingId id)
        {
            switch (id)
            {
                case BuildingId.Base: return "主基地";
                case BuildingId.Barracks: return "兵营";
                case BuildingId.ResourcePoint: return "资源点";
                default: return "箭塔";
            }
        }

        // 建筑材质缓存(懒加载,同 HeroMats 模式;null 也缓存避免反复 Load)
        private static readonly System.Collections.Generic.Dictionary<string, Material> BldMats
            = new System.Collections.Generic.Dictionary<string, Material>();
        private static Material BuildingMaterial(string name)
        {
            Material m;
            if (BldMats.TryGetValue(name, out m)) return m;
            m = UnityEngine.Resources.Load<Material>("Models/Buildings/BuildingMat_" + name);
            BldMats[name] = m;
            return m;
        }

        // 建筑美术件:高度归一 + 底面贴地(fbx 枢轴不定)+ 材质直挂;失败返回 null 走 Kenney 回退。
        // 不走 AddPiece:其会清 root 旋转,而 Blender 导出的 fbx root 带 X-90(Z-up→Y-up),
        // 清掉后模型躺倒且量高失真(实测兵营 6.2 目标变 10.7)。
        private GameObject AddBuildingPiece(Transform parent, string name, float targetH)
        {
            var prefab = UnityEngine.Resources.Load<GameObject>("Models/Buildings/" + name);
            if (prefab == null) { Debug.LogWarning("[EntityView] Missing building model: " + name); return null; }
            var go = Instantiate(prefab, parent);
            go.name = "Buildings/" + name;
            go.transform.localRotation = prefab.transform.rotation;
            var rr = go.GetComponentsInChildren<Renderer>();
            if (rr.Length == 0) return go;
            var wb = rr[0].bounds;
            for (int i = 1; i < rr.Length; i++) wb.Encapsulate(rr[i].bounds);
            float ph = wb.size.y;
            float s = ph > 0.0001f ? targetH / ph : 1f;
            go.transform.localScale = Vector3.one * s;
            wb = rr[0].bounds; // 缩放后重取
            for (int i = 1; i < rr.Length; i++) wb.Encapsulate(rr[i].bounds);
            // 贴地+水平居中:按包围盒最低点对齐模型根地面、包围盒中心对齐建筑原点
            go.transform.localPosition = new Vector3(parent.position.x - wb.center.x, parent.position.y - wb.min.y, parent.position.z - wb.center.z);
            var mat = BuildingMaterial(name);
            if (mat != null)
                foreach (var r in rr)
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) ms[i] = mat;
                    r.sharedMaterials = ms;
                }
            return go;
        }

        private bool TryArt(string name, float targetH)
        {
            if (AddBuildingPiece(_model.transform, name, targetH) == null) return false;
            _bldArt = true;
            return true;
        }

        private Color TeamGlowColor()
        {
            if (_side == Side.Blue) return new Color(0.4f, 0.8f, 1f);
            if (_side == Side.Red) return new Color(1f, 0.4f, 0.35f);
            return new Color(0.35f, 1f, 0.55f);
        }

        private void AddGlowDisc(Transform parent, float radius, Color? glowColor = null)
        {
            // 2026-09-10 制作人目击修复:方盒冒充圆盘(俯视正方形色块)→真圆盘网格
            Color c = glowColor ?? TeamGlowColor();
            Material glow = ViewAssets.Mat(c * 0.2f, 0f, 0.7f, c, 0.55f);
            ViewAssets.DecoDisc(glow, new Vector3(0f, 0.02f, 0f), radius * 2f, parent, "GlowDisc");
        }

        private void AddFlag(Transform parent, Vector3 pos, float height)
        {
            Color fc = _side == Side.Blue ? new Color(0.2f, 0.85f, 1f)
                : (_side == Side.Red ? new Color(1f, 0.3f, 0.25f) : new Color(0.5f, 0.75f, 0.5f));
            Material pm = ViewAssets.Flat(new Color(0.25f, 0.22f, 0.18f));
            Material fm = ViewAssets.Flat(fc);
            ViewAssets.DecoBox(pm, pos + new Vector3(0f, height * 0.5f, 0f), new Vector3(0.08f, height, 0.08f), parent, "FlagPole");
            ViewAssets.DecoBox(fm, pos + new Vector3(0.5f, height - 0.34f, 0f), new Vector3(0.9f, 0.55f, 0.06f), parent, "Flag");
        }

        private void TintRenderers(GameObject go, Color tint)
        {
            foreach (var rr in go.GetComponentsInChildren<Renderer>())
            {
                var mats = rr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = Instantiate(mats[i]);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint);
                    else m.color = m.color * tint;
                    mats[i] = m;
                }
                rr.sharedMaterials = mats;
            }
        }

        private void GreyRenderers(GameObject go)
        {
            foreach (var rr in go.GetComponentsInChildren<Renderer>())
            {
                var mats = rr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null) mats[i] = Instantiate(ViewAssets.Flat(new Color(0.62f, 0.62f, 0.60f)));
                rr.sharedMaterials = mats;
            }
        }

        // ===== 单位:英雄 → Kenney 角色映射(后续替换机甲模型只改此处)=====

        private static string HeroModelName(HeroId hero)
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
        private static Material HeroMaterial(HeroId hero)
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

        private void AttachWeapons(HeroId hero, GameObject body)
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
            AddGlowDisc(_model.transform, 0.7f, HeroAccent(hero) * 1.25f);
        }

        private void BuildBuilding(BuildingId type)
        {
            _model = NewModelRoot();
            // M8.6 占地可视化。2026-09-09 用户拍板:大面积色块改细描边框(保留占地语义);
            // flash 审查:共享同一材质实例(原 Instantiate×4 产生孤儿材质)
            Material pm = ViewAssets.Flat(_side == Side.Blue ? new Color(0.35f, 0.55f, 0.95f, 0.5f)
                : (_side == Side.Red ? new Color(0.95f, 0.4f, 0.3f, 0.5f) : new Color(0.75f, 0.75f, 0.7f, 0.5f)));
            float fw = BuildingFootprint.HalfW(_e) * 2f, fh = BuildingFootprint.HalfH(_e) * 2f, ft = 0.3f;
            ViewAssets.DecoBox(pm, new Vector3(0f, 0.02f, (fh - ft) * 0.5f), new Vector3(fw, 0.02f, ft), _model.transform, "FootN");
            ViewAssets.DecoBox(pm, new Vector3(0f, 0.02f, -(fh - ft) * 0.5f), new Vector3(fw, 0.02f, ft), _model.transform, "FootS");
            ViewAssets.DecoBox(pm, new Vector3((fw - ft) * 0.5f, 0.02f, 0f), new Vector3(ft, 0.02f, fh), _model.transform, "FootE");
            ViewAssets.DecoBox(pm, new Vector3(-(fw - ft) * 0.5f, 0.02f, 0f), new Vector3(ft, 0.02f, fh), _model.transform, "FootW");
            bool neutral = _side == Side.Neutral;
            bool fort = _e.IsFort && type == BuildingId.ResourcePoint;
            switch (type)
            {
                case BuildingId.Base:
                    // 2026-09-10 正式建筑 fbx(主基地);资源缺失回退 Kenney 塔楼组合
                    if (TryArt("主基地", 11.4f)) { }
                    else
                    {
                        AddPiece(_model.transform, "tower-square-base", 3.6f, 0f);
                        AddPiece(_model.transform, "tower-square-mid", 4.2f, 3.6f);
                        AddPiece(_model.transform, "tower-square-top-roof", 3.6f, 7.8f);
                    }
                    _topY = 11.4f; AddGlowDisc(_model.transform, 3.2f);
                    break;
                case BuildingId.Barracks:
                    if (TryArt("兵营", 6.2f)) { }
                    else
                    {
                        AddPiece(_model.transform, "tower-square-mid-door", 4.4f, 0f);
                        AddPiece(_model.transform, "tower-square-roof", 1.8f, 4.4f);
                    }
                    _topY = 6.2f; AddGlowDisc(_model.transform, 2.5f);
                    AddFlag(_model.transform, new Vector3(3.6f, 0f, 3.6f), 3.6f);
                    break;
                case BuildingId.ResourcePoint:
                    // 据点与非据点共用资源点模型(无专属据点美术);高度统一 7.0 不压扁
                    if (TryArt("资源点", 7.0f))
                    {
                        _topY = 7.0f;
                        if (fort)
                        {
                            Material gm = ViewAssets.Mat(Color.black, 0f, 0.9f, TeamGlowColor(), 2.2f);
                            for (int i = 0; i < 3; i++)
                            {
                                float a = i * Mathf.PI * 2f / 3f;
                                ViewAssets.DecoBox(gm, new Vector3(Mathf.Cos(a) * 1.7f, _topY + 0.3f, Mathf.Sin(a) * 1.7f),
                                    Vector3.one * 0.5f, _model.transform, "fortglow" + i);
                            }
                        }
                    }
                    else if (fort)
                    {
                        var stall = AddPiece(_model.transform, "stall", 4.0f, 0f);
                        _topY = 4.4f;
                        Material gm = ViewAssets.Mat(Color.black, 0f, 0.9f, TeamGlowColor(), 2.2f);
                        for (int i = 0; i < 3; i++)
                        {
                            float a = i * Mathf.PI * 2f / 3f;
                            ViewAssets.DecoBox(gm, new Vector3(Mathf.Cos(a) * 1.7f, 4.7f, Mathf.Sin(a) * 1.7f),
                                Vector3.one * 0.5f, _model.transform, "fortglow" + i);
                        }
                        if (stall != null && neutral) GreyRenderers(stall);
                    }
                    else
                    {
                        AddPiece(_model.transform, neutral ? "watermill" : "windmill", 7.0f, 0f);
                        _topY = 7.0f;
                    }
                    AddGlowDisc(_model.transform, 2.2f);
                    break;
                case BuildingId.Tower:
                default:
                    if (TryArt("箭塔", 8.6f)) { }
                    else
                    {
                        AddPiece(_model.transform, "tower-hexagon-base", 2.6f, 0f);
                        AddPiece(_model.transform, "tower-hexagon-mid", 4.0f, 2.6f);
                        AddPiece(_model.transform, "tower-hexagon-roof", 2.0f, 6.6f);
                    }
                    _topY = 8.6f; AddGlowDisc(_model.transform, 2.8f);
                    break;
            }
            if (!fort) AddFlag(_model.transform, new Vector3(6.0f, 0f, type == BuildingId.Barracks ? -1.8f : 0f), 5.0f);
            // 2026-09-10 制作人定稿:建筑分阶段烟雾(75%轻/50%偏浓/25%浓黑/摧毁)
            BuildingSmokeFx.Attach(_model, _topY, BuildingFootprint.HalfW(_e) + BuildingFootprint.HalfH(_e));
            // 正式模型材质即成品,阵营识别走旗帜/血条/占地描边(同英雄拍板);仅回退路径保留 grey/tint
            if (_bldArt) { }
            else if (neutral && type != BuildingId.ResourcePoint) GreyRenderers(_model);
            else if (!neutral)
                TintRenderers(_model, _side == Side.Blue ? new Color(0.55f, 0.72f, 1f) : new Color(1f, 0.55f, 0.45f)); // 建筑阵营 tint(红方暖、蓝方冷)
        }

        public void SetRubbled(bool on)
        {
            if (_rubbled == on || _model == null) return;
            _rubbled = on;
            if (_chunks != null) { Destroy(_chunks); _chunks = null; }
            _model.SetActive(!on);
            if (!on) return;
            _chunks = new GameObject("Rubble");
            _chunks.transform.SetParent(transform, false);
            // 2026-09-10 废墟正式模型(同建筑 fbx 流程);未挂正式模型时回退 Kenney 碎石
            if (_bldArt && AddBuildingPiece(_chunks.transform, BuildingModelName(_e.Building ?? BuildingId.Tower) + "_废墟", 4.0f) != null)
                return;
            string main = _e.Building == BuildingId.ResourcePoint ? "cart" : "siege-tower-demolished";
            var go = AddPiece(_chunks.transform, main, 4.0f, 0f);
            if (go != null) go.transform.localScale *= 0.9f;
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f + 0.7f;
                var rk = AddPiece(_chunks.transform, "rocks-small", 0.7f, 0f);
                if (rk != null) rk.transform.localPosition = new Vector3(Mathf.Cos(a) * 2.6f, 0.2f, Mathf.Sin(a) * 2.6f);
            }
            if (_chunks.transform.childCount == 0) { Destroy(_chunks); _chunks = null; }
        }
    }
}
