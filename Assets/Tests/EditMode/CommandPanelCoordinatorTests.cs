using IronCurtain.Sim;
using IronCurtain.View;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class CommandPanelCoordinatorTests
    {
        [Test]
        public void OpenedPanel_折叠其他展开面板()
        {
            var root = new GameObject("CommandPanelCoordinatorTest");
            try
            {
                var upgrades = root.AddComponent<UpgradeCommandPanel>();
                var hexes = root.AddComponent<HexCommandPanel>();
                var tools = root.AddComponent<ToolCommandPanel>();
                var coordinator = new CommandPanelCoordinator();
                coordinator.Attach(upgrades, hexes, tools);

                upgrades.SetExpanded(true);
                Assert.IsTrue(upgrades.Expanded);
                hexes.SetExpanded(true);
                Assert.IsFalse(upgrades.Expanded);
                Assert.IsTrue(hexes.Expanded);
                tools.SetExpanded(true);
                Assert.IsFalse(hexes.Expanded);
                Assert.IsTrue(tools.Expanded);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void 切换到非道具面板_取消未完成道具目标模式()
        {
            var config = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig(), new SeededRng(11)) { Game = config };
            sim.SpawnBuilding(Side.Blue, BuildingId.ResourcePoint, new GridPos(15, 20));
            for (int i = 0; i < 905; i++) sim.Step();
            var root = new GameObject("CommandPanelTargetTest");
            try
            {
                var upgrades = root.AddComponent<UpgradeCommandPanel>();
                var hexes = root.AddComponent<HexCommandPanel>();
                var tools = root.AddComponent<ToolCommandPanel>();
                tools.Init(sim, Side.Blue);
                new CommandPanelCoordinator().Attach(upgrades, hexes, tools);

                Assert.IsTrue(tools.Arm(ToolTargetMode.Ram));
                upgrades.SetExpanded(true);
                Assert.AreEqual(ToolTargetMode.None, tools.PendingTarget);
                StringAssert.Contains("已取消", tools.LastFeedback);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
