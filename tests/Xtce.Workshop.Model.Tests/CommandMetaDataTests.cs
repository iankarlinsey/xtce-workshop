using System.Text;

namespace Xtce.Workshop.Model.Tests;

public class CommandMetaDataTests
{
    private static SpaceSystem LoadCommandsSample()
    {
        using var stream = File.OpenRead(TestPaths.CommandsSample);
        return XtceDocumentReader.Load(stream);
    }

    [Test]
    public void CommandsSampleFixture_IsItselfSchemaValid()
    {
        Assert.Empty(XsdValidation.Validate(File.ReadAllText(TestPaths.CommandsSample)));
    }

    [Test]
    public void Load_ParsesMetaCommandsWithBaseRefsAndVerifiers()
    {
        var commandMetaData = LoadCommandsSample().CommandMetaData!;

        Assert.Equal(["BaseCmd", "DupCmd", "CleanCmd", "GhostCmd"],
            commandMetaData.MetaCommands.Select(m => m.Name).ToList());

        var baseCmd = commandMetaData.MetaCommands[0];
        Assert.Equal(true, baseCmd.Abstract);
        Assert.Null(baseCmd.BaseMetaCommandRef);
        Assert.Equal(1, baseCmd.CompleteVerifiers!.Count);
        Assert.Contains("CmdAck", baseCmd.CompleteVerifiers[0].OuterXml);

        var dupCmd = commandMetaData.MetaCommands[1];
        Assert.Equal("BaseCmd", dupCmd.BaseMetaCommandRef);
        Assert.Null(dupCmd.Abstract);
    }

    [Test]
    public void RoundTrip_CommandsSample_IsLosslessAndSchemaValid()
    {
        var loaded = LoadCommandsSample();

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        Assert.Equal(loaded, reloaded);
        var errors = XsdValidation.Validate(xml);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
    }

    [Test]
    public void RoundTrip_CommandMetaDataWithUnmodeledSets_PreservesThem()
    {
        var xml = """
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="S">
              <CommandMetaData>
                <ArgumentTypeSet>
                  <IntegerArgumentType name="ArgT"/>
                </ArgumentTypeSet>
                <MetaCommandSet>
                  <MetaCommand name="Cmd"/>
                  <BlockMetaCommand name="Block">
                    <MetaCommandStepList>
                      <MetaCommandStep metaCommandRef="Cmd"/>
                    </MetaCommandStepList>
                  </BlockMetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """;
        var loaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        // ArgumentTypeSet is modeled since #95 — no longer a preserved fragment.
        var argumentType = Assert.Single(loaded.CommandMetaData!.ArgumentTypeSet ?? []);
        Assert.Equal(("ArgT", ParameterTypeKind.Integer), (argumentType.Name, argumentType.Kind));
        Assert.Null(loaded.CommandMetaData.Preserved);
        Assert.Equal(["BlockMetaCommand"], loaded.CommandMetaData.PreservedEntries!.Select(f => f.ElementName).ToList());

        var written = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(written)));
        Assert.Equal(loaded, reloaded);
        Assert.Contains("<ArgumentTypeSet", written);
        Assert.Contains("<BlockMetaCommand", written);
        // ArgumentTypeSet must precede MetaCommandSet per CommandMetaDataType's sequence.
        Assert.True(written.IndexOf("<ArgumentTypeSet", StringComparison.Ordinal)
                    < written.IndexOf("<MetaCommandSet", StringComparison.Ordinal));
    }

    [Test]
    public void Load_ModelsCommandSideParameterSets_AndRoundTripsThem()
    {
        var xml = """
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="S">
              <CommandMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="CMD_APID_Type" signed="false">
                    <IntegerDataEncoding sizeInBits="11"/>
                  </IntegerParameterType>
                </ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="CMD_APID" parameterTypeRef="CMD_APID_Type"/>
                </ParameterSet>
                <MetaCommandSet>
                  <MetaCommand name="Cmd"/>
                </MetaCommandSet>
                <CommandContainerSet>
                  <CommandContainer name="Shared"><EntryList/></CommandContainer>
                </CommandContainerSet>
              </CommandMetaData>
            </SpaceSystem>
            """;
        var loaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        var commandMetaData = loaded.CommandMetaData!;

        var type = Assert.Single(commandMetaData.ParameterTypeSet ?? []);
        Assert.Equal((ParameterTypeKind.Integer, 11L), (type.Kind, type.DataEncoding!.SizeInBits!.Value));
        var parameter = Assert.Single(commandMetaData.ParameterSet ?? []);
        Assert.Equal(("CMD_APID", "CMD_APID_Type"), (parameter.Name, parameter.ParameterTypeRef));
        // CommandContainerSet stays a fragment.
        Assert.Equal(["CommandContainerSet"], commandMetaData.Preserved!.Select(f => f.ElementName).ToList());

        var written = XtceDocumentWriter.Write(loaded);
        Assert.Equal(loaded, XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(written))));
        var errors = XsdValidation.Validate(written);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
        // CommandMetaDataType sequence: ParameterTypeSet, ParameterSet, MetaCommandSet, CommandContainerSet.
        var indexes = new[] { "<ParameterTypeSet", "<ParameterSet", "<MetaCommandSet", "<CommandContainerSet" }
            .Select(tag => written.IndexOf(tag, StringComparison.Ordinal)).ToList();
        Assert.True(indexes.All(i => i >= 0) && indexes.SequenceEqual(indexes.OrderBy(i => i)),
            "CommandMetaData children out of schema order: " + string.Join(", ", indexes));
    }
}
