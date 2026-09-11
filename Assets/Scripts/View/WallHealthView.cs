using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 屏障血条(2026-09-11 收尾):五段各自独立血量 —— 段受击后在该段顶缘上方显示阵营色血条,
    /// 满血隐藏(五段全显会有 10 条,画面噪音大;受击才出现也符合"打到哪里、还剩多少"的读法),
    /// 击碎后随段消失。规范沿用单位血条:UiSkin 槽底 + 阵营色填充 + 刻度线,
    /// 朝向以相机 up 对齐保持屏幕水平(同 EntityView.TickBillboard,修过"歪斜/背对剔除"两个坑)。
    /// </summary>
    public sealed class WallHealthView : MonoBehaviour
    {
        const float BarW = 4.2f;         // 条宽(米)
        const float BarH = 0.46f;        // 条高(米)
        // 离地高随幕高走(幕高改为可调后写死会失效;2026-09-11 收尾)
        static readonly float TopY = WallView.HeightMeters + 1.1f;
        // 2026-09-11 实测坑:血条原放在碗沿顶缘半径(=Radius−LeanIn=11)上,正好落进主城城堡模型内部,
        // 被城堡深度遮挡 → 整条看不见(把血条强制全显才暴露出来)。改为放在屏障**外侧半径**,
        // 从外侧看永远在城堡之前,不再被挡。
        const float BarRadius = WallSystem.Radius + 1f;
        const int HpPerTick = 20;        // 200 血 → 10 格(刻度 9 条)

        private sealed class Row
        {
            public Side Side;
            public int Panel;
            public Transform Root;
            public Transform Fill;
            public float Width;
        }

        private readonly List<Row> _rows = new List<Row>();
        private static Material _tickMat;

        public void Init(Transform parent)
        {
            var root = new GameObject("WallHpBars");
            root.transform.SetParent(parent, false);
            var slate = UiSkin.HpSlate();
            for (int s = 0; s < 2; s++)
            {
                var side = (Side)s;
                for (int panel = 0; panel < WallSystem.Panels; panel++)
                {
                    var rowGo = new GameObject("WallHp_" + side + "_" + panel);
                    rowGo.transform.SetParent(root.transform, false);
                    // 顶缘上方:顶缘半径 = Radius − LeanIn(碗沿上沿内收),故条子放在内收后的半径上,
                    // 正对各段顶缘、不悬在幕面外侧
                    var c = WallSystem.BaseCenter(side);
                    float a = WallSystem.PanelAngle(side, panel);
                    rowGo.transform.position = new Vector3(c.X + Mathf.Cos(a) * BarRadius, TopY, (3f - c.Y) - Mathf.Sin(a) * BarRadius);

                    if (slate != null)
                        ViewAssets.DecoTextured(slate, Vector3.zero, new Vector2(BarW + 0.10f, BarH + 0.06f), rowGo.transform, "Back");
                    else
                        ViewAssets.DecoQuad(ViewAssets.Unlit(new Color(0.10f, 0.11f, 0.13f)), Vector3.zero,
                            new Vector2(BarW + 0.08f, BarH + 0.05f), rowGo.transform, "Back");
                    var fill = ViewAssets.DecoQuad(FactionMat(side), Vector3.zero, new Vector2(BarW, BarH),
                        rowGo.transform, "Fill").transform;
                    if (_tickMat == null) _tickMat = ViewAssets.Unlit(new Color(0.08f, 0.09f, 0.11f));
                    int seg = Mathf.Max(1, Mathf.FloorToInt(WallSystem.WallHp / HpPerTick));
                    for (int i = 1; i < seg; i++)
                    {
                        float x = -BarW * 0.5f + BarW * i / seg;
                        ViewAssets.DecoQuad(_tickMat, new Vector3(x, 0f, 0.01f), new Vector2(0.035f, BarH), rowGo.transform, "Tick" + i);
                    }
                    rowGo.SetActive(false);
                    _rows.Add(new Row { Side = side, Panel = panel, Root = rowGo.transform, Fill = fill, Width = BarW });
                }
            }
        }

        private static Material _blueMat, _redMat;
        private static Material FactionMat(Side side)
        {
            // 与单位血条同色系(2026-09-09 制作人拍板:己方蓝/敌方红)
            if (_blueMat == null) _blueMat = ViewAssets.Unlit(new Color(0.25f, 0.55f, 1f));
            if (_redMat == null) _redMat = ViewAssets.Unlit(new Color(0.98f, 0.28f, 0.22f));
            return side == Side.Blue ? _blueMat : _redMat;
        }

        /// <summary>每帧刷新:段受损才显示,长度=血量比例;击碎隐藏。
        /// 调试/取证开关 IC_WALL_BARS=always 可强制全段常显(排查遮挡位置用,正式观感不需要)。</summary>
        public void Tick(Camera cam)
        {
            bool always = System.Environment.GetEnvironmentVariable("IC_WALL_BARS") == "always";
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.Root == null) continue;
                bool broken = WallSystem.IsBroken(row.Side, row.Panel);
                float hp = WallSystem.Hp(row.Side, row.Panel);
                float frac = WallSystem.WallHp > 0f ? Mathf.Clamp01(hp / WallSystem.WallHp) : 0f;
                bool show = always ? !broken : (!broken && frac < 0.999f);
                if (row.Root.gameObject.activeSelf != show) row.Root.gameObject.SetActive(show);
                if (!show) continue;
                row.Fill.localScale = new Vector3(row.Width * frac, BarH, 1f);
                row.Fill.localPosition = new Vector3(-row.Width * (1f - frac) * 0.5f, 0f, 0f);
                if (cam != null)
                    row.Root.rotation = Quaternion.LookRotation(cam.transform.position - row.Root.position, cam.transform.up);
            }
        }
    }
}
