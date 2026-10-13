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
            go.transform.localRotation = Quaternion.identity;
            // 2026-09-14 修"英雄浮在光环上方":旧写法按 `y + targetH*0.5` 摆模型,等于**假设枢轴在模型中心**。
            // 六英雄 fbx 的枢轴在脚底 → 模型被整体抬高 targetH/2(实测 PanShi 悬空 0.93m、断峰 1.63m),
            // 脚下阵营环留在地面、人浮在空中 —— 制作人"英雄脚下光环要紧贴"的一半原因就在这。
            // 改为按包围盒最低点贴到 y:与枢轴无关,对 Kenney 居中件也等价(它们本来就对称)。
            // 2026-09-14 二次修正:蒙皮网格的 `Renderer.bounds` 是**膨胀过**的(随根骨+外扩算),
            // 用它当最低点会留下 0.2~0.75m 残余悬空(实测)。所以这里直接扫描**静止姿顶点**取最低点。
            go.transform.localPosition = Vector3.zero;
            float minY = float.MaxValue;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                float low = LowestPointLocalY(go.transform, r);
                if (!float.IsNaN(low)) minY = Mathf.Min(minY, low);
            }
            if (float.IsNaN(minY) || float.IsInfinity(minY) || minY == float.MaxValue) minY = targetH * 0.5f;
            go.transform.localPosition = new Vector3(0f, y - minY, 0f);
            return go;
        }

        /// <summary>渲染体在 `root` 局部空间里的最低点高度。优先扫网格顶点(蒙皮网格的 bounds 会膨胀),
        /// 拿不到网格时退回包围盒。</summary>
        private static float LowestPointLocalY(Transform root, Renderer r)
        {
            Mesh mesh = null;
            var smr = r as SkinnedMeshRenderer;
            if (smr != null) mesh = smr.sharedMesh;
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
            }
            if (mesh == null) return root.InverseTransformPoint(r.bounds.min).y;
            // 2026-09-14:模型若没开 Read/Write,`mesh.vertices` 会**报一条警告**再返回空数组
            // (实测:`Not allowed to access vertices on mesh 'node_0.001'`)—— 与其开着 Read/Write
            // 把每个网格的可读副本打进包(实测 +78MB),不如**先判断再读**:
            // 这些模型的"顶点最低点 vs 包围盒最低点"实测差值为 0(方块风格资产包围盒本就贴紧顶点),
            // 所以走包围盒口径**观感完全一致**、且不刷警告。将来真需要顶点级精度时,
            // 跑 `ModelReadabilityTool.EnableReadable` 打开即可(这条分支会自动生效)。
            if (!mesh.isReadable) return root.InverseTransformPoint(r.bounds.min).y;
            var vs = mesh.vertices;
            if (vs == null || vs.Length == 0) return root.InverseTransformPoint(r.bounds.min).y;
            int lowest = 0;
            for (int i = 1; i < vs.Length; i++)
                if (vs[i].y < vs[lowest].y) lowest = i;
            var world = r.transform.TransformPoint(vs[lowest]);
            return root.InverseTransformPoint(world).y;
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

        /// <summary>小型地堡(2026-09-16 制作人拍板美术出地堡前,先用程序化占位顶替箭塔兜底;
        /// fbx 落进 Resources/Models/Buildings/地堡 后经 TryArt 同路径自动接管)。
        /// 规格:占地 3×3 米(BuildingFootprint 堡垒半宽 1.5)、低矮盒体+挑檐+四向射击口,
        /// 无废墟态(堡垒死亡直接移除)。命名 BunkerBody 供实机自检判据辨认。</summary>
        private void BuildFortBunker()
        {
            _model = NewModelRoot();
            Material pm = ViewAssets.Flat(FootColor(_side));
            _footMat = pm;
            float fw = BuildingFootprint.HalfW(_e) * 2f, fh = BuildingFootprint.HalfH(_e) * 2f, ft = 0.3f;
            ViewAssets.DecoBox(pm, new Vector3(0f, 0.02f, (fh - ft) * 0.5f), new Vector3(fw, 0.02f, ft), _model.transform, "FootN");
            ViewAssets.DecoBox(pm, new Vector3(0f, 0.02f, -(fh - ft) * 0.5f), new Vector3(fw, 0.02f, ft), _model.transform, "FootS");
            ViewAssets.DecoBox(pm, new Vector3((fw - ft) * 0.5f, 0.02f, 0f), new Vector3(ft, 0.02f, fh), _model.transform, "FootE");
            ViewAssets.DecoBox(pm, new Vector3(-(fw - ft) * 0.5f, 0.02f, 0f), new Vector3(ft, 0.02f, fh), _model.transform, "FootW");

            if (TryArt("地堡", 2.0f)) { }
            else
            {
                // 混凝土主体 + 挑檐顶板 + 四向射击口(暗色窄条),体量远小于箭塔(顶高 ~2.0 vs 8.6)
                Material body = ViewAssets.Mat(new Color(0.34f, 0.33f, 0.30f), 0.05f, 0.55f, new Color(0f, 0f, 0f), 0f);
                ViewAssets.DecoBox(body, new Vector3(0f, 0.8f, 0f), new Vector3(2.7f, 1.6f, 2.7f), _model.transform, "BunkerBody");
                ViewAssets.DecoBox(body, new Vector3(0f, 1.72f, 0f), new Vector3(3.1f, 0.22f, 3.1f), _model.transform, "BunkerRoof");
                Material port = ViewAssets.Flat(new Color(0.06f, 0.06f, 0.07f));
                ViewAssets.DecoBox(port, new Vector3(0f, 1.0f, 1.36f), new Vector3(1.2f, 0.34f, 0.06f), _model.transform, "BunkerPortN");
                ViewAssets.DecoBox(port, new Vector3(0f, 1.0f, -1.36f), new Vector3(1.2f, 0.34f, 0.06f), _model.transform, "BunkerPortS");
                ViewAssets.DecoBox(port, new Vector3(1.36f, 1.0f, 0f), new Vector3(0.06f, 0.34f, 1.2f), _model.transform, "BunkerPortE");
                ViewAssets.DecoBox(port, new Vector3(-1.36f, 1.0f, 0f), new Vector3(0.06f, 0.34f, 1.2f), _model.transform, "BunkerPortW");
            }
            _topY = 2.0f;
            AddGlowDisc(_model.transform, 1.9f);
            BuildingSmokeFx.Attach(_model, _topY, BuildingFootprint.HalfW(_e) + BuildingFootprint.HalfH(_e));
            if (_side == Side.Neutral) GreyRenderers(_model);
            else TintRenderers(_model, _side == Side.Blue ? new Color(0.55f, 0.72f, 1f) : new Color(1f, 0.55f, 0.45f));
        }

        private void BuildBuilding(BuildingId type)
        {
            _model = NewModelRoot();
            // M8.6 占地可视化。2026-09-09 用户拍板:大面积色块改细描边框(保留占地语义);
            // flash 审查:共享同一材质实例(原 Instantiate×4 产生孤儿材质)
            Material pm = ViewAssets.Flat(FootColor(_side));
            _footMat = pm;   // 占领/易主后由 SyncSide → RetintForSide 按新阵营重上色
            float fw = BuildingFootprint.HalfW(_e) * 2f, fh = BuildingFootprint.HalfH(_e) * 2f, ft = 0.3f;
            ViewAssets.DecoBox(pm, new Vector3(0f, 0.02f, (fh - ft) * 0.5f), new Vector3(fw, 0.02f, ft), _model.transform, "FootN");
            ViewAssets.DecoBox(pm, new Vector3(0f, 0.02f, -(fh - ft) * 0.5f), new Vector3(fw, 0.02f, ft), _model.transform, "FootS");
            ViewAssets.DecoBox(pm, new Vector3((fw - ft) * 0.5f, 0.02f, 0f), new Vector3(ft, 0.02f, fh), _model.transform, "FootE");
            ViewAssets.DecoBox(pm, new Vector3(-(fw - ft) * 0.5f, 0.02f, 0f), new Vector3(ft, 0.02f, fh), _model.transform, "FootW");
            bool neutral = _side == Side.Neutral;
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
                    break;
                case BuildingId.ResourcePoint:
                    // 据点与非据点共用资源点模型(无专属据点美术);高度统一 7.0 不压扁
                    // 2026-09-16 三十七轮:旧 fort 分支已删 —— 堡垒(Building=null)现在走 BuildFortBunker,
                    // 不再借道资源点模型(fortglow 装饰随之移除)。
                    if (TryArt("资源点", 7.0f)) { _topY = 7.0f; }
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
            // 2026-09-13 旗帜统一由 EntityView.PlaceFlag 插在建筑顶上(按模型包围盒定位、随阵营变色),
            // 原来这里立在建筑旁边的地面上,已移除。
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
