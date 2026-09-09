using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class UpgradeCommandPanelTests
    {
        [Test]
        public void PanelRectFor_常规分辨率位于右上且不碰顶栏()
        {
            var collapsed = UpgradeCommandPanel.PanelRectFor(1920, 1080, false);
            var expanded = UpgradeCommandPanel.PanelRectFor(1920, 1080, true);

            Assert.AreEqual(new Rect(1590f, 50f, 320f, 42f), collapsed);
            Assert.AreEqual(new Rect(1590f, 50f, 320f, 248f), expanded);
            Assert.GreaterOrEqual(collapsed.yMin, 40f);
            Assert.Greater(expanded.height, collapsed.height);
        }

        [Test]
        public void PanelRectFor_窄屏仍完整收在屏幕内()
        {
            var rect = UpgradeCommandPanel.PanelRectFor(300, 180, true);
            Assert.GreaterOrEqual(rect.xMin, 0f);
            Assert.GreaterOrEqual(rect.yMin, 0f);
            Assert.LessOrEqual(rect.xMax, 300f);
            Assert.LessOrEqual(rect.yMax, 180f);
        }

        [Test]
        public void SetExpanded_状态可预测且禁用时清空命中区()
        {
            var go = new GameObject("UpgradeCommandPanelTest");
            try
            {
                var panel = go.AddComponent<UpgradeCommandPanel>();
                panel.SetExpanded(true);
                Assert.IsTrue(panel.Expanded);
                panel.SetExpanded(false);
                Assert.IsFalse(panel.Expanded);

                UpgradeCommandPanel.LastPanelRect = new Rect(1f, 1f, 20f, 20f);
                typeof(UpgradeCommandPanel).GetMethod("OnDisable",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(panel, null);
                Assert.AreEqual(Rect.zero, UpgradeCommandPanel.LastPanelRect);
            }
            finally
            {
                Object.DestroyImmediate(go);
                UpgradeCommandPanel.LastPanelRect = Rect.zero;
            }
        }
    }
}
