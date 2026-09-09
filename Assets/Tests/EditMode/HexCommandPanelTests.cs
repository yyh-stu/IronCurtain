using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class HexCommandPanelTests
    {
        [Test]
        public void PanelRectFor_位于保留区域内且展开高度受限()
        {
            var collapsed = HexCommandPanel.PanelRectFor(1920, 1080, 306f, 1000f, false);
            var expanded = HexCommandPanel.PanelRectFor(1920, 1080, 306f, 1000f, true);

            Assert.AreEqual(new Rect(1590f, 306f, 320f, 42f), collapsed);
            Assert.AreEqual(new Rect(1590f, 306f, 320f, 310f), expanded);
            Assert.Greater(expanded.height, collapsed.height);
            Assert.LessOrEqual(expanded.yMax, 1000f);
        }

        [Test]
        public void PanelRectFor_窄区间裁切且不越过底部保留线()
        {
            var rect = HexCommandPanel.PanelRectFor(300, 480, 300f, 420f, true);
            Assert.GreaterOrEqual(rect.xMin, 0f);
            Assert.LessOrEqual(rect.xMax, 300f);
            Assert.AreEqual(300f, rect.yMin);
            Assert.AreEqual(420f, rect.yMax);
        }

        [Test]
        public void PanelRectFor_异常保留区仍限制在屏幕内()
        {
            var rect = HexCommandPanel.PanelRectFor(300, 200, 500f, 600f, true);
            Assert.GreaterOrEqual(rect.yMin, 0f);
            Assert.LessOrEqual(rect.yMax, 200f);
            Assert.AreEqual(0f, rect.height);
        }

        [Test]
        public void SetExpanded_状态可预测且生命周期清空命中区()
        {
            var go = new GameObject("HexCommandPanelTest");
            try
            {
                var panel = go.AddComponent<HexCommandPanel>();
                panel.SetExpanded(true);
                Assert.IsTrue(panel.Expanded);
                panel.SetExpanded(false);
                Assert.IsFalse(panel.Expanded);
                HexCommandPanel.LastPanelRect = new Rect(1f, 1f, 20f, 20f);
                typeof(HexCommandPanel).GetMethod("OnDisable",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(panel, null);
                Assert.AreEqual(Rect.zero, HexCommandPanel.LastPanelRect);
            }
            finally
            {
                Object.DestroyImmediate(go);
                HexCommandPanel.LastPanelRect = Rect.zero;
            }
        }
    }
}
