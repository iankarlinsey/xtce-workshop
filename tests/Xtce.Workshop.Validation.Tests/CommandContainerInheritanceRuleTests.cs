using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R21: MetaCommand/CommandContainer inheritance wiring (warning).</summary>
public class CommandContainerInheritanceRuleTests
{
    private const string R21 = "XTCE-1.2-R21-metacommand-commandcontainer-inheritance-requires-basecontainer";

    private static SpaceSystem Document(params MetaCommand[] metaCommands) =>
        new("S", [], CommandMetaData: new CommandMetaData(metaCommands));

    [Test]
    public void InheritanceWithoutWiring_Warns()
    {
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Parent", CommandContainer: new CommandContainer("ParentCC")),
            new MetaCommand("Child", BaseMetaCommandRef: "Parent",
                CommandContainer: new CommandContainer("ChildCC"))));

        var issue = Assert.Single(issues, i => i.RuleId == R21);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Contains("will not be inherited", issue.Message);
    }

    [Test]
    public void InheritanceWithWiring_IsClean()
    {
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Parent", CommandContainer: new CommandContainer("ParentCC")),
            new MetaCommand("Child", BaseMetaCommandRef: "Parent",
                CommandContainer: new CommandContainer("ChildCC", BaseContainerRef: "ParentCC"))));

        Assert.DoesNotContain(issues, i => i.RuleId == R21);
    }

    [Test]
    public void ExtendingAParentWithoutACommandContainer_IsClean()
    {
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Parent"),
            new MetaCommand("Child", BaseMetaCommandRef: "Parent",
                CommandContainer: new CommandContainer("ChildCC"))));

        Assert.DoesNotContain(issues, i => i.RuleId == R21);
    }

    [Test]
    public void WiringToAnInlineContainerWithoutInheritance_Warns()
    {
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Other", CommandContainer: new CommandContainer("OtherCC")),
            new MetaCommand("Loner",
                CommandContainer: new CommandContainer("LonerCC", BaseContainerRef: "OtherCC"))));

        var issue = Assert.Single(issues, i => i.RuleId == R21);
        Assert.Contains("should not be included", issue.Message);
    }

    [Test]
    public void WiringToANonInlineContainerWithoutInheritance_IsClean_HeadersAreLegal()
    {
        // BaseContainer refs to CommandContainerSet containers or telemetry
        // SequenceContainers are legal outside MetaCommand inheritance.
        var telemetry = new TelemetryMetaData([], [], ContainerSet: [new SequenceContainer("Header", [])]);
        var spaceSystem = new SpaceSystem("S", [], telemetry,
            CommandMetaData: new CommandMetaData(
            [
                new MetaCommand("Loner",
                    CommandContainer: new CommandContainer("LonerCC", BaseContainerRef: "Header")),
            ]));

        var issues = XtceValidator.Validate(spaceSystem);

        Assert.DoesNotContain(issues, i => i.RuleId == R21);
    }
}
