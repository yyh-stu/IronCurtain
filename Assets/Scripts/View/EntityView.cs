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
        private Material _bk, _gA, _gB;
        // 2026-09-13 阵营变更(占领/易主/被迫转中立)后要重上色的对象:占地描边框 + 旗帜布面
        private Material _footMat, _flagMat;
        // 2026-09-13 阵营光环:常驻阵营色,选中整环换绿加粗(SelectionSystem 切换,不叠第二个环)
        private GameObject _glowRing, _glowRingSel;

        // M6.1 战争迷雾门控状态
        private bool _fogUnitHidden;
        private bool _fogBuildingUnknown;
        private bool _fogBuildingStale;
        private int? _fogGarrisonOverride;

        // M8.6b 行军组件接管位:为 true 时 Tick 跳过位置同步(外部组件驱动 transform)
        public bool MarchHold;

        // F-A03(2026-09-19 修订 II):朝向系统
        // GPT 复核两处遗漏:①攻击时仍回默认±90°(侧/后方接敌朝错方向);②静止时回默认(来回摆动)
        // 修正:持有 _faceYaw,只在有新输入时更新,静止/无目标保持最近有效方向
        private float _faceYaw;
        private bool _hasFaceYaw;
        private SimVec2 _lastPosF;
        private bool _hasLastPos;

        /// <summary>F-A03:全局 Sim 引用(BoardView.Init 设置;EntityView 查攻击者位置)</summary>
        public static SimCore SimRef;

        // M6.2 飘字顶部锚点高度(单位默认 2.9;建筑在 Bind 时覆盖)
        public float TextTopY = 2.9f;
        private float _hitT;
        private MeshRenderer[] _modelRenderers;
        private bool _modelFlashed;
        private MaterialPropertyBlock _flashBlock;

        // M6.0f 六英雄模型接线:动画驱动与配置(懒加载:Unity 禁止静态初始化器调 Resources.Load)
        private Animator _anim;
        private long _lastAttackSeq = -1;   // A01 修订:上次消费的攻击序号(替代旧冷却比较)

        public void Bind(Entity e, Side side, HeroId? hero, BuildingId? building, System.Func<int> soldierCountOf = null)
        {
            _e = e; _side = side;
            _lastAttackSeq = e.AttackSeq;   // GPT 复核:首帧不误触发(0!=−1 → 多余的一次 SetTrigger)
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
                    // 2026-09-16 三十七轮(1c):堡垒(Building=null)不再落进箭塔兜底,走小型地堡
                    if (e.IsFort) BuildFortBunker();
                    else BuildBuilding(building.HasValue ? building.Value : BuildingId.Tower);
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
            {
                transform.rotation = Quaternion.Euler(0f, side == Side.Blue ? 90f : -90f, 0f);
                // F-A03 修订 III:绑定时初始化位置——否则首帧 delta=PosF-(0,0) 会误判为大位移
                _lastPosF = e.PosF; _hasLastPos = true;
            }
            // 2026-09-13 旗帜必须在落位之后建:它按模型包围盒顶面定位,提前建会读到原点。
            // 顶面高度顺便缓存给塔的弹道起点用(缓存必须在插旗之前,否则把旗杆算进去)。
            if (e.Kind == EntityKind.Building) { CacheRoofY(); PlaceFlag(); }
            // 受击模型闪白用(MPB 逐渲染器覆盖,不动共享材质)
            _modelRenderers = _model != null ? _model.GetComponentsInChildren<MeshRenderer>() : null;
        }

        /// <summary>本视图模型在**自身局部空间**里的顶面高度(血条/飘字锚点用)。
        /// 只认真实网格、排除占地描边与发光装饰(同 CacheRoofY 的口径)。</summary>
        private float ModelTopLocalY()
        {
            if (_model == null) return _topY;
            float top = float.MinValue;
            foreach (var r in _model.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !(r is MeshRenderer)) continue;
                var n = r.gameObject.name;
                if (n.StartsWith("Foot") || n.StartsWith("Glow") || n.StartsWith("fortglow")
                    || n.StartsWith("Flag") || n.StartsWith("Garrison") || n.StartsWith("HpBar")) continue;
                float y = transform.InverseTransformPoint(r.bounds.max).y;
                if (float.IsNaN(y) || float.IsInfinity(y)) continue;
                if (y > top) top = y;
            }
            return top == float.MinValue ? _topY : top;
        }

        private void InitMats()
        {
            if (_bk != null) return;
            _bk = ViewAssets.Unlit(new Color(0.18f, 0.18f, 0.20f));
            // 2026-09-13 去廉价感:驻军点原为 albedo 纯黑 + emission 2.2 的发光方块(亮到发白、像调试体),
            // 现给实体底色 + 压暗自发光,并改用扁片(见 UpdateGarrison)。
            _gA = ViewAssets.Mat(new Color(0.32f, 0.26f, 0.14f), 0f, 0.35f, new Color(1f, 0.87f, 0.55f), 0.75f);
            _gB = ViewAssets.Mat(new Color(0.24f, 0.24f, 0.26f), 0f, 0.35f, new Color(0.55f, 0.55f, 0.58f), 0.30f);
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
            SyncSide();
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

                // F-A03(2026-09-20 修订 III):朝向三档——攻击朝 CurrentTargetId(真实选定目标,
                // 非 LastAttackerId"谁打了我")、移动朝行进方向、静止保持最近有效方向。
                // 首帧初始化在 Bind() 中完成,防止 delta=PosF-(0,0) 误判。
                if (!isB)
                {
                    var delta = _e.PosF - _lastPosF;
                    _lastPosF = _e.PosF; _hasLastPos = true;

                    if (_e.State == EntityState.Attacking && _e.CurrentTargetId != 0 && SimRef != null)
                    {
                        // 攻击:面朝 Sim 本 tick 实际选定的攻击目标(CurrentTargetId → 对方位置)
                        var foe = SimRef.GetEntity(_e.CurrentTargetId);
                        if (foe != null && foe.State != EntityState.Dead)
                        {
                            var toFoe = foe.PosF - _e.PosF;
                            if (toFoe.Magnitude > 0.01f)
                            {
                                _faceYaw = Mathf.Atan2(toFoe.X, -toFoe.Y) * Mathf.Rad2Deg;
                                _hasFaceYaw = true;
                            }
                        }
                    }
                    else if (delta.Magnitude > 0.001f)
                    {
                        // 移动:面朝行进方向(Sim dx,dy → Unity dx,0,-dy → yaw)
                        _faceYaw = Mathf.Atan2(delta.X, -delta.Y) * Mathf.Rad2Deg;
                        _hasFaceYaw = true;
                    }
                    // 静止且无目标:保持 _faceYaw 不变(不回默认——修"来回摆动")

                    if (_hasFaceYaw)
                    {
                        float currentYaw = transform.eulerAngles.y;
                        if (Mathf.Abs(Mathf.DeltaAngle(currentYaw, _faceYaw)) > 0.5f)
                        {
                            float newYaw = Mathf.LerpAngle(currentYaw, _faceYaw, dt * 8f);
                            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
                        }
                    }
                }
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
                // A01 修订(2026-09-20 GPT①):改用 AttackSeq 序号消费,替代旧冷却比较——
                // 旧 `cd >= _lastCd` 在两个画面帧之间冷却没变也误触发,动画反复重启;
                // 序号只在 Sim 真实发射攻击时自增,每帧最多消费一次。
                if (_e.AttackSeq != _lastAttackSeq)
                {
                    _lastAttackSeq = _e.AttackSeq;
                    _anim.SetTrigger("attack");   // 新的一击 → 从第一帧重播
                }
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
                // 2026-09-13:立方体改扁片(0.26×0.08),不再是一圈悬浮发光方块
                ViewAssets.DecoBox(i < f ? _gA : _gB,
                    new Vector3(Mathf.Cos(a) * 2.3f, _topY, Mathf.Sin(a) * 2.3f),
                    new Vector3(0.26f, 0.08f, 0.26f), _gr.transform, "d" + i);
            }
        }

        private Color TeamGlowColor()
        {
            if (_side == Side.Blue) return new Color(0.4f, 0.8f, 1f);
            if (_side == Side.Red) return new Color(1f, 0.4f, 0.35f);
            return new Color(0.35f, 1f, 0.55f);
        }

        /// <summary>旗帜布面色(建筑与基地共用)。</summary>
        private static Color FlagColor(Side s)
        {
            if (s == Side.Blue) return new Color(0.2f, 0.85f, 1f);
            if (s == Side.Red) return new Color(1f, 0.3f, 0.25f);
            return new Color(0.5f, 0.75f, 0.5f);
        }

        /// <summary>建筑占地描边色(细框,保留占地语义)。</summary>
        private static Color FootColor(Side s)
        {
            if (s == Side.Blue) return new Color(0.35f, 0.55f, 0.95f, 0.5f);
            if (s == Side.Red) return new Color(0.95f, 0.4f, 0.3f, 0.5f);
            return new Color(0.75f, 0.75f, 0.7f, 0.5f);
        }

        /// <summary>
        /// 2026-09-13 制作人报"我方占领箭塔后血条颜色和框的颜色都没变":
        /// 根因 = Sim 是**原地**改 `Entity.Side`(`InstantCaptureSystem`/`SiegeSystem`/`DestroySystem`),
        /// 而 `_side` 原来只在 `Bind` 时赋值一次 → 视图永远停在旧阵营。
        /// 这里每帧比一次枚举,变了就按新阵营重上色(建筑结构不重上色是既有设计:
        /// 阵营识别走旗帜/血条/占地描边)。
        /// </summary>
        private void SyncSide()
        {
            if (_e.Side == _side) return;
            _side = _e.Side;
            RetintForSide();
        }

        private void RetintForSide()
        {
            // 血条填充:换成新阵营的共享材质(蓝/红/中立各一份静态材质)
            if (_hpFill != null)
            {
                var hpMr = _hpFill.GetComponent<MeshRenderer>();
                if (hpMr != null) hpMr.sharedMaterial = FactionHpMat();
            }
            // 占地描边框
            if (_footMat != null) _footMat.SetColor("_BaseColor", FootColor(_side));
            // 旗帜布面
            if (_flagMat != null) _flagMat.SetColor("_BaseColor", FlagColor(_side));
            // 脚下阵营光环:常驻态换色;选中态(绿)不动
            if (_glowRing != null && _glowRingSel != null && !_glowRingSel.activeSelf)
            {
                var mr = _glowRing.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_BaseColor"))
                    mr.sharedMaterial.SetColor("_BaseColor", TeamGlowColor());
            }
        }

        // 2026-09-13 制作人拍板:光环"平时常驻阵营色,选中变绿色"。两个网格建一次、互斥显示,
        // 运行时只是 SetActive 切换 —— 观感上是同一个环被点亮,不是叠加第二个环。
        private const float GlowWidthFloor = 0.03f;   // 线宽绝对下限(米),消除 5mm 细到闪烁
        private static readonly Color GlowSelColor = new Color(0.25f, 0.95f, 0.38f); // 选中绿(去自发光)

        private void AddGlowDisc(Transform parent, float radius, Color? glowColor = null)
        {
            // 2026-09-10 制作人拍板:农药样式阵营环——阵营色细环;环内填充盘已彻底删除(多次反馈)
            // 2026-09-13:线宽 5mm 过细 → 绝对下限 0.03m;仍为 Unlit 纯色(不自发光)
            Color c = TeamGlowColor();
            _glowRing = ViewAssets.DecoRing(ViewAssets.Unlit(c), new Vector3(0f, 0.02f, 0f), radius,
                Mathf.Max(radius * 0.0075f, GlowWidthFloor), parent, "GlowRing");
            if (_e == null || _e.Kind != EntityKind.Unit) return; // 只有将兵参与选中
            _glowRingSel = ViewAssets.DecoRing(ViewAssets.Unlit(GlowSelColor), new Vector3(0f, 0.02f, 0f),
                radius * 1.15f, GlowWidthFloor * 1.6f, parent, "GlowRingSel");
            _glowRingSel.SetActive(false);
        }

        /// <summary>选中态:脚下的阵营环整体换成绿色并加粗(由 SelectionSystem 驱动)。</summary>
        public void SetSelected(bool on)
        {
            if (_glowRingSel == null) return;
            if (_glowRing != null) _glowRing.SetActive(!on);
            _glowRingSel.SetActive(on);
        }

        /// <summary>旗帜:旗杆 + 布面。布面材质留引用,占领易主时随阵营重上色。</summary>
        private void AddFlag(Transform parent, Vector3 pos, float height)
        {
            Material pm = ViewAssets.Flat(new Color(0.25f, 0.22f, 0.18f));
            _flagMat = ViewAssets.Flat(FlagColor(_side));
            ViewAssets.DecoBox(pm, pos + new Vector3(0f, height * 0.5f, 0f), new Vector3(0.08f, height, 0.08f), parent, "FlagPole");
            ViewAssets.DecoBox(_flagMat, pos + new Vector3(0.5f, height - 0.34f, 0f), new Vector3(0.9f, 0.55f, 0.06f), parent, "Flag");
        }

        private const float FlagPoleHeight = 1.6f;   // 2026-09-13 制作人"太高了":3.0 → 1.6
        private float _roofY = 1.0f;

        /// <summary>建筑模型顶面高度(局部,米)。塔的弹道起点这类表现要用。</summary>
        public float RoofY => _roofY;

        /// <summary>
        /// 按模型实际包围盒缓存顶面高度(换算到模型局部空间)。必须在 `Bind` 里 transform
        /// 落位**之后**调用 —— 提前调读到的是原点坐标。也必须在插旗**之前**调,否则旗杆会把自己算进去。
        /// </summary>
        private void CacheRoofY()
        {
            // 2026-09-13 制作人"旗帜还是太高了" → 找到两个几何缺陷:
            // ①杆底高度取的是**全模型最高点**,水平位置却取**占地边缘一角**,而角上屋顶通常比最高点低
            //   → 杆底悬空、看着又高又飘;②杆本身 3m 太长。
            // 改为:锚点取**最高的那个渲染体**的顶面与它的 XZ 中心(杆底必落在实体上),
            // 并朝建筑中心内缩一点避免压住檐角;杆长缩短(见 FlagPoleHeight)。
            // 2026-09-14 再加三道路障(制作人已两轮报"飞天",不能再靠"这次应该没问题"):
            //   a) 只认真实网格(MeshRenderer)——粒子的包围盒会随发射膨胀;
            //   b) 排除装饰件(发光环/据点光点/旗子自身),它们不是屋顶;
            //   c) 夹紧到 [0.5, _topY+1]:无论模型怎么导出失真,旗杆也不可能高过建筑本该的高度。
            float top = 1.0f;
            Vector3 anchor = Vector3.zero;
            bool any = false;
            if (_model != null)
            {
                var mr = _model.transform;
                foreach (var r in _model.GetComponentsInChildren<Renderer>())
                {
                    if (r == null) continue;
                    // 2026-09-13 制作人"旗帜飞在万米高空":建筑上挂着 BuildingSmokeFx(**粒子**),
                    // 粒子包围盒会随发射膨胀到很大 → 屋顶被算成"天上的粒子高度" → 旗子飞天。
                    // 屋顶只认真实网格(MeshRenderer),并把占地描边排除在外。
                    if (!(r is MeshRenderer)) continue;
                    var n = r.gameObject.name;
                    if (n.StartsWith("Foot") || n.StartsWith("Glow") || n.StartsWith("fortglow")
                        || n.StartsWith("Flag") || n.StartsWith("Garrison") || n.StartsWith("HpBar")) continue;
                    float y = mr.InverseTransformPoint(r.bounds.max).y;
                    if (float.IsNaN(y) || float.IsInfinity(y)) continue;
                    if (!any || y > top)
                    {
                        top = y;
                        anchor = mr.InverseTransformPoint(r.bounds.center);
                        any = true;
                    }
                }
            }
            float ceil = _topY + 1f;                       // 建筑美术件按 _topY 归一,顶面不可能高出 1 米以上
            if (!any) top = _topY;                         // 什么都没量到 → 退回该建筑的建模目标高度
            _roofY = Mathf.Clamp(top, 0.5f, ceil);
            if (top > ceil)
                Debug.LogWarning("[EntityView] 屋顶包围盒异常 top=" + top.ToString("F2")
                    + " 已夹紧到 " + _roofY.ToString("F2") + "(建筑 " + (_e != null ? _e.Building.ToString() : "-") + ")");
            _flagAnchorLocal = any
                ? new Vector3(anchor.x * 0.75f, 0f, anchor.z * 0.75f)   // 0.75:从中心点略朝楼体收
                : Vector3.zero;
        }

        /// <summary>旗杆落点(模型局部空间 XZ)与底面高度:落在最高结构的顶上,不再悬空。</summary>
        private Vector3 _flagAnchorLocal;

        /// <summary>
        /// 2026-09-13 制作人:旗子要插在**建筑上面**并随阵营变色(原来立在建筑旁边的地面上)。
        /// 定位用 `CacheRoofY` 缓存的顶面高度,不用 `_topY` —— 走正式美术的建筑分支不写 `_topY`,
        /// 用它会把旗杆埋进模型里。
        /// </summary>
        private void PlaceFlag()
        {
            if (_e == null || _e.IsFort) return;   // 据点(堡垒)不插旗,沿用既有口径
            AddFlag(_model != null ? _model.transform : transform,
                new Vector3(_flagAnchorLocal.x, _roofY, _flagAnchorLocal.z), FlagPoleHeight);
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
