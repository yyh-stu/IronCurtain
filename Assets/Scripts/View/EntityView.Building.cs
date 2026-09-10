using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    public sealed partial class EntityView
    {
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
