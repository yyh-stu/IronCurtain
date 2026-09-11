using System;
using NUnit.Framework;
using IronCurtain.Sim;

namespace IronCurtain.Tests
{
    /// <summary>三路城墙(2026-09-11 制作人规格 §4):三段独立血量/可被攻击/破口通行/墙外视野。</summary>
    public class WallSystemTests
    {
        [SetUp]
        public void SetUp() { WallSystem.Reset(); }

        [Test]
        public void 初始状态与几何()
        {
            Assert.AreEqual(5, WallSystem.Panels, "五段可击破面");
            for (int p = 0; p < WallSystem.Panels; p++)
            {
                Assert.AreEqual(WallSystem.WallHp, WallSystem.Hp(Side.Blue, p), "初始满血(蓝段" + p + ")");
                Assert.IsFalse(WallSystem.IsBroken(Side.Blue, p), "初始未破");
            }
            // 五段锚点顺序:上路 < 上草地 < 中路 < 下草地 < 下路(Row 小=负角)
            for (int p = 0; p < 4; p++)
                Assert.Less(WallSystem.PanelAngle(Side.Blue, p), WallSystem.PanelAngle(Side.Blue, p + 1),
                    "锚点递增(段" + p + "→" + (p + 1) + ")");
            // 段中心=本路方向:上路段中心角>0(朝上偏)
            Assert.Less(WallSystem.LaneAngle(0), -0.5f, "上路段朝上偏(Row 小=负角)");
            Assert.Greater(WallSystem.LaneAngle(2), 0.5f, "下路段朝下偏");
            Assert.AreEqual(0f, WallSystem.LaneAngleOf(Side.Blue, 1), 0.01f, "蓝中段正对前方");
            Assert.AreEqual(Math.PI, Math.Abs(WallSystem.LaneAngleOf(Side.Red, 1)), 0.01f, "红中段反向(180°)");
        }

        [Test]
        public void 墙体命中与伤害到破()
        {
            // 蓝中路段中心点(基地(20,80) 外 40 米,正前方)=(60,80)
            var wallPoint = new SimVec2(28f, 80f);   // 基地(8,80)+半径 20
            Assert.IsTrue(WallSystem.OnWall(Side.Blue, 2, wallPoint), "中路段中心在弧带上(五段制:2=中路)");
            WallSystem.Damage(Side.Blue, 2, 120f);
            Assert.AreEqual(80f, WallSystem.Hp(Side.Blue, 2), 0.01f, "伤害正确扣血(200−120)");
            Assert.IsFalse(WallSystem.IsBroken(Side.Blue, 2), "未归零不破");
            WallSystem.Damage(Side.Blue, 2, 80f);
            Assert.IsTrue(WallSystem.IsBroken(Side.Blue, 2), "归零即破");
            WallSystem.Damage(Side.Blue, 2, 100f); // 已破不再吸收
            Assert.AreEqual(0f, WallSystem.Hp(Side.Blue, 2), "破后伤害无效");
            // 段独立性
            Assert.AreEqual(WallSystem.WallHp, WallSystem.Hp(Side.Blue, 0), "上路段不受影响");
            Assert.AreEqual(WallSystem.WallHp, WallSystem.Hp(Side.Red, 2), "红方不受影响");
        }

        [Test]
        public void 通行判定完好与破开()
        {
            // 魔法屏障不设大门(2026-09-11):完好时自家处处可过、敌方处处被挡;击碎后敞开
            var wallPoint = new SimVec2(28f, 80f);     // 蓝中路屏障弧上(基地(8,80)+半径 20)
            Assert.IsTrue(WallSystem.OnWall(Side.Blue, 2, wallPoint), "中路段在弧带上");
            Assert.IsTrue(WallSystem.CanPass(Side.Blue, wallPoint), "自家可穿屏障");
            Assert.IsFalse(WallSystem.CanPass(Side.Red, wallPoint), "敌方被完好屏障挡住");
            WallSystem.Damage(Side.Blue, 2, WallSystem.WallHp);
            Assert.IsTrue(WallSystem.IsBroken(Side.Blue, 2), "该段击碎");
            Assert.IsTrue(WallSystem.CanPass(Side.Red, wallPoint), "击碎后双方可过");
        }

