using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public sealed partial class EntityView : MonoBehaviour
    {
        // AI 管线士兵模型接入后改为加载 Resources 模型
        private static bool UseSoldierArtPlaceholder = true;

        private Entity _e;
        private Side _side;
        private GameObject _model, _gr, _chunks;
        private Transform _hpRoot;
        // 2026-09-10 制作人拍板:血条农药样式——固定长约 2.5 英雄宽(2.4m)、每 10 点血一条刻度线
        private float _w = 2.4f, _h = 0.21f, _deadT = -1f, _topY = 3.5f;
        private bool _dead, _rubbled;
        // 2026-09-10 建筑正式 fbx 已挂(BuildingMat 直挂,不走 grey/tint 回退)
        private bool _bldArt;
        private int _gf = -1, _gt = -1;
        private Material _g, _y, _r, _bk, _sh, _rubG, _gA, _gB;

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

        // M6.0f 六英雄模型接线:动画驱动与配置(懒加载:Unity 禁止静态初始化器调 Resources.Load)
        private Animator _anim;

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

        private void InitMats()
        {
            if (_g != null) return;
            _bk = ViewAssets.Unlit(new Color(0.18f, 0.18f, 0.20f));
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
            // 2026-09-10 制作人拍板:迷雾只隐藏敌方将兵单位;建筑永远可见可点(否则没法设进攻偏好)
            bool vis = !hidden && !_rubbled;
            if (_model != null && _model.activeSelf != vis)
            {
                // 2026-09-09 bug1 修复:雾散/出驻重现时直接落位实时坐标,
                // 禁止从雾前残留位置 Lerp 滑移(表现为"从基方向瞬移")
                if (vis && !isB) transform.position = BoardView.WorldPos(_e.PosF);
                _model.SetActive(vis); _hpRoot.gameObject.SetActive(!hidden);
            }
            if (!hidden && !MarchHold)
            {
                Vector3 target = isB ? BoardView.WorldPos(_e.Pos) : BoardView.WorldPos(_e.PosF);
                if (isB) transform.position = target;
                else transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-14f * dt));
                SetHp(_e.Hp, _e.MaxHp);
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

        private Color TeamGlowColor()
        {
            if (_side == Side.Blue) return new Color(0.4f, 0.8f, 1f);
            if (_side == Side.Red) return new Color(1f, 0.4f, 0.35f);
            return new Color(0.35f, 1f, 0.55f);
        }

        private void AddGlowDisc(Transform parent, float radius, Color? glowColor = null)
        {
            // 2026-09-10 制作人拍板:农药样式阵营环——阵营色细环+超淡同色填充(不再用个体主色)
            // 2026-09-10 制作人拍板:环细到 1/8;环内填充盘彻底删除(多次反馈)
            Color c = TeamGlowColor();
            Material ringMat = ViewAssets.Unlit(c);
            ViewAssets.DecoRing(ringMat, new Vector3(0f, 0.02f, 0f), radius, radius * 0.0075f, parent, "GlowRing");
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
    }
}
