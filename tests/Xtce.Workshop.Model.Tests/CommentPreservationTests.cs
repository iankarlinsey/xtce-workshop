using System.Text;
using Xunit;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Issue #51: XML comments must survive the load → save round trip. Placement guarantees,
/// per the design recorded on the issue: comments keep their parent element and land
/// immediately before the sibling they preceded (or its slot group), leading comments on
/// set items precede the item's start tag exactly, entry-list comments keep their exact
/// entry position, and document prolog/epilog comments (license headers) survive around
/// the root element. Comments inside preserved fragments were already byte-exact.
/// </summary>
public class CommentPreservationTests
{
    private static string RoundTrip(string xml)
    {
        var document = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        return XtceDocumentWriter.Write(document);
    }

    private static void AssertOrdered(string haystack, params string[] needles)
    {
        var position = -1;
        foreach (var needle in needles)
        {
            var next = haystack.IndexOf(needle, position + 1, StringComparison.Ordinal);
            Assert.True(next > position, $"expected '{needle}' after position {position} in:\n{haystack}");
            position = next;
        }
    }

    [Fact]
    public void PrologAndEpilogComments_SurviveAroundTheRootElement()
    {
        var output = RoundTrip("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!-- Copyright (c) Example Corp. License header. -->
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat"/>
            <!-- end of file -->
            """);

        AssertOrdered(output,
            "<!-- Copyright (c) Example Corp. License header. -->",
            "<SpaceSystem",
            "<!-- end of file -->");
    }

    [Fact]
    public void CommentsBetweenModeledChildren_KeepTheirPosition()
    {
        var output = RoundTrip("""
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <!-- telemetry side -->
              <TelemetryMetaData>
                <!-- types first -->
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <!-- then parameters -->
                <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        AssertOrdered(output,
            "<!-- telemetry side -->",
            "<TelemetryMetaData>",
            "<!-- types first -->",
            "<ParameterTypeSet>",
            "<!-- then parameters -->",
            "<ParameterSet>");
    }

    [Fact]
    public void LeadingCommentOnASpecificSetItem_StaysWithThatItem()
    {
        // The middle parameter of three carries the comment — the grouping trap the
        // slot-order writer can't express is exactly what Leading attachment solves.
        var output = RoundTrip("""
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="A" parameterTypeRef="T"/>
                  <!-- battery bus voltage, calibrated -->
                  <Parameter name="B" parameterTypeRef="T"/>
                  <Parameter name="C" parameterTypeRef="T"/>
                </ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        AssertOrdered(output,
            "\"A\"",
            "<!-- battery bus voltage, calibrated -->",
            "\"B\"",
            "\"C\"");
    }

    [Fact]
    public void EntryListComments_KeepExactEntryPosition()
    {
        var output = RoundTrip("""
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="A" parameterTypeRef="T"/>
                  <Parameter name="B" parameterTypeRef="T"/>
                </ParameterSet>
                <ContainerSet>
                  <SequenceContainer name="Frame"><EntryList>
                    <ParameterRefEntry parameterRef="A"/>
                    <!-- word boundary -->
                    <ParameterRefEntry parameterRef="B"/>
                  </EntryList></SequenceContainer>
                </ContainerSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        AssertOrdered(output,
            "parameterRef=\"A\"",
            "<!-- word boundary -->",
            "parameterRef=\"B\"");
    }

    [Fact]
    public void CommentsOnContainersAndTrailingTheContainerSet_Survive()
    {
        var output = RoundTrip("""
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <ContainerSet>
                  <!-- housekeeping packets -->
                  <SequenceContainer name="Hk"><EntryList/></SequenceContainer>
                  <!-- end of container definitions -->
                </ContainerSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        AssertOrdered(output,
            "<!-- housekeeping packets -->",
            "\"Hk\"",
            "<!-- end of container definitions -->");
        // The trailing comment must stay INSIDE the ContainerSet.
        AssertOrdered(output, "<!-- end of container definitions -->", "</ContainerSet>");
    }

    [Fact]
    public void CommandSideComments_SurviveOnMetaCommandsAndInsideThem()
    {
        var output = RoundTrip("""
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <CommandMetaData>
                <!-- commands below -->
                <MetaCommandSet>
                  <!-- reboot is gated on the safe-mode interlock -->
                  <MetaCommand name="Reboot">
                    <!-- args someday -->
                    <ArgumentList><Argument name="A" argumentTypeRef="U8"/></ArgumentList>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);

        AssertOrdered(output,
            "<!-- commands below -->",
            "<MetaCommandSet>",
            "<!-- reboot is gated on the safe-mode interlock -->",
            "\"Reboot\"",
            "<!-- args someday -->",
            "<ArgumentList"); // no '>' — preserved fragments carry the inherited xmlns
    }

    [Fact]
    public void RoundTrippedOutputWithComments_IsStillSchemaValid()
    {
        var output = RoundTrip("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!-- header -->
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <!-- telemetry -->
              <TelemetryMetaData>
                <ParameterTypeSet><!-- t --><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet><Parameter name="P" parameterTypeRef="T"/><!-- tail --></ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        Assert.Empty(XsdValidation.Validate(output));
    }

    [Fact]
    public void SecondRoundTrip_IsStable()
    {
        var first = RoundTrip("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!-- header -->
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <!-- types -->
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet>
                  <!-- p -->
                  <Parameter name="P" parameterTypeRef="T"/>
                </ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        Assert.Equal(first, RoundTrip(first));
    }

    [Fact]
    public void JsonUnsafeCommentText_IsSanitizedNotThrown()
    {
        // Text with "--" can only arrive via the JSON API (it is unparseable as XML);
        // the writer must degrade it gracefully rather than throw mid-save.
        var document = new SpaceSystem("Sat", [], Preserved:
            [new RawXmlFragment(CommentAnchor.ElementName, "a--b-", CommentAnchor.Leading)]);

        var output = XtceDocumentWriter.Write(document);

        Assert.Contains("<!--a- -b- -->", output);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(output)));
        Assert.Equal("Sat", reloaded.Name);
    }
}
