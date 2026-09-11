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
            _root.style.position = Position.Absolute;
            _root.style.left = 12;
            _root.style.top = 12;
            _root.style.width = 372;

            var title = UiKit.MakeLabel("总览", "label-title");
            _root.Add(title);
            _population = UiKit.MakeLabel("");
            _root.Add(_population);

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
            _root.Add(poolRow);

            _blocksHost = new VisualElement();
            _blocksHost.style.marginTop = 6;
            _root.Add(_blocksHost);

            _feedback = UiKit.MakeLabel("", "feedback-ok");
            _feedback.style.marginTop = 4;
            _root.Add(_feedback);

            parent.Add(_root);
        }

        private void Update()
        {
            if (_model == null || _root == null) return;
            _population.text = "人口 将 " + _model.PopulationOf() + "/" + _model.PopulationCapOf();
            _pool.text = "补兵池 " + _model.ReinforcePoolOf() + "/" + _model.ReinforcePoolCap();
            RebuildBlocks(_model.DescribeBlocks());

            if (_lastFeedback != null && Time.unscaledTime < _feedbackUntil)
            {
                _feedback.text = _lastFeedback;
                _feedback.EnableInClassList("feedback-ok", _lastFeedbackOk);
                _feedback.EnableInClassList("feedback-err", !_lastFeedbackOk);
            }
            else _feedback.text = "";
        }

        private void RebuildBlocks(List<BarracksOverviewModel.BlockInfo> blocks)
        {
            _blocksHost.Clear();
            foreach (var block in blocks)
                _blocksHost.Add(BuildBlock(block));
        }

        private VisualElement BuildBlock(BarracksOverviewModel.BlockInfo block)
        {
            bool destroyed = block.State == BarracksOverviewModel.BlockState.Destroyed;
            var host = new VisualElement();
            host.style.marginTop = 6;
            host.style.paddingTop = 6;
            host.style.borderTopWidth = 1;
            host.style.borderTopColor = new Color(1f, 1f, 1f, 0.12f);

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            var idLabel = UiKit.MakeLabel("主基地 #" + block.BarracksId, "label-title");
            head.Add(idLabel);
            if (destroyed)
            {
                var d = UiKit.MakeLabel("已摧毁 · 不可生产", "feedback-err");
                d.style.marginLeft = 8;
                head.Add(d);
            }
            host.Add(head);

            for (int i = 0; i < block.Slots.Length; i++)
                host.Add(BuildSlot(block, i, destroyed));

            if (!destroyed)
                host.Add(BuildHeroButtons(block.BarracksId));
            return host;
        }

        private VisualElement BuildSlot(BarracksOverviewModel.BlockInfo block, int index, bool destroyed)
        {
            var slot = block.Slots[index];
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3;

            string heroText;
            if (destroyed) heroText = "—";
            else if (slot.WaitingForSpawn) heroText = HeroNameOf(slot.Current) + "(等位)";
            else heroText = slot.Current.HasValue ? HeroNameOf(slot.Current) : "空闲";
            var slotLabel = UiKit.MakeLabel("槽" + index + ":" + heroText);
            slotLabel.style.width = 104;
            row.Add(slotLabel);

            // 生产进度条(绿);空闲/摧毁=空槽底
            var shell = new VisualElement();
            shell.AddToClassList("hpbar-shell");
            shell.style.width = 150;
            shell.style.marginRight = 6;
            var fill = new VisualElement();
            fill.AddToClassList("hpbar-fill");
            if (!destroyed && slot.Current.HasValue && slot.TotalSeconds > 0f)
            {
                float frac = Mathf.Clamp01(slot.Progress / ProductionSystem.GeneralProductionSeconds);
                fill.style.width = Length.Percent(frac * 100f);
                fill.style.backgroundColor = new Color(0.30f, 0.85f, 0.40f);
            }
            else fill.style.width = 0;
            shell.Add(fill);
            row.Add(shell);

            if (!destroyed && slot.PendingCount > 0)
            {
                var p = UiKit.MakeLabel("×" + slot.PendingCount, "label-dim");
                p.style.width = 30;
                row.Add(p);
            }

            var spacer = new VisualElement(); spacer.style.flexGrow = 1; row.Add(spacer);
            if (!destroyed && (slot.Current.HasValue || slot.PendingCount > 0))
            {
                var cancel = UiKit.MakeButton("取消", () =>
                {
                    string result = _model.Cancel(block.BarracksId, index);
                    Report(result, "已取消");
#if UNITY_EDITOR
                    InputAudit.Log("production_cancel building=" + block.BarracksId + " slot=" + index
                        + " result=" + ResultForLog(result));
#endif
                });
                cancel.style.width = 56;
                row.Add(cancel);
            }
            return row;
        }

        private VisualElement BuildHeroButtons(int buildingId)
        {
            var host = new VisualElement();
            for (int row = 0; row < 2; row++)
            {
                var r = new VisualElement();
                r.style.flexDirection = FlexDirection.Row;
                r.style.alignItems = Align.Center;
                r.style.marginTop = 3;
                for (int col = 0; col < 3; col++)
                {
                    int idx = row * 3 + col;
                    var hero = HeroOrder[idx];
                    var chip = new VisualElement();
                    chip.style.width = 8; chip.style.height = 18;
                    chip.style.marginRight = 4;
                    chip.style.backgroundColor = HeroColors[idx];
                    r.Add(chip);
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
                    r.Add(b);
                }
                host.Add(r);
            }
            return host;
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
            if (_root != null) LastPanelRect = UiKit.WorldRect(_root);   // 拦截桥 + 道具面板跟随
        }

        private void OnDisable() => LastPanelRect = Rect.zero;
        private void OnDestroy() => LastPanelRect = Rect.zero;
    }
}
