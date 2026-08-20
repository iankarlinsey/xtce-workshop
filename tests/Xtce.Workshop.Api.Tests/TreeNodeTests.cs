using Xtce.Workshop.Model;

namespace Xtce.Workshop.Api.Tests;

public class TreeNodeTests
{
    [Test]
    public void FromSpaceSystem_Childless_ReturnsSingleNodeWithNoChildren()
    {
        var spaceSystem = new SpaceSystem("Minimal", []);

        var node = TreeNode.FromSpaceSystem(spaceSystem);

        Assert.Equal("Minimal", node.Label);
        Assert.Equal("SpaceSystem", node.NodeType);
        Assert.Empty(node.Children);
    }

    [Test]
    public void FromSpaceSystem_Nested_ProjectsCorrectStructureAtEveryLevel()
    {
        var spaceSystem = new SpaceSystem("Mission", [
            new SpaceSystem("Bus", [
                new SpaceSystem("Power", []),
                new SpaceSystem("Thermal", []),
            ]),
            new SpaceSystem("Payload", []),
        ]);

        var node = TreeNode.FromSpaceSystem(spaceSystem);

        Assert.Equal("Mission", node.Label);
        Assert.Equal(2, node.Children.Count);

        var bus = node.Children[0];
        Assert.Equal("Bus", bus.Label);
        Assert.Equal("SpaceSystem", bus.NodeType);
        Assert.Equal(2, bus.Children.Count);
        Assert.Equal("Power", bus.Children[0].Label);
        Assert.Equal("Thermal", bus.Children[1].Label);

        var payload = node.Children[1];
        Assert.Equal("Payload", payload.Label);
        Assert.Empty(payload.Children);
    }
}
