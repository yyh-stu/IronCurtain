using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>
    /// M3 C06 四负面状态头顶图标 + M4 X00 海克斯激活横幅。
    /// 状态图标:每 0.25s 扫描可见己方单位的 StatusEffects,在头顶挂小色块(MPB 着色,池化);
    /// 一个英雄最多同时显示两枚关键状态图标(README §4:其余用选中详情承接)。
    /// 海克斯横幅:释放成功后 0.8s 顶部横幅(卡名+阵营色,不锁操作、双方分行)。
    /// </summary>
    public sealed class StatusIconSystem : MonoBehaviour
    {
        private SimCore _sim;
        private Side _player;
        private float _nextScan;
        private readonly Dictionary<int, List<GameObject>> _icons = new Dictionary<int, List<GameObject>>();
        private VfxPool _iconPool;          // 增益/冲锋(通用方片)
        private VfxPool _slowPool, _silencePool, _woundPool, _fragilePool;  // Q09:四负面各用形状池
        private static Material _iconMat;
        private Label _hexBanner;
        private float _bannerUntil;

        /// <summary>Q09:状态图标信息(池+色彩;同一枚位先负面后增益)。</summary>
        private struct StatusIconInfo
        {
            public VfxPool Pool;
            public Color Color;
        }

        // 四负面对应色(C06 规格口径)
        private static readonly Color SlowColor = new Color(0.35f, 0.60f, 1.00f);   // 迟滞=蓝
        private static readonly Color SilenceColor = new Color(0.65f, 0.40f, 1.00f); // 沉默=紫
        private static readonly Color WoundColor = new Color(1.00f, 0.35f, 0.30f);   // 重伤=红
        private static readonly Color FragileColor = new Color(1.00f, 0.65f, 0.20f); // 脆弱=橙

        // 海克斯增益色(M4 X01-X08;负面优先级高于增益,同一枚位先填负面)
        private static readonly Color SteelTideBuff = new Color(0.55f, 0.65f, 0.85f);  // 钢铁洪流=冷钢
        private static readonly Color VengeanceBuff = new Color(0.85f, 0.30f, 0.25f);  // 陷阵之志=战旗红
        private static readonly Color LightMobBuff = new Color(0.40f, 0.80f, 0.70f);   // 轻装机动=风青
        private static readonly Color IronWallBuff = new Color(0.90f, 0.75f, 0.35f);   // 铁壁防线=暖金
        private static readonly Color HoldFastBuff = new Color(0.95f, 0.85f, 0.40f);   // 以逸待劳=琥珀
        private static readonly Color HiddenPathBuff = new Color(0.60f, 0.50f, 0.90f); // 曲径通幽=柔紫
        private static readonly Color SandbagBuff = new Color(0.80f, 0.70f, 0.45f);    // 沙包=沙棕
        // 逐风冲锋(M3 H07)
        private static readonly Color ChargeTrail = new Color(0.70f, 0.85f, 0.80f);    // 冲锋=风白

        public void Init(SimCore sim, Side player)
        {
            _sim = sim;
            _player = player;
            if (_iconMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                _iconMat = new Material(shader);
                _iconMat.color = Color.white;
            }
            _iconPool = new VfxPool(96, MakeIconQuad);                    // 增益/冲锋(方片)
            _slowPool = new VfxPool(24, MakeSlowIcon);                    // Q09:横条
            _silencePool = new VfxPool(24, MakeSilenceIcon);              // Q09:十字
            _woundPool = new VfxPool(24, MakeWoundIcon);                   // Q09:竖条
            _fragilePool = new VfxPool(24, MakeFragileIcon);               // Q09:菱形

            var screen = UiRoot.Current != null ? UiRoot.Current.Screen : null;
            if (screen != null) BuildBanner(screen);
            if (sim != null) sim.AddEventListener(OnSimEvent);
        }

        /// <summary>GPT④:EP 事件驱动(替代轮询——轮询会漏掉"先花后补、净变=0")。</summary>
        private void OnSimEvent(SimEvent ev)
        {
            if (ev.Type != SimEventType.EpChanged) return;
            if (ev.SubjectId != (int)_player) return;
            if (_epFlash == null) return;
            float delta = ev.Amount;
            _epFlash.text = (delta > 0 ? "EP +" : "EP ") + delta.ToString("0.#");
            _epFlash.style.color = delta > 0 ? new Color(0.4f, 0.9f, 0.5f) : new Color(1f, 0.6f, 0.3f);
            _epFlash.style.display = DisplayStyle.Flex;
            _epFlashUntil = Time.unscaledTime + 1.0f;
        }

        // ================= 海克斯横幅(X00) =================

        private void BuildBanner(VisualElement parent)
        {
            _hexBanner = new Label { name = "HexBanner", pickingMode = PickingMode.Ignore };
            _hexBanner.style.position = Position.Absolute;
            _hexBanner.style.top = 8;
            _hexBanner.style.left = 0; _hexBanner.style.right = 0;
            _hexBanner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hexBanner.style.fontSize = 18;
            _hexBanner.style.color = Color.white;
            _hexBanner.style.backgroundColor = new Color(0.08f, 0.07f, 0.10f, 0.85f);
            _hexBanner.style.paddingTop = 4; _hexBanner.style.paddingBottom = 4;
            _hexBanner.style.display = DisplayStyle.None;
            parent.Add(_hexBanner);

            // U04:EP 变化浮动提示(横幅下方,1 秒自动消失)
            _epFlash = new Label { name = "EpFlash", pickingMode = PickingMode.Ignore };
            _epFlash.style.position = Position.Absolute;
            _epFlash.style.top = 40;
            _epFlash.style.left = 0; _epFlash.style.right = 0;
            _epFlash.style.unityTextAlign = TextAnchor.MiddleCenter;
            _epFlash.style.fontSize = 14;
            _epFlash.style.display = DisplayStyle.None;
            parent.Add(_epFlash);
        }

        private long _lastStamp = -1;   // Q08:-1 = 从未激活(与 ActivationStampOf 口径一致)

        private void CheckHexActivation()
        {
            var stamp = HexSystem.ActivationStampOf(_sim, _player);
            // Q08:戳变化且 ≥0(=有激活卡)→ 弹横幅;-1(无卡/过期)不弹
            if (stamp != _lastStamp && stamp >= 0)
            {
                var card = HexSystem.ActiveOf(_sim, _player);
                if (card != null)
                    ShowBanner("「" + HexName(card.Value) + "」生效", _player == Side.Blue ? "蓝方" : "红方");
            }
            _lastStamp = stamp;
        }

        private static string HexName(HexId id)
        {
            switch (id)
            {
                case HexId.SteelTide: return "钢铁洪流";
                case HexId.RapidDeploy: return "快速部署";
                case HexId.Vengeance: return "陷阵之志";
                case HexId.LightMobility: return "轻装机动";
                case HexId.IronWall: return "铁壁防线";
                case HexId.HoldFast: return "以逸待劳";
                case HexId.HiddenPath: return "曲径通幽";
                case HexId.Sandbag: return "沙包";
                default: return id.ToString();
            }
        }

        private void ShowBanner(string text, string side)
        {
            if (_hexBanner == null) return;
            _hexBanner.text = side + " " + text;
            _hexBanner.style.display = DisplayStyle.Flex;
            _bannerUntil = Time.unscaledTime + 0.8f;
        }

        // ================= 状态图标(C06) =================

        private void Update()
        {
            // 横幅到期
            if (_hexBanner != null && _hexBanner.style.display == DisplayStyle.Flex
                && Time.unscaledTime >= _bannerUntil)
                _hexBanner.style.display = DisplayStyle.None;

            // U04:EP 浮标到期(每帧检查)
            if (_epFlash != null && _epFlash.style.display == DisplayStyle.Flex
                && Time.unscaledTime >= _epFlashUntil)
                _epFlash.style.display = DisplayStyle.None;

            // 每 0.25s 扫描一次状态
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.25f;
            if (_sim == null) return;
            CheckHexActivation();
            ScanStatusIcons();
            CheckUpgrades();     // U03:升级徽章
            // U04 已改事件驱动(OnSimEvent 消费 EpChanged),不再轮询
        }

        // ================= U04:EP 变化提示 =================

        private float _lastEp = -1f;
        private Label _epFlash;
        private float _epFlashUntil;

        private void CheckEpChange()
        {
            float ep = _sim.EpOf(_player);
            if (_lastEp >= 0f && Mathf.Abs(ep - _lastEp) > 0.01f)
            {
                float delta = ep - _lastEp;
                if (_epFlash != null)
                {
                    _epFlash.text = (delta > 0 ? "EP +" : "EP ") + delta.ToString("0.#");
                    _epFlash.style.color = delta > 0 ? new Color(0.4f, 0.9f, 0.5f) : new Color(1f, 0.6f, 0.3f);
                    _epFlash.style.display = DisplayStyle.Flex;
                    _epFlashUntil = Time.unscaledTime + 1.0f;
                }
            }
            _lastEp = ep;
        }

        // ================= U03:升级成功闪光 =================

        private readonly Dictionary<HeroId, bool> _upgraded = new Dictionary<HeroId, bool>();

        private void CheckUpgrades()
        {
            foreach (HeroId hero in System.Enum.GetValues(typeof(HeroId)))
            {
                bool now = _sim.IsUpgraded(_player, hero);
                bool was;
                if (!_upgraded.TryGetValue(hero, out was)) was = false;
                if (now && !was)
                {
                    // 升级成功:找该英雄在场主将 → 金色闪光(VfxDirector 池)
                    var board = BoardView.Active;
                    if (board != null && VfxDirector.Current != null)
                    {
                        foreach (var kv in _sim.Entities)
                        {
                            var e = kv.Value;
                            if (e == null || e.Kind != EntityKind.Unit || e.IsSoldier || e.Side != _player) continue;
                            if (e.Hero != hero) continue;
                            var view = board.GetView(e.Id);
                            if (view == null) continue;
                            VfxDirector.Current.SpawnUpgradeFlash(view.transform.position);
                            break;
                        }
                    }
                }
                _upgraded[hero] = now;
            }
        }

        private void ScanStatusIcons()
        {
            // 清旧
            foreach (var kv in _icons)
                foreach (var go in kv.Value)
                    _iconPool.Return(go);
            _icons.Clear();

            var board = BoardView.Active;
            if (board == null) return;

            // 海克斯当前激活卡(整方一个口径,逐单位判断资格)
            var activeCard = HexSystem.ActiveOf(_sim, _player);

            foreach (var kv in _sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Unit || e.State == EntityState.Dead) continue;
                if (!VfxVisibility.ShowAttached(_sim, _player, e)) continue;
                // Q07 修复:不再一刀切过滤阵营 —— 己方显示增益+负面,敌军只显示负面(可见时)
                bool isOwn = e.Side == _player;
                if (!isOwn && e.IsSoldier) continue;   // 敌军只看主将(士兵太密不挂)

                var view = board.GetView(e.Id);
                if (view == null) continue;

                var icons = new List<StatusIconInfo>();

                // 负面:所有可见主将(含敌军)+己方士兵(信息密度控制);Q09:每型用形状池
                if (!e.IsSoldier || isOwn)
                {
                    if (e.Status.Has(DebuffType.Slow)) icons.Add(new StatusIconInfo { Pool = _slowPool, Color = SlowColor });
                    if (e.Status.Has(DebuffType.Silence)) icons.Add(new StatusIconInfo { Pool = _silencePool, Color = SilenceColor });
                    if (e.Status.Has(DebuffType.Wound)) icons.Add(new StatusIconInfo { Pool = _woundPool, Color = WoundColor });
                    if (e.Status.Has(DebuffType.Fragile)) icons.Add(new StatusIconInfo { Pool = _fragilePool, Color = FragileColor });
                }

                // 增益(海克斯,只给己方主将;Q07 修复:加 isOwn 守卫,敌军只看负面)
                if (isOwn && !e.IsSoldier && activeCard.HasValue)
                {
                    var card = activeCard.Value;
                    switch (card)
                    {
                        case HexId.SteelTide:
                            if (e.Hp >= 100f) icons.Add(new StatusIconInfo { Pool = _iconPool, Color = SteelTideBuff });
                            break;
                        case HexId.Vengeance:
                            icons.Add(new StatusIconInfo { Pool = _iconPool, Color = VengeanceBuff });
                            break;
                        case HexId.LightMobility:
                            if (HexSystem.MoveMultiplierOf(_sim, e) > 1.5f) icons.Add(new StatusIconInfo { Pool = _iconPool, Color = LightMobBuff });
                            break;
                        case HexId.IronWall:
                            if (HexSystem.IsHeavyRegimentOf(_sim, e)) icons.Add(new StatusIconInfo { Pool = _iconPool, Color = IronWallBuff });
                            break;
                        case HexId.HoldFast:
                            if (HexSystem.OutputBonusOf(_sim, e) > 0.05f) icons.Add(new StatusIconInfo { Pool = _iconPool, Color = HoldFastBuff });
                            break;
                        case HexId.HiddenPath:
                            icons.Add(new StatusIconInfo { Pool = _iconPool, Color = HiddenPathBuff });
                            break;
                        case HexId.Sandbag:
                            if (HexSystem.DamageReductionOf(_sim, e) < 0.9f) icons.Add(new StatusIconInfo { Pool = _iconPool, Color = SandbagBuff });
                            break;
                    }
                }

                // 逐风冲锋尾迹(M3 H07;己方主将)
                if (isOwn && !e.IsSoldier && e.Hero == HeroId.ZhuFeng && e.IsCharging)
                    icons.Add(new StatusIconInfo { Pool = _iconPool, Color = ChargeTrail });

                if (icons.Count == 0) continue;

                // 最多两枚(README §4;负面优先)
                var list = new List<GameObject>();
                int count = Mathf.Min(icons.Count, 2);
                for (int i = 0; i < count; i++)
                {
                    var info = icons[i];
                    var go = info.Pool.Borrow(view.transform);
                    if (go == null) break;
                    var y = view.TextTopY + 0.6f + i * 0.35f;
                    go.transform.localPosition = new Vector3(0f, y, 0f);
                    // Q09:形状池的实例出厂时已设好 localScale(慢=扁宽/沉默=十字/伤=窄高/脆=菱形);
                    // 增益/冲锋走通用池需要设尺寸
                    if (info.Pool == _iconPool)
                        go.transform.localScale = new Vector3(0.22f, 0.22f, 1f);
                    var mr = go.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var block = new MaterialPropertyBlock();
                        mr.GetPropertyBlock(block);
                        block.SetColor("_BaseColor", info.Color);
                        mr.SetPropertyBlock(block);
                    }
                    list.Add(go);
                }
                if (list.Count > 0) _icons[e.Id] = list;
            }
        }

        // Q09:形状+色彩双编码(README §4"不靠记色"),去色后仍可辨
        private static GameObject MakeSlowIcon()     // 横条(被压住)
        {
            var go = MakeIconQuad();
            go.name = "FX_Icon_Slow";
            go.transform.localScale = new Vector3(0.30f, 0.14f, 1f);   // 扁宽
            return go;
        }
        private static GameObject MakeSilenceIcon()  // 十字(禁言/禁法)
        {
            var root = new GameObject("FX_Icon_Silence");
            for (int i = 0; i < 2; i++)
            {
                var go = MakeIconQuad();
                go.transform.SetParent(root.transform, false);
                go.transform.Rotate(0, 0, i == 0 ? 45f : -45f);
                go.transform.localScale = new Vector3(0.30f, 0.10f, 1f);
            }
            return root;
        }
        private static GameObject MakeWoundIcon()    // 竖条(受创)
        {
            var go = MakeIconQuad();
            go.name = "FX_Icon_Wound";
            go.transform.localScale = new Vector3(0.14f, 0.30f, 1f);   // 窄高
            return go;
        }
        private static GameObject MakeFragileIcon()  // 菱形(脆裂)
        {
            var go = MakeIconQuad();
            go.name = "FX_Icon_Fragile";
            go.transform.Rotate(0, 0, 45f);
            go.transform.localScale = new Vector3(0.24f, 0.24f, 1f);
            return go;
        }

        private static GameObject MakeIconQuad()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "FX_StatusIcon";
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _iconMat;
            return go;
        }

        private void OnDisable() => Dispose();
        private void OnDestroy() => Dispose();
        public void Dispose()
        {
            _icons.Clear();
            _iconPool?.DisposeAll();
            _slowPool?.DisposeAll(); _silencePool?.DisposeAll();
            _woundPool?.DisposeAll(); _fragilePool?.DisposeAll();
            // 解订阅由 GameBootstrap 的 Sim.ClearEventListeners 统一覆盖
        }
    }
}
