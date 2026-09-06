using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>M8.4c 总览面板:主基地将生产 + 补兵池显示与补兵按钮。IMGUI 渲染,多基地自动多区块。</summary>
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

        /// <summary>M8.6 面板包围盒(GUI 坐标系,y 从顶起),供 SelectionSystem 判断点击是否落在面板上。</summary>
        public static Rect LastPanelRect = new Rect(10f, 10f, 360f, 0f);

        private BarracksOverviewModel _model;
        private string _lastError;
        private float _errorUntil;

        public void Init(SimCore sim, Side player)
        {
            _model = new BarracksOverviewModel(sim, player);
        }

        private void OnGUI()
        {
            if (_model == null) return;
            GUI.skin.font = ViewAssets.UiFont(14);

            var blocks = _model.DescribeBlocks();
            float height = 84f + blocks.Count * 158f;
            LastPanelRect = new Rect(10f, 10f, 360f, height);
            GUI.Box(new Rect(10f, 10f, 360f, height), "", MakeBoxStyle());
            GUILayout.BeginArea(new Rect(18f, 16f, 344f, height - 12f));

            GUILayout.Label("总览", MakeTitleStyle());
            GUILayout.Label("人口 将 " + _model.PopulationOf() + "/" + _model.PopulationCapOf());

            GUILayout.BeginHorizontal();
            GUILayout.Label("补兵池 " + _model.ReinforcePoolOf() + "/" + _model.ReinforcePoolCap());
            if (GUILayout.Button("补兵", GUILayout.Width(64f)))
                Report(NotNullOrOk(_model.RequestReinforce(out _)));
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            foreach (var block in blocks)
                DrawBlock(block);

            if (_lastError != null && Time.unscaledTime < _errorUntil)
            {
                var prev = GUI.color;
                GUI.color = _lastError == "已补兵" ? Color.green : Color.red;
                GUILayout.Label(_lastError);
                GUI.color = prev;
            }

            GUILayout.EndArea();
        }

        private static string NotNullOrOk(string error)
        {
            return error ?? "已补兵";
        }

        private void DrawBlock(BarracksOverviewModel.BlockInfo block)
        {
            bool destroyed = block.State == BarracksOverviewModel.BlockState.Destroyed;
            GUILayout.BeginHorizontal();
            GUILayout.Label("主基地 #" + block.BarracksId, MakeBoldLabel());
            if (destroyed)
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.35f, 0.3f);
                GUILayout.Label("已摧毁 · 不可生产");
                GUI.color = prev;
            }
            GUILayout.EndHorizontal();

            for (int i = 0; i < block.Slots.Length; i++)
                DrawSlot(block, i, destroyed);

            if (!destroyed)
                DrawHeroButtons(block.BarracksId);

            GUILayout.Space(6f);
        }

        private void DrawSlot(BarracksOverviewModel.BlockInfo block, int index, bool destroyed)
        {
            var slot = block.Slots[index];
            GUILayout.BeginHorizontal();
            string heroText;
            if (destroyed) heroText = "—";
            else if (slot.WaitingForSpawn) heroText = HeroNameOf(slot.Current) + "(等位)";
            else heroText = slot.Current.HasValue ? HeroNameOf(slot.Current) : "空闲";
            GUILayout.Label("槽" + index + ":" + heroText, GUILayout.Width(88f));

            var barRect = GUILayoutUtility.GetRect(150f, 16f);
            if (!destroyed && slot.Current.HasValue && slot.TotalSeconds > 0f)
            {
                float frac = Mathf.Clamp01(slot.Progress / ProductionSystem.GeneralProductionSeconds);
                var prev = GUI.color;
                GUI.color = new Color(0.2f, 0.2f, 0.2f);
                GUI.DrawTexture(barRect, Texture2D.whiteTexture);
                GUI.color = Color.green;
                GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * frac, barRect.height), Texture2D.whiteTexture);
                GUI.color = prev;
            }

            if (!destroyed && slot.PendingCount > 0)
                GUILayout.Label("×" + slot.PendingCount, GUILayout.Width(32f));

            GUILayout.FlexibleSpace();
            if (!destroyed && (slot.Current.HasValue || slot.PendingCount > 0))
            {
                if (GUILayout.Button("取消", GUILayout.Width(48f)))
                    Report(_model.Cancel(block.BarracksId, index));
            }
            else
            {
                GUILayout.Space(52f);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawHeroButtons(int buildingId)
        {
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 3; col++)
                {
                    int idx = row * 3 + col;
                    var hero = HeroOrder[idx];
                    var prev = GUI.color;
                    GUI.color = HeroColors[idx];
                    GUILayout.Box("", GUILayout.Width(14f), GUILayout.Height(20f));
                    GUI.color = prev;
                    if (GUILayout.Button("将:" + HeroNames[idx], GUILayout.Width(84f)))
                        Report(_model.StartProduction(buildingId, hero));
                }
                GUILayout.EndHorizontal();
            }
        }

        private void Report(string error)
        {
            _lastError = error;
            _errorUntil = Time.unscaledTime + 2f;
        }

        private static string HeroNameOf(HeroId? hero)
        {
            if (!hero.HasValue) return "空闲";
            int idx = System.Array.IndexOf(HeroOrder, hero.Value);
            return idx >= 0 ? HeroNames[idx] : hero.Value.ToString();
        }

        private static GUIStyle _boxStyle;
        private static GUIStyle MakeBoxStyle()
        {
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box);
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.10f, 0.82f));
                tex.Apply();
                _boxStyle.normal.background = tex;
            }
            return _boxStyle;
        }

        private static GUIStyle _titleStyle;
        private static GUIStyle MakeTitleStyle()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                _titleStyle.normal.textColor = Color.white;
            }
            return _titleStyle;
        }

        private static GUIStyle _boldStyle;
        private static GUIStyle MakeBoldLabel()
        {
            if (_boldStyle == null)
            {
                _boldStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _boldStyle.normal.textColor = new Color(0.9f, 0.92f, 0.95f);
            }
            return _boldStyle;
        }
    }
}
