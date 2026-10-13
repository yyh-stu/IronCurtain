using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronCurtain.View
{
    /// <summary>M8.4c 总览面板:主基地将生产 + 补兵池显示与补兵按钮。多基地自动多区块。
    /// 2026-09-11 demo→成品迁移:IMGUI OnGUI → UI Toolkit;数据/操作全部来自 BarracksOverviewModel。
    /// LastPanelRect 语义保留(worldBound 回填):SelectionSystem 拦截 + 道具面板跟随底边都依赖它。</summary>
    public sealed class BarracksOverviewPanel : MonoBehaviour
    {
        private static readonly HeroId[] HeroOrder =
            { HeroId.PanShi, HeroId.ZhenYue, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };

        private static readonly string[] HeroNames =
            { "磐石", "镇岳", "断峰", "逐风", "坠日", "星落" };

        private static readonly Color[] HeroColors =
        {
            new Color(0.55f, 0.62f, 0.70f), new Color(0.85f, 0.75f, 0.30f),
            new Color(0.75f, 0.40f, 0.35f), new Color(0.40f, 0.70f, 0.60f),
            new Color(0.90f, 0.60f, 0.25f), new Color(0.50f, 0.45f, 0.85f),
        };

        /// <summary>M8.6 面板包围盒(屏幕坐标),供 SelectionSystem 拦截与道具面板跟随。</summary>
        // 2026-09-13:与命令面板同款折叠/展开(折叠=底部居中,展开=屏幕中央)
        private Button _header;
        private VisualElement _body;
        private bool _expanded;

        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            ApplyExpanded();
        }

        private void ApplyExpanded()
        {
            if (_header == null) return;
            _header.text = _expanded ? "总览 ▼" : "总览 ▲";
            if (_body != null) _body.style.display = DisplayStyle.Flex;   // 中央框里始终展开(显隐由 UiRoot 管)
        }

        public static Rect LastPanelRect = new Rect(10f, 10f, 360f, 0f);

        private BarracksOverviewModel _model;
        private string _lastFeedback;
        private bool _lastFeedbackOk;
        private float _feedbackUntil;

        private VisualElement _root, _blocksHost;
        private Label _population, _pool, _feedback;

        public void Init(SimCore sim, Side player)
        {
            _model = new BarracksOverviewModel(sim, player);
            if (UiRoot.Current != null && UiRoot.Current.Screen != null) Build(UiRoot.Current.Screen);
        }

        private void Build(VisualElement parent)
        {
            _root = new VisualElement { name = "BarracksOverview" };
            _root.AddToClassList("panel");

            // 2026-09-13 制作人:"兵营总览也放下去" —— 与三个命令面板同一套:
            // 折叠态=底部居中入口条上的按钮,展开态=屏幕中央;箭头 折叠 ▲ / 展开 ▼。
            _header = UiKit.MakeButton("总览 ▲", () => SetExpanded(!_expanded));
            _header.style.width = 118;
            _body = new VisualElement();
            _body.style.marginTop = 6;
            _root.Add(_body);

            // 2026-09-13:面板内不再放重复的"总览"标题 —— 中央框的标题行已经给了(抓帧图里出现过两次)
            _population = UiKit.MakeLabel("");
            _body.Add(_population);

            var poolRow = new VisualElement();
            poolRow.style.flexDirection = FlexDirection.Row;
            poolRow.style.alignItems = Align.Center;
            poolRow.style.marginTop = 4;
            _pool = UiKit.MakeLabel("");
            _pool.style.flexGrow = 1;
            poolRow.Add(_pool);
            var btn = UiKit.MakeButton("补兵", () =>
            {
                int before = _model.ReinforcePoolOf();
                string result = _model.RequestReinforce(out _);
                Report(result, "已补兵");
#if UNITY_EDITOR
                InputAudit.Log("reinforce_click result=" + ResultForLog(result)
                    + " pool=" + before + "->" + _model.ReinforcePoolOf());
#endif
            });
            btn.style.width = 76;
            poolRow.Add(btn);
            _body.Add(poolRow);

            _blocksHost = new VisualElement();
            _blocksHost.style.marginTop = 6;
            _body.Add(_blocksHost);

            _feedback = UiKit.MakeLabel("", "feedback-ok");
            _feedback.style.marginTop = 4;
            _body.Add(_feedback);

            // 2026-09-13 修正:此前这句 parent.Add(_root) 把面板直接挂回 Screen,
            // 于是它被正常流式排在屏幕上方、铺满宽度**常显**(制作人三次报"上面的框还在");
            // 而 RegisterCenterEntry 又被落到 ApplyExpanded 里 → 每帧重复注册入口按钮。
            // 现在只在这里注册一次:折叠=底部入口,展开=中央框。
            UiRoot.Current.RegisterCenterEntry("总览", _root);
        }

        /// <summary>2026-09-14 供实机自检:本面板累计重建正文的次数(正常应为 0 或极少)。
        /// 制作人报"面板不稳定、点一下才恢复、这一下不算数"的根因就是"每帧/每秒重建正文"。</summary>
        public static int Rebuilds;

        private void Update()
        {
            if (_model == null || _root == null) return;
            _population.text = "人口 将 " + _model.PopulationOf() + "/" + _model.PopulationCapOf();
            _pool.text = "补兵池 " + _model.ReinforcePoolOf() + "/" + _model.ReinforcePoolCap();
            // 2026-09-13 修"点将领生产没反应/没反馈":原来这里**每帧无条件**重建全部区块,
            // 而 RebuildBlocks 会 Clear + 重建"将:英雄"按钮 —— 按钮每帧被销毁,点击永远落不到
            // 稳定元素上。改为只有**结构**变化才重建(键里排除逐帧跳动的 Progress),
            // 进度条改由 RefreshProgress 走元素引用逐帧刷新,动画不受影响。
            // 2026-09-14 制作人:"产兵我快速按下三个将名时…似乎每个指令发出都有延迟" ——
            // 根因是这里原先"排队数一变就 Clear+重建全部区块(含'将:英雄'按钮)" → 快速连点会点在
            // **已销毁的按钮**上,那一下不算数。现在改成**只建一次、原地刷新**:
            // 区块在其主基地存续期内**永不销毁**,按钮元素恒定,点击永远落在活元素上(零重建)。
            var blocks = _model.DescribeBlocks();
            SyncBlocks(blocks);

            if (_lastFeedback != null && Time.unscaledTime < _feedbackUntil)
            {
                _feedback.text = _lastFeedback;
                _feedback.EnableInClassList("feedback-ok", _lastFeedbackOk);
                _feedback.EnableInClassList("feedback-err", !_lastFeedbackOk);
            }
            else _feedback.text = "";
        }

        private static string BlocksKeyOf(List<BarracksOverviewModel.BlockInfo> blocks)
        {
            var sb = new System.Text.StringBuilder();
            for (int b = 0; b < blocks.Count; b++)
            {
                var blk = blocks[b];
                sb.Append(blk.BarracksId).Append(':').Append((int)blk.State).Append('|');
                for (int i = 0; i < blk.Slots.Length; i++)
                {
                    var sl = blk.Slots[i];
                    sb.Append(sl.Current.HasValue ? (int)sl.Current.Value : -1).Append(',')
                      .Append(sl.WaitingForSpawn ? 1 : 0).Append(',')
                      .Append(sl.PendingCount).Append(',');
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>每个槽一行(元素恒定):文本/进度/取消按钮全部原地更新,不重建。</summary>
        private sealed class SlotRow
        {
            public Label Name;
            public VisualElement Shell;
            public VisualElement Fill;
            public Label Pending;
            public Button Cancel;
            public bool CancelShown;
        }

        /// <summary>每个主基地一个区块(元素恒定)。</summary>
        private sealed class BlockView
        {
            public VisualElement Host;
            public Label DestroyedTag;
            public readonly List<SlotRow> Slots = new List<SlotRow>();
            public VisualElement HeroHost;
            public string HeroKey;
        }

        private readonly Dictionary<int, BlockView> _views = new Dictionary<int, BlockView>();
        private readonly List<int> _staleIds = new List<int>();

        /// <summary>按基地 id 对齐区块:新增基地才建元素,消失的基地才销毁 —— 其余一律原地刷新。</summary>
        private void SyncBlocks(List<BarracksOverviewModel.BlockInfo> blocks)
        {
            _staleIds.Clear();
            foreach (var id in _views.Keys)
            {
                bool alive = false;
                for (int i = 0; i < blocks.Count; i++) if (blocks[i].BarracksId == id) { alive = true; break; }
                if (!alive) _staleIds.Add(id);
            }
            for (int i = 0; i < _staleIds.Count; i++)
            {
                var v = _views[_staleIds[i]];
                if (v.Host.parent != null) v.Host.parent.Remove(v.Host);
                _views.Remove(_staleIds[i]);
            }
            for (int b = 0; b < blocks.Count; b++)
            {
                var blk = blocks[b];
                BlockView view;
                if (!_views.TryGetValue(blk.BarracksId, out view))
                {
                    view = CreateBlock(blk);
                    _views[blk.BarracksId] = view;
                    _blocksHost.Add(view.Host);
                }
                UpdateBlock(view, blk);
            }
        }

        /// <summary>建一次(基地出生时建,之后不再重建)。</summary>
        private BlockView CreateBlock(BarracksOverviewModel.BlockInfo block)
        {
            Rebuilds++;   // 只在**真的建元素**时 +1(原地刷新不计)
            var view = new BlockView();
            var host = new VisualElement();
            host.style.marginTop = 6;
            host.style.paddingTop = 6;
            host.style.borderTopWidth = 1;
            host.style.borderTopColor = new Color(1f, 1f, 1f, 0.12f);

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.Add(UiKit.MakeLabel("主基地 #" + block.BarracksId, "label-title"));
            view.DestroyedTag = UiKit.MakeLabel("已摧毁 · 不可生产", "feedback-err");
            view.DestroyedTag.style.marginLeft = 8;
            view.DestroyedTag.style.display = DisplayStyle.None;
            head.Add(view.DestroyedTag);
            host.Add(head);

            for (int i = 0; i < block.Slots.Length; i++)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 3;

                var sr = new SlotRow();
                sr.Name = UiKit.MakeLabel("");
                sr.Name.style.width = 104;
                row.Add(sr.Name);

                sr.Shell = new VisualElement();
                sr.Shell.AddToClassList("hpbar-shell");
                sr.Shell.style.width = 150;
                sr.Shell.style.marginRight = 6;
                sr.Fill = new VisualElement();
                sr.Fill.AddToClassList("hpbar-fill");
                sr.Fill.style.width = 0;
                sr.Shell.Add(sr.Fill);
                row.Add(sr.Shell);

                sr.Pending = UiKit.MakeLabel("");
                sr.Pending.style.width = 30;
                sr.Pending.style.display = DisplayStyle.None;
                row.Add(sr.Pending);

                var spacer = new VisualElement();
                spacer.style.flexGrow = 1;
                row.Add(spacer);

                int slotIndex = i;
                sr.Cancel = UiKit.MakeButton("取消", () =>
                {
                    string result = _model.Cancel(block.BarracksId, slotIndex);
                    Report(result, "已取消");
#if UNITY_EDITOR
                    InputAudit.Log("production_cancel building=" + block.BarracksId + " slot=" + slotIndex
                        + " result=" + ResultForLog(result));
#endif
                });
                sr.Cancel.style.width = 56;
                sr.Cancel.style.display = DisplayStyle.None;
                row.Add(sr.Cancel);
                host.Add(row);
                view.Slots.Add(sr);
            }

            view.HeroHost = new VisualElement();
            host.Add(view.HeroHost);
            view.Host = host;
            return view;
        }

        /// <summary>原地刷新一个区块(不动元素结构,除非"可生产英雄集合"变了)。</summary>
        private void UpdateBlock(BlockView view, BarracksOverviewModel.BlockInfo block)
        {
            bool destroyed = block.State == BarracksOverviewModel.BlockState.Destroyed;
            view.DestroyedTag.style.display = destroyed ? DisplayStyle.Flex : DisplayStyle.None;

            for (int i = 0; i < view.Slots.Count && i < block.Slots.Length; i++)
            {
                var sr = view.Slots[i];
                var slot = block.Slots[i];
                bool producing = !destroyed && slot.Current.HasValue && slot.TotalSeconds > 0f;
                string heroText;
                if (destroyed) heroText = "—";
                else if (slot.WaitingForSpawn) heroText = HeroNameOf(slot.Current) + "(等位)";
                else heroText = slot.Current.HasValue ? HeroNameOf(slot.Current) : "空闲";
                sr.Name.text = "槽" + i + ":" + heroText;

                if (producing)
                    sr.Fill.style.width = Length.Percent(
                        Mathf.Clamp01(slot.Progress / ProductionSystem.GeneralProductionSeconds) * 100f);
                else sr.Fill.style.width = 0;
                sr.Fill.style.backgroundColor = new Color(0.30f, 0.85f, 0.40f);

                bool showPending = !destroyed && slot.PendingCount > 0;
                sr.Pending.text = showPending ? ("×" + slot.PendingCount) : "";
                sr.Pending.style.display = showPending ? DisplayStyle.Flex : DisplayStyle.None;

                bool showCancel = !destroyed && (slot.Current.HasValue || slot.PendingCount > 0);
                if (showCancel != sr.CancelShown)
                {
                    sr.CancelShown = showCancel;
                    sr.Cancel.style.display = showCancel ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            // 英雄按钮:只在"可生产英雄集合"变化时重建(换边/BP 才会变,不是每帧)
            var producible = ProducibleHeroes();
            string heroKey = destroyed ? "dead" : string.Join(",", producible);
            if (heroKey != view.HeroKey)
            {
                view.HeroKey = heroKey;
                Rebuilds++;   // 英雄按钮集合变了才重建
                view.HeroHost.Clear();
                if (!destroyed) BuildHeroButtons(view.HeroHost, block.BarracksId, producible);
            }
        }

        private void BuildHeroButtons(VisualElement host, int buildingId, List<HeroId> producible)
        {
            // 2026-09-14 制作人:"AI 对局中,总览应该是**玩家选择的三个英雄**,而不是六英雄"。
            // 有 BP 选将结果时只列那 3 个(练习场无 BP → 仍是六个都可生产)。
            for (int i = 0; i < producible.Count; i++)
            {
                if (i % 3 == 0)
                {
                    var r0 = new VisualElement();
                    r0.style.flexDirection = FlexDirection.Row;
                    r0.style.alignItems = Align.Center;
                    r0.style.marginTop = 3;
                    host.Add(r0);
                }
                var rowEl = host[host.childCount - 1];
                var hero = producible[i];
                int idx = System.Array.IndexOf(HeroOrder, hero);
                if (idx < 0) idx = 0;
                var chip = new VisualElement();
                chip.style.width = 8; chip.style.height = 18;
                chip.style.marginRight = 4;
                chip.style.backgroundColor = HeroColors[idx];
                rowEl.Add(chip);
                var b = UiKit.MakeButton("将:" + HeroNames[idx], () =>
                {
                    string result = _model.StartProduction(buildingId, hero);
                    Report(result, "已加入生产");
#if UNITY_EDITOR
                    InputAudit.Log("production_click building=" + buildingId + " hero=" + hero
                        + " result=" + ResultForLog(result));
#endif
                });
                b.style.width = 96;
                b.style.flexShrink = 0;
                rowEl.Add(b);
            }
        }

        /// <summary>本局**玩家可生产**的英雄:走过 BP 就是自己选的那 3 个,否则(练习场/默认开局)六个全开放。
        /// 制作人 2026-09-14:"AI 对局中总览应该是玩家选择的三个英雄,而不是六英雄"。</summary>
        private static System.Collections.Generic.List<HeroId> ProducibleHeroes()
        {
            var picks = AppFlow.PlayerSide == Side.Red ? AppFlow.BpRedPicks : AppFlow.BpBluePicks;
            var list = new System.Collections.Generic.List<HeroId>();
            if (picks != null)
                for (int i = 0; i < picks.Count; i++)
                    if (!list.Contains(picks[i])) list.Add(picks[i]);   // BP 同方三张不重复,这里再兜一道
            if (list.Count > 0) return list;
            list.AddRange(HeroOrder);
            return list;
        }

        private void Report(string error, string success)
        {
            _lastFeedbackOk = error == null;
            _lastFeedback = error ?? success;
            _feedbackUntil = Time.unscaledTime + 2f;
        }

#if UNITY_EDITOR
        private static string ResultForLog(string error)
        {
            return error == null ? "ok" : "error message=\"" + error + "\"";
        }
#endif

        private static string HeroNameOf(HeroId? hero)
        {
            if (!hero.HasValue) return "空闲";
            int idx = System.Array.IndexOf(HeroOrder, hero.Value);
            return idx >= 0 ? HeroNames[idx] : hero.Value.ToString();
        }

        private void LateUpdate()
        {
            if (_root != null) LastPanelRect = UiKit.VisibleRectOrZero(_root);  // 2026-09-15:隐藏/关闭的面板不再留幽灵拦截区(拦截桥 + 道具面板跟随)
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;
    }
}
