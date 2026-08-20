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

        Assert.Equal(["ArgumentTypeSet"], loaded.CommandMetaData!.Preserved!.Select(f => f.ElementName).ToList());
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
}
