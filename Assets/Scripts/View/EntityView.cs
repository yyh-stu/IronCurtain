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
        private Transform _hpRoot, _hpFill, _shFill;
        private float _w = 1.7f, _h = 0.26f, _deadT = -1f, _topY = 3.5f;
        private bool _dead, _rubbled;
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

        public void Bind(Entity e, Side side, HeroId? hero, BuildingId? building, System.Func<int> soldierCountOf = null)
        {
            _e = e; _side = side;
            InitMats();
            if (e.Kind == EntityKind.Unit && e.IsSoldier)
            {
                // 士兵:轻量占位模型,不建血条/光环/旗帜
                _model = BuildSoldier();
                _hpFill = null; _shFill = null;
                var hb = new GameObject("HpBar");
                hb.transform.SetParent(transform, false);
                _hpRoot = hb.transform;
                hb.SetActive(false);
            }
            else
            {
                if (e.Kind == EntityKind.Unit) BuildUnit(hero.HasValue ? hero.Value : HeroId.PanShi);
                else BuildBuilding(building.HasValue ? building.Value : BuildingId.Tower);
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
        }

        private GameObject BuildSoldier()
        {
            var root = NewModelRoot();
            var sm = new GameObject("SoldierModel");
            sm.transform.SetParent(root.transform, false);
            Material mat = _side == Side.Blue ? ViewAssets.Flat(new Color(0.25f, 0.42f, 0.72f), 0.3f)
                : (_side == Side.Red ? ViewAssets.Flat(new Color(0.72f, 0.30f, 0.26f), 0.3f)
                : ViewAssets.Flat(new Color(0.55f, 0.55f, 0.52f), 0.3f));
            // 躯干
            ViewAssets.DecoBox(mat, new Vector3(0f, 0.35f, 0f), new Vector3(0.36f, 0.5f, 0.24f), sm.transform, "Torso");
            // 头
            ViewAssets.DecoBox(mat, new Vector3(0f, 0.72f, 0f), Vector3.one * 0.22f, sm.transform, "Head");
            // 武器棒
            ViewAssets.DecoBox(mat, new Vector3(0f, 0.5f, 0.24f), new Vector3(0.05f, 0.06f, 0.7f), sm.transform, "Weapon");
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

        public void FlashHit() { }

        public void SetDead()
        {
            if (_dead) return;
            _dead = true; _deadT = 0f;
        }

        public void SetHp(float hp, float maxHp)
        {
            if (_hpFill == null) return;
            float t = maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f;
            bool show = _e.Kind == EntityKind.Building || t < 0.999f;
            _hpRoot.gameObject.SetActive(show);
            _hpFill.localScale = new Vector3(_w * t, _h, 1f);
            _hpFill.localPosition = new Vector3(-_w * (1f - t) * 0.5f, 0f, 0f);
            _hpFill.GetComponent<MeshRenderer>().sharedMaterial = t > 0.5f ? _g : (t > 0.25f ? _y : _r);
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
                _hpRoot.rotation = Quaternion.LookRotation(_hpRoot.position - cam.transform.position);
        }

        public void Tick(float dt, Camera cam)
        {
            if (_e == null) return;
            bool isB = _e.Kind == EntityKind.Building;
            bool hidden = !isB && (_e.GarrisonBuildingId != 0 || _fogUnitHidden);
            bool vis = !hidden && !_rubbled && !(isB && _fogBuildingUnknown);
            if (_model != null && _model.activeSelf != vis) { _model.SetActive(vis); _hpRoot.gameObject.SetActive(!hidden); }
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
            if (_e.Rubbled && !_rubbled) SetRubbled(true);
            if (!_e.Rubbled && _rubbled) SetRubbled(false);
            if (isB) UpdateGarrison();
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
            ViewAssets.DecoQuad(_bk, Vector3.zero, new Vector2(_w, _h), root.transform, "Back");
            _hpFill = ViewAssets.DecoQuad(_g, Vector3.zero, new Vector2(_w, _h), root.transform, "Fill").transform;
            _shFill = ViewAssets.DecoQuad(_sh, new Vector3(0f, 0.14f, 0f), new Vector2(_w, 0.05f), root.transform, "Shield").transform;
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

        private Color TeamGlowColor()
        {
            if (_side == Side.Blue) return new Color(0.4f, 0.8f, 1f);
            if (_side == Side.Red) return new Color(1f, 0.4f, 0.35f);
            return new Color(0.35f, 1f, 0.55f);
        }

        private void AddGlowDisc(Transform parent, float radius)
        {
            Material glow = ViewAssets.Mat(TeamGlowColor() * 0.32f, 0f, 0.7f, TeamGlowColor(), 0.55f);
            ViewAssets.DecoBox(glow, new Vector3(0f, 0.02f, 0f), new Vector3(radius * 2f, 0.025f, radius * 2f), parent, "GlowDisc");
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
                case HeroId.PanShi: return "character-a";
                case HeroId.ZhenYue: return "character-b";
                case HeroId.DuanFeng: return "character-c";
                case HeroId.ZhuFeng: return "character-d";
                case HeroId.ZhuiRi: return "character-e";
                case HeroId.XingLuo:
                default: return "character-f";
            }
        }

        private void BuildUnit(HeroId hero)
        {
            _model = NewModelRoot();
            var body = AddPiece(_model.transform, HeroModelName(hero), 2.4f, 0f);
            if (body != null)
                TintRenderers(body, _side == Side.Blue ? new Color(0.6f, 0.8f, 1f)
                    : (_side == Side.Red ? new Color(1f, 0.6f, 0.55f) : Color.white));
            AddGlowDisc(_model.transform, 1.15f);
        }

        private void BuildBuilding(BuildingId type)
        {
            _model = NewModelRoot();
            // M8.6 占地底板:半透明阵营色,占地可视化(建筑多格占用)
            Material pm = ViewAssets.Flat(_side == Side.Blue ? new Color(0.35f, 0.55f, 0.95f, 0.35f)
                : (_side == Side.Red ? new Color(0.95f, 0.4f, 0.3f, 0.35f) : new Color(0.75f, 0.75f, 0.7f, 0.35f)));
            ViewAssets.DecoBox(Instantiate(pm), new Vector3(0f, 0.015f, 0f),
                new Vector3(BuildingFootprint.HalfW(_e) * 2f, 0.02f, BuildingFootprint.HalfH(_e) * 2f), _model.transform, "Footprint");
            bool neutral = _side == Side.Neutral;
            bool fort = _e.IsFort && type == BuildingId.ResourcePoint;
            switch (type)
            {
                case BuildingId.Base:
                    AddPiece(_model.transform, "tower-square-base", 3.6f, 0f);
                    AddPiece(_model.transform, "tower-square-mid", 4.2f, 3.6f);
                    AddPiece(_model.transform, "tower-square-top-roof", 3.6f, 7.8f);
                    _topY = 11.4f; AddGlowDisc(_model.transform, 7.2f);
                    break;
                case BuildingId.Barracks:
                    AddPiece(_model.transform, "tower-square-mid-door", 4.4f, 0f);
                    AddPiece(_model.transform, "tower-square-roof", 1.8f, 4.4f);
                    _topY = 6.2f; AddGlowDisc(_model.transform, 5.0f);
                    AddFlag(_model.transform, new Vector3(3.6f, 0f, 3.6f), 3.6f);
                    break;
                case BuildingId.ResourcePoint:
                    if (fort)
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
                    AddGlowDisc(_model.transform, 4.4f);
                    break;
                case BuildingId.Tower:
                default:
                    AddPiece(_model.transform, "tower-hexagon-base", 2.6f, 0f);
                    AddPiece(_model.transform, "tower-hexagon-mid", 4.0f, 2.6f);
                    AddPiece(_model.transform, "tower-hexagon-roof", 2.0f, 6.6f);
                    _topY = 8.6f; AddGlowDisc(_model.transform, 5.6f);
                    break;
            }
            if (!fort) AddFlag(_model.transform, new Vector3(6.0f, 0f, type == BuildingId.Barracks ? -1.8f : 0f), 5.0f);
            if (neutral && type != BuildingId.ResourcePoint) GreyRenderers(_model);
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
