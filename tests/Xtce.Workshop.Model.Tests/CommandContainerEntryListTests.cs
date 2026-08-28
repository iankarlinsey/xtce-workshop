using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Modeled CommandContainer EntryList (#97): ArgumentRefEntry and FixedValueEntry are
/// first-class alongside the shared ParameterRefEntry/ContainerRefEntry; the remaining
/// command entry kinds ride as Raw entries in position.
/// </summary>
public class CommandContainerEntryListTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Sample => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <CommandMetaData>
            <ArgumentTypeSet>
              <IntegerArgumentType name="U8" sizeInBits="8"><IntegerDataEncoding sizeInBits="8"/></IntegerArgumentType>
            </ArgumentTypeSet>
            <MetaCommandSet>
              <MetaCommand name="Cmd">
                <ArgumentList><Argument name="opcode" argumentTypeRef="U8"/></ArgumentList>
                <CommandContainer name="CmdFrame">
                  <EntryList>
                    <FixedValueEntry name="sync" binaryValue="5A5A" sizeInBits="16"/>
                    <ArgumentRefEntry argumentRef="opcode">
                      <IncludeCondition><Comparison value="1"><ParameterInstanceRef parameterRef="Mode"/></Comparison></IncludeCondition>
                    </ArgumentRefEntry>
                    <!-- trailer -->
                    <ArrayArgumentRefEntry argumentRef="opcode"/>
                  </EntryList>
                </CommandContainer>
              </MetaCommand>
            </MetaCommandSet>
          </CommandMetaData>
        </SpaceSystem>
        """;

    [Test]
    public void Load_ModelsCommandEntryKinds_AndKeepsRawEntriesInPosition()
    {
        var container = Load(Sample).CommandMetaData!.MetaCommands.Single().CommandContainer!;

        var entries = container.EntryList!;
        Assert.Equal(
            [SequenceEntryKind.FixedValue, SequenceEntryKind.ArgumentRef, SequenceEntryKind.Raw, SequenceEntryKind.Raw],
            entries.Select(e => e.Kind));

        Assert.Equal(("sync", "5A5A", 16L), (entries[0].Name, entries[0].BinaryValue, entries[0].SizeInBits!.Value));
        Assert.Equal("opcode", entries[1].Ref);
        // IncludeCondition is modeled (#109); its instance-ref comparison (not the plain
        // form) rides preserved INSIDE the criteria.
        Assert.Null(entries[1].Preserved);
        Assert.Equal(["Comparison"], entries[1].IncludeCondition!.Preserved!.Select(f => f.ElementName).ToList());
        Assert.Equal(CommentAnchor.ElementName, entries[2].RawXml!.ElementName);
        Assert.Equal("ArrayArgumentRefEntry", entries[3].RawXml!.ElementName);

        // The EntryList no longer rides as one opaque fragment.
        Assert.Null(container.Preserved);
    }

    [Test]
    public void RoundTrip_IsLosslessAndSchemaValid()
    {
        var loaded = Load(Sample);

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = Load(xml);

        Assert.Equal(loaded, reloaded);
        var errors = XsdValidation.Validate(xml);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
        // Entry order is the packet layout — the raw entry must stay third, after the
        // modeled ones, with the comment in place.
        var fixedIndex = xml.IndexOf("<FixedValueEntry", StringComparison.Ordinal);
        var argumentIndex = xml.IndexOf("<ArgumentRefEntry", StringComparison.Ordinal);
        var commentIndex = xml.IndexOf("<!-- trailer -->", StringComparison.Ordinal);
        var arrayIndex = xml.IndexOf("<ArrayArgumentRefEntry", StringComparison.Ordinal);
        Assert.True(fixedIndex >= 0 && fixedIndex < argumentIndex && argumentIndex < commentIndex && commentIndex < arrayIndex);
    }

    [Test]
    public void Load_EmptyEntryList_IsAnEmptyNonNullList()
    {
        var document = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <MetaCommandSet>
                  <MetaCommand name="Cmd">
                    <CommandContainer name="CC"><EntryList/></CommandContainer>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);

        var container = document.CommandMetaData!.MetaCommands.Single().CommandContainer!;
        Assert.NotNull(container.EntryList);
        Assert.Empty(container.EntryList!);
    }
}
