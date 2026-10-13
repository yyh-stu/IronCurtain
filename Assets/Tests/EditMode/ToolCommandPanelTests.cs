using System.Reflection;
using System.Collections.Generic;
using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class ToolCommandPanelTests
    {
        private static GameConfig Config() =>
            GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);

        private static SimCore NewSim() =>
            new SimCore(new SimConfig(), new SeededRng(51)) { Game = Config() };

        private static void GiveOneEp(SimCore sim)
        {
            sim.SpawnBuilding(Side.Blue, BuildingId.ResourcePoint, new GridPos(0, 24));
            for (int i = 0; i < 905; i++) sim.Step();
        }

        private static ToolCommandPanel NewPanel(SimCore sim, out GameObject go)
        {
            go = new GameObject("ToolCommandPanelTest");
            var panel = go.AddComponent<ToolCommandPanel>();
            panel.Init(sim, Side.Blue);
            return panel;
        }

        [SetUp]
        public void SetUp() => VisionSystem.Reset();

        [TearDown]
        public void TearDown()
        {
            VisionSystem.Reset();
            ToolCommandPanel.LastPanelRect = Rect.zero;
        }

        [Test]
        public void PanelRectFor_位于兵营面板下方且裁切在屏幕内()
        {
            Assert.AreEqual(new Rect(10f, 250f, 340f, 174f),
                ToolCommandPanel.PanelRectFor(1920, 1080, 250f, true));
            var narrow = ToolCommandPanel.PanelRectFor(300, 200, 500f, true);
            Assert.GreaterOrEqual(narrow.xMin, 0f);
            Assert.LessOrEqual(narrow.xMax, 300f);
            Assert.LessOrEqual(narrow.yMax, 200f);
            Assert.AreEqual(0f, narrow.height);
        }

        [Test]
        public void CanShowActions_仅有效道具状态展示可执行按钮()
        {
            Assert.IsFalse(ToolCommandPanel.CanShowActions(new ToolCommandState(false, 0f, 0, 0)));
            Assert.IsTrue(ToolCommandPanel.CanShowActions(new ToolCommandState(true, 1f, 1, 1)));
        }

        [Test]
        public void Arm_EP不足安全拒绝且不进入目标模式()
        {
            var panel = NewPanel(NewSim(), out var go);
            try
            {
                Assert.IsFalse(panel.Arm(ToolTargetMode.Ram));
                Assert.AreEqual(ToolTargetMode.None, panel.PendingTarget);
                StringAssert.Contains("EP 不足", panel.LastFeedback);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void 攻城锥_可见敌方建筑成功后退出目标模式()
        {
            var sim = NewSim();
            var vision = sim.SpawnBuilding(Side.Blue, BuildingId.Tower, new GridPos(15, 90));
            var target = sim.SpawnBuilding(Side.Red, BuildingId.Tower, new GridPos(15, 110));
            GiveOneEp(sim);
            Assert.IsNotNull(vision);
            var panel = NewPanel(sim, out var go);
            try
            {
                float hp = target.Hp;
                Assert.IsTrue(panel.Arm(ToolTargetMode.Ram));
                var selected = new List<int> { 4321 };
                var coordinator = new SelectionInteractionCoordinator(sim, Side.Blue, selected);
                coordinator.AttachToolPanel(panel);
                coordinator.HandleWorldClick(9999, true, SimVec2.FromGrid(target.Pos));
                Assert.AreEqual(ToolTargetMode.None, panel.PendingTarget);
                Assert.AreEqual(hp - sim.Game.Tools.RamDamage, target.Hp, 0.001f);
                Assert.AreEqual(0f, sim.EpOf(Side.Blue), 0.001f);
                CollectionAssert.AreEqual(new[] { 4321 }, selected,
                    "道具目标点击不得改写当前选择");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void 临时阵地_地图外失败保留重选_合法空格成功()
        {
            var sim = NewSim();
            GiveOneEp(sim);
            int before = sim.EntityCount;
            var panel = NewPanel(sim, out var go);
            try
            {
                Assert.IsTrue(panel.Arm(ToolTargetMode.Fort));
                Assert.IsTrue(panel.TryHandleWorldClick(true, new SimVec2(0.5f, 0.5f)));
                Assert.AreEqual(ToolTargetMode.Fort, panel.PendingTarget);
                Assert.AreEqual(before, sim.EntityCount);
                Assert.AreEqual(1f, sim.EpOf(Side.Blue), 0.001f);
                Assert.IsTrue(panel.TryHandleWorldClick(true, SimVec2.FromGrid(new GridPos(80, 10))));   // 2026-09-16 补记:换己方大本营区域内的合法格(旧格 (15,100) 非路非广场)
                Assert.AreEqual(ToolTargetMode.None, panel.PendingTarget);
                Assert.AreEqual(before + 1, sim.EntityCount);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void 取消和禁用_清除目标模式与命中区()
        {
            var sim = NewSim();
            GiveOneEp(sim);
            var panel = NewPanel(sim, out var go);
            try
            {
                Assert.IsTrue(panel.Arm(ToolTargetMode.Ram));
                Assert.IsTrue(panel.CancelTargeting("test"));
                Assert.AreEqual(ToolTargetMode.None, panel.PendingTarget);
                StringAssert.Contains("已取消", panel.LastFeedback);
                panel.Arm(ToolTargetMode.Fort);
                ToolCommandPanel.LastPanelRect = new Rect(1f, 1f, 10f, 10f);
                typeof(ToolCommandPanel).GetMethod("OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
                Assert.AreEqual(ToolTargetMode.None, panel.PendingTarget);
                Assert.AreEqual(Rect.zero, ToolCommandPanel.LastPanelRect);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
