using System.Text;

namespace Xtce.Workshop.Model.Tests;

public class BlockMetaCommandTests
{
    private static SpaceSystem LoadBlocksSample()
    {
        using var stream = File.OpenRead(TestPaths.BlocksSample);
        return XtceDocumentReader.Load(stream);
    }

    private static SpaceSystem LoadXml(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string WrapSetEntries(string entries) =>
        $"""
        <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="S">
          <CommandMetaData>
            <MetaCommandSet>
              <MetaCommand name="Anchor"/>
              {entries}
            </MetaCommandSet>
          </CommandMetaData>
        </SpaceSystem>
        """;

    private static string RoundTrip(SpaceSystem loaded, out SpaceSystem reloaded)
    {
        var xml = XtceDocumentWriter.Write(loaded);
        reloaded = LoadXml(xml);
        return xml;
    }

    [Test]
    public void BlocksSampleFixture_IsItselfSchemaValid()
    {
        Assert.Empty(XsdValidation.Validate(File.ReadAllText(TestPaths.BlocksSample)));
    }

    [Test]
    public void Load_ParsesBlockMetaCommandWithSteps()
    {
        var command = LoadBlocksSample().CommandMetaData!;

        var block = Assert.Single(command.BlockMetaCommands!);
        Assert.Equal("ArmAndFire", block.Name);
        Assert.Equal("Arms the selected channel, then fires.", block.Description!.LongDescription);
        Assert.Null(block.Preserved);

        Assert.Equal(2, block.Steps!.Count);
        Assert.Equal("Arm", block.Steps[0].MetaCommandRef);
        var assignment = Assert.Single(block.Steps[0].ArgumentAssignments!);
        Assert.Equal("Channel", assignment.ArgumentName);
        Assert.Equal("3", assignment.ArgumentValue);
        Assert.Equal("Fire", block.Steps[1].MetaCommandRef);
        Assert.Null(block.Steps[1].ArgumentAssignments); // element absent stays null
    }

    [Test]
    public void Load_ParsesMetaCommandRefTextEntries()
    {
        var command = LoadBlocksSample().CommandMetaData!;

        Assert.Equal(["Payload/Deploy"], command.MetaCommandRefs!.ToList());
        Assert.Null(command.PreservedEntries);
    }

    [Test]
    public void Load_AcceptsCorrectedArgumentAssignmentListSpellingToo()
    {
        // The XSD's element is "ArgumentAssigmentList" (sic); files written against the
        // corrected spelling load into the same model.
        var loaded = LoadXml(WrapSetEntries("""
            <BlockMetaCommand name="B">
              <MetaCommandStepList>
                <MetaCommandStep metaCommandRef="Anchor">
                  <ArgumentAssignmentList>
                    <ArgumentAssignment argumentName="A" argumentValue="1"/>
                  </ArgumentAssignmentList>
                </MetaCommandStep>
              </MetaCommandStepList>
            </BlockMetaCommand>
            """));

        var block = Assert.Single(loaded.CommandMetaData!.BlockMetaCommands!);
        Assert.Equal("1", Assert.Single(block.Steps![0].ArgumentAssignments!).ArgumentValue);
    }

    [Test]
    public void RoundTrip_BlocksSample_IsLosslessAndEmitsTheXsdTypoSpelling()
    {
        var loaded = LoadBlocksSample();

        var xml = RoundTrip(loaded, out var reloaded);

        Assert.Equal(loaded, reloaded);
        Assert.Contains("<ArgumentAssigmentList>", xml);
        Assert.DoesNotContain("<ArgumentAssignmentList>", xml);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void RoundTrip_ProgrammaticallyBuiltBlock_IsLosslessAndSchemaValid()
    {
        var command = new CommandMetaData(
            [new MetaCommand("Step1"), new MetaCommand("Step2")],
            BlockMetaCommands:
            [
                new BlockMetaCommand("Sequence",
                    [
                        new MetaCommandStep("Step1", [new ArgumentAssignment("N", "2")]),
                        new MetaCommandStep("Step2"),
                    ]),
            ],
            MetaCommandRefs: ["Other/Included"]);
        var original = new SpaceSystem("S", [], CommandMetaData: command);

        var xml = RoundTrip(original, out var reloaded);

        Assert.Equal(original, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_MetaCommandRefWithElementContentIsPreservedNotModeled()
    {
        var loaded = LoadXml(WrapSetEntries("<MetaCommandRef>Ref<b/></MetaCommandRef>"));

        Assert.Null(loaded.CommandMetaData!.MetaCommandRefs);
        var fragment = Assert.Single(loaded.CommandMetaData.PreservedEntries!);
        Assert.Equal("MetaCommandRef", fragment.ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_BlockWithoutNameIsPreservedWhole()
    {
        var loaded = LoadXml(WrapSetEntries("<BlockMetaCommand><MetaCommandStepList/></BlockMetaCommand>"));

        Assert.Null(loaded.CommandMetaData!.BlockMetaCommands);
        Assert.Equal("BlockMetaCommand", Assert.Single(loaded.CommandMetaData.PreservedEntries!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_StepMissingItsRequiredRefRollsTheWholeListBackToPreserved()
    {
        // Partial-parse rollback: nothing from the list may be silently dropped.
        var loaded = LoadXml(WrapSetEntries("""
            <BlockMetaCommand name="B">
              <MetaCommandStepList>
                <MetaCommandStep metaCommandRef="Anchor"/>
                <MetaCommandStep/>
              </MetaCommandStepList>
            </BlockMetaCommand>
            """));

        var block = Assert.Single(loaded.CommandMetaData!.BlockMetaCommands!);
        Assert.Null(block.Steps);
        Assert.Equal("MetaCommandStepList", Assert.Single(block.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_CommentInsideStepListRollsBackToPreserved()
    {
        var loaded = LoadXml(WrapSetEntries("""
            <BlockMetaCommand name="B">
              <MetaCommandStepList>
                <!-- fire second -->
                <MetaCommandStep metaCommandRef="Anchor"/>
              </MetaCommandStepList>
            </BlockMetaCommand>
            """));

        var block = Assert.Single(loaded.CommandMetaData!.BlockMetaCommands!);
        Assert.Null(block.Steps);
        Assert.Equal("MetaCommandStepList", Assert.Single(block.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_AssignmentWithExtraAttributeRollsBackToPreserved()
    {
        var loaded = LoadXml(WrapSetEntries("""
            <BlockMetaCommand name="B">
              <MetaCommandStepList>
                <MetaCommandStep metaCommandRef="Anchor">
                  <ArgumentAssigmentList>
                    <ArgumentAssignment argumentName="A" argumentValue="1" extra="x"/>
                  </ArgumentAssigmentList>
                </MetaCommandStep>
              </MetaCommandStepList>
            </BlockMetaCommand>
            """));

        var block = Assert.Single(loaded.CommandMetaData!.BlockMetaCommands!);
        Assert.Null(block.Steps);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_UnknownStepChildRidesInStepPreserved()
    {
        var loaded = LoadXml(WrapSetEntries("""
            <BlockMetaCommand name="B">
              <MetaCommandStepList>
                <MetaCommandStep metaCommandRef="Anchor">
                  <Mystery/>
                </MetaCommandStep>
              </MetaCommandStepList>
            </BlockMetaCommand>
            """));

        var block = Assert.Single(loaded.CommandMetaData!.BlockMetaCommands!);
        var step = Assert.Single(block.Steps!);
        Assert.Equal("Mystery", Assert.Single(step.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_MetaCommandRefWithAttributesIsPreservedNotModeled()
    {
        var loaded = LoadXml(WrapSetEntries("""<MetaCommandRef mystery="1">Ref</MetaCommandRef>"""));

        Assert.Null(loaded.CommandMetaData!.MetaCommandRefs);
        Assert.Equal("MetaCommandRef", Assert.Single(loaded.CommandMetaData.PreservedEntries!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_AssignmentListWithAttributesRidesInStepPreserved()
    {
        var loaded = LoadXml(WrapSetEntries("""
            <BlockMetaCommand name="B">
              <MetaCommandStepList>
                <MetaCommandStep metaCommandRef="Anchor">
                  <ArgumentAssigmentList mystery="1"/>
                </MetaCommandStep>
              </MetaCommandStepList>
            </BlockMetaCommand>
            """));

        var block = Assert.Single(loaded.CommandMetaData!.BlockMetaCommands!);
        var step = Assert.Single(block.Steps!);
        Assert.Null(step.ArgumentAssignments);
        Assert.Equal("ArgumentAssigmentList", Assert.Single(step.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_EmptyAssignmentListStaysAnEmptyModeledList()
    {
        var loaded = LoadXml(WrapSetEntries("""
            <BlockMetaCommand name="B">
              <MetaCommandStepList>
                <MetaCommandStep metaCommandRef="Anchor">
                  <ArgumentAssigmentList></ArgumentAssigmentList>
                </MetaCommandStep>
              </MetaCommandStepList>
            </BlockMetaCommand>
            """));

        var step = Assert.Single(Assert.Single(loaded.CommandMetaData!.BlockMetaCommands!).Steps!);
        Assert.NotNull(step.ArgumentAssignments);
        Assert.Empty(step.ArgumentAssignments!);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_CommentInsideAssignmentListRollsBackTheStepList()
    {
        var loaded = LoadXml(WrapSetEntries("""
            <BlockMetaCommand name="B">
              <MetaCommandStepList>
                <MetaCommandStep metaCommandRef="Anchor">
                  <ArgumentAssigmentList>
                    <!-- channel three -->
                    <ArgumentAssignment argumentName="A" argumentValue="1"/>
                  </ArgumentAssigmentList>
                </MetaCommandStep>
              </MetaCommandStepList>
            </BlockMetaCommand>
            """));

        var block = Assert.Single(loaded.CommandMetaData!.BlockMetaCommands!);
        Assert.Null(block.Steps);
        Assert.Equal("MetaCommandStepList", Assert.Single(block.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_UnknownBlockChildAndAttributesArePreserved()
    {
        var loaded = LoadXml(WrapSetEntries("""
            <BlockMetaCommand name="B" shortDescription="two-step">
              <Mystery/>
              <MetaCommandStepList/>
            </BlockMetaCommand>
            """));

        var block = Assert.Single(loaded.CommandMetaData!.BlockMetaCommands!);
        Assert.Equal("two-step",
            Assert.Single(block.PreservedAttributes!).Value);
        Assert.Equal("Mystery", Assert.Single(block.Preserved!).ElementName);
        Assert.NotNull(block.Steps);
        Assert.Empty(block.Steps!);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }
}
