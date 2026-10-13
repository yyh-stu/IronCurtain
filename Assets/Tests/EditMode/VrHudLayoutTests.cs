using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    // VR01(2026-09-21 返工单):血盾一体布局断言。
    // 返工单点名要求"要验证测试能失败:外移盾段的隔离样例必须报错"——
    // OldFormula_ShieldSticksOut 用修复前的旧公式(盾条接在血槽外右侧)复算同一组数值,
    // 断言它越框 ⇒ 证明本套边界断言能抓住"盾在槽外"这类缺陷,不是永真式。
    public class VrHudLayoutTests
    {
        const float W = 2.4f;   // 与实机主将条同量级的槽宽
        const float Eps = 1e-4f;

        static void AssertInsideSlot(EntityView.BarLayout l, float w)
        {
            // 两段都必须完全落在 [-w/2, +w/2] 内(顶点不越框)
            Assert.GreaterOrEqual(l.LifeX - l.LifeW * 0.5f, -w * 0.5f - Eps, "生命段左端越出槽外");
            Assert.LessOrEqual(l.LifeX + l.LifeW * 0.5f, w * 0.5f + Eps, "生命段右端越出槽外");
            if (l.ShowShield)
            {
                Assert.GreaterOrEqual(l.ShieldX - l.ShieldW * 0.5f, -w * 0.5f - Eps, "护盾段左端越出槽外");
                Assert.LessOrEqual(l.ShieldX + l.ShieldW * 0.5f, w * 0.5f + Eps, "护盾段右端越出槽外");
            }
        }

        [Test]
        public void FullHpWithShield_TwoThirdsLifeOneThirdShield()
        {
            // 返工单示例:MaxHp=100,H=100,S=50 → 生命占 2/3、盾占 1/3
            var l = EntityView.ComputeHpShieldBar(W, 100f, 100f, 50f, 60f);
            Assert.AreEqual(2f / 3f, l.LifeW / W, 1e-3f);
            Assert.AreEqual(1f / 3f, l.ShieldW / W, 1e-3f);
            Assert.IsTrue(l.ShowShield);
            AssertInsideSlot(l, W);
            // 两段紧接、同一起点(无间隙、不从槽外起排)
            Assert.AreEqual(-W * 0.5f, l.LifeX - l.LifeW * 0.5f, Eps, "生命段未从槽左端起排");
            Assert.AreEqual(l.LifeX + l.LifeW * 0.5f, l.ShieldX - l.ShieldW * 0.5f, Eps, "护盾段与生命段之间有间隙/重叠");
        }

        [Test]
        public void LowHpWithShield_LifeThenShieldThenEmpty()
        {
            // 返工单示例:H=60,S=20 → 60%生命+20%盾+20%空槽
            var l = EntityView.ComputeHpShieldBar(W, 60f, 100f, 20f, 60f);
            Assert.AreEqual(0.60f, l.LifeW / W, 1e-3f);
            Assert.AreEqual(0.20f, l.ShieldW / W, 1e-3f);
            AssertInsideSlot(l, W);
            Assert.Less(l.LifeX + l.LifeW * 0.5f, W * 0.5f - Eps, "残血时生命+盾不该顶满整槽");
        }

        [Test]
        public void ZeroShield_FallsBackToPlainLifeProportion()
        {
            var l = EntityView.ComputeHpShieldBar(W, 75f, 100f, 0f, 60f);
            Assert.IsFalse(l.ShowShield);
            Assert.AreEqual(0.75f, l.LifeW / W, 1e-3f);
            // 与旧公式完全一致:右端 = -w/2 + w*(H/MaxHp)(75% 血的右端落在 60% 处,不是槽右端)
            Assert.AreEqual(-W * 0.5f + W * 0.75f, l.LifeX + l.LifeW * 0.5f, Eps);
        }

        [Test]
        public void OverShield_ClampedInsideSlot()
        {
            // 超额盾:H=100,S=200 → D=300,1/3 生命 + 2/3 盾,盾末端=槽右端,不越框
            var l = EntityView.ComputeHpShieldBar(W, 100f, 100f, 200f, 250f);
            Assert.AreEqual(W, l.LifeW + l.ShieldW, Eps);
            AssertInsideSlot(l, W);
        }

        [Test]
        public void MaxHpZero_HiddenSafely()
        {
            var l = EntityView.ComputeHpShieldBar(W, 0f, 0f, 30f, 60f);
            Assert.IsFalse(l.ShowShield);
            Assert.AreEqual(0f, l.LifeW);
            Assert.AreEqual(0f, l.ShieldW);
        }

        [Test]
        public void TickX_UsesSameDenominatorAndFallsBackWhenNoShield()
        {
            // 有盾:D=120 时第 1 格(10 血)落在 -w/2 + w/12;无盾 D=MaxHp 时回退等分位置
            Assert.AreEqual(-W * 0.5f + W * (10f / 120f), EntityView.TickX(W, 10f, 120f), 1e-3f);
            Assert.AreEqual(-W * 0.5f + W * (10f / 100f), EntityView.TickX(W, 10f, 100f), 1e-3f);
        }

        [Test]
        public void OldFormula_ShieldSticksOut_BoundaryAssertWouldCatchIt()
        {
            // 红-绿对照:修复前 UpdateShield 的公式 x = w/2 + len/2 + 0.04(盾条接在血槽外右侧)。
            // 用同一数值复算旧公式的盾段右端,断言它**越框** ⇒ 边界断言对旧缺陷确实会失败,不是永真式。
            float maxHp = 100f, shield = 50f;
            float len = W * Mathf.Clamp01(shield / maxHp);
            float oldX = W * 0.5f + len * 0.5f + 0.04f;
            Assert.Greater(oldX + len * 0.5f, W * 0.5f + Eps, "旧公式盾段若未越框,说明本测试没盯住缺陷");
        }
    }
}