        [Test]
        public void 墙外视野()
        {
            // 蓝中路完好段:墙外 10 米内的点应被覆盖
            Assert.IsTrue(WallSystem.SeesThrough(Side.Blue, new SimVec2(38f, 80f)), "墙外 10 米内可见");
            Assert.IsFalse(WallSystem.SeesThrough(Side.Blue, new SimVec2(150f, 80f)), "远离墙不可见(河心之外)");
            // 相邻段的视野带(外扩 10 米,约 28.6°)在 R=20 下必然互相覆盖,
            // 故单破一段仍有邻段照亮该处(这是正确语义);五段全碎才彻底失去墙外视野。
            for (int p2 = 0; p2 < WallSystem.Panels; p2++)
                WallSystem.Damage(Side.Blue, p2, WallSystem.WallHp);
            Assert.IsFalse(WallSystem.SeesThrough(Side.Blue, new SimVec2(38f, 80f)), "五段全碎后无墙外视野");
        }
        /// <summary>2026-09-11 制作人目视"红方全包围、蓝方范围正常"→ 根因=PanelAngle 朴素平均跨 ±180° 出错:
        /// 红方中路 180° 与上路 −120.5° 的平均给出 +29.75°(绕到圆的另一侧),且半角被算成 75°(半圆级)。
        /// 此测试两侧都验:段中心必须落在本段弧带上(通行判定依赖它)、且都在本侧正前方 ±85° 内、相邻段间距恒定。</summary>
        [Test]
        public void 两侧段角都在本侧弧内_红方不绕到对侧()
        {
            foreach (var side in new[] { Side.Blue, Side.Red })
            {
                float face = WallSystem.LaneAngleOf(side, 1);   // 本侧正前方(中路)
                for (int p = 0; p < WallSystem.Panels; p++)
                {
                    float d = AngleDiff(WallSystem.PanelAngle(side, p), face);
                    Assert.LessOrEqual(Math.Abs(d), 1.49f, side + " 段" + p + " 中心角应在本侧正前方 ±85° 内");
                    Assert.IsTrue(WallSystem.OnWall(side, p, WallSystem.PanelCenterPos(side, p)),
                        side + " 段" + p + " 的段中心必须落在本段弧带上(OnWall/CanPass 依赖它)");
                }
                // 真正的不变量:相邻段**首尾相接**(段间距 = 两段半角之和),五段恰好铺满整条弧
                for (int p = 0; p < WallSystem.Panels - 1; p++)
                {
                    float gap = Math.Abs(AngleDiff(WallSystem.PanelAngle(side, p + 1), WallSystem.PanelAngle(side, p)));
                    float sum = WallSystem.PanelHalf(side, p) + WallSystem.PanelHalf(side, p + 1);
                    Assert.AreEqual(sum, gap, 0.001f, side + " 段" + p + "/" + (p + 1) + " 必须首尾相接");
                }
            }
        }

        /// <summary>2026-09-11 制作人目视"没有完全挡住上下路边缘":
        /// 每路的**两条边缘**(车道在屏障半径处的角边界)必须落在该路面板的覆盖内,否则敌人能从路边绕进去。</summary>
        [Test]
        public void 每条车道的两条边缘都必须被该路面板挡住()
        {
            foreach (var side in new[] { Side.Blue, Side.Red })
            {
                var c = WallSystem.BaseCenter(side);
                for (int lane = 0; lane < 3; lane++)
                {
                    int panel = lane * 2;                       // 0→上路(段0), 1→中路(段2), 2→下路(段4)
                    float a = WallSystem.LaneAngleOf(side, lane);
                    float h = WallSystem.LaneHalfAngle(side, lane);
                    Assert.Greater(h, 0.25f, side + " 路" + lane + " 角半宽应显著大于 0");
                    // ① 该路面板的半角不得小于车道角半宽(数值比较,不受边界浮点影响)
                    Assert.GreaterOrEqual(WallSystem.PanelHalf(side, panel) + 1e-4f, h,
                        side + " 路" + lane + " 的路面板半角必须 ≥ 车道角半宽");
                    // ② 几何上:路缘稍内侧的弧点必须被该路面板挡住(边缘 ±0.03 内即为"挡住")
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        float ea = a + sgn * (h - 0.03f);
                        var edge = new SimVec2(c.X + (float)Math.Cos(ea) * WallSystem.Radius,
                            c.Y + (float)Math.Sin(ea) * WallSystem.Radius);
                        Assert.IsTrue(WallSystem.OnWall(side, panel, edge),
                            side + " 路" + lane + " 的路缘附近必须被段" + panel + "挡住");
                    }
                }
            }
        }

        /// <summary>有符号角差归一化到 (−π,π](测试内自备,与被测实现独立)。</summary>
        private static float AngleDiff(float a, float b)
        {
            float d = a - b;
            while (d > (float)Math.PI) d -= 2f * (float)Math.PI;
            while (d <= -(float)Math.PI) d += 2f * (float)Math.PI;
            return d;
        }
    }
}
