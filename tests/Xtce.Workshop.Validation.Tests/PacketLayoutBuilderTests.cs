using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

public class PacketLayoutBuilderTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static ParameterTypeDefinition EncodedType(string name, long sizeInBits) =>
        new(name, ParameterTypeKind.Integer, Preserved:
            [new RawXmlFragment("IntegerDataEncoding",
                $"""<IntegerDataEncoding sizeInBits="{sizeInBits}" xmlns="{Ns}"/>""")]);

    [Test]
    public void SequentialParameterEntries_GetCumulativeOffsets()
    {
        var telemetry = new TelemetryMetaData(
            [EncodedType("U16", 16), EncodedType("U8", 8)],
            [new Parameter("A", "U16"), new Parameter("B", "U8"), new Parameter("C", "U16")],
            ContainerSet:
            [
                new SequenceContainer("Frame",
                [
                    new SequenceEntry(SequenceEntryKind.ParameterRef, "A"),
                    new SequenceEntry(SequenceEntryKind.ParameterRef, "B"),
                    new SequenceEntry(SequenceEntryKind.ParameterRef, "C"),
                ]),
            ]);
        var root = new SpaceSystem("S", [], telemetry);

        var layout = PacketLayoutBuilder.Build(root, [], "Frame")!;

        Assert.Equal([0L, 16L, 24L], layout.Rows.Select(r => r.OffsetInBits).ToList());
        Assert.Equal([16L, 8L, 16L], layout.Rows.Select(r => r.SizeInBits).ToList());
        Assert.Equal(40, layout.TotalSizeInBits);
    }

    [Test]
    public void InheritedParentEntries_ComeFirst()
    {
        var telemetry = new TelemetryMetaData(
            [EncodedType("U8", 8)],
            [new Parameter("HeaderField", "U8"), new Parameter("Body", "U8")],
            ContainerSet:
            [
                new SequenceContainer("Header", [new SequenceEntry(SequenceEntryKind.ParameterRef, "HeaderField")], Abstract: true),
                new SequenceContainer("Packet",
                    [new SequenceEntry(SequenceEntryKind.ParameterRef, "Body")],
                    new BaseContainer("Header", new RestrictionCriteria(Comparison: new Comparison("HeaderField", "1")))),
            ]);
        var root = new SpaceSystem("S", [], telemetry);

        var layout = PacketLayoutBuilder.Build(root, [], "Packet")!;

        Assert.Equal(["HeaderField", "Body"], layout.Rows.Select(r => r.Name).ToList());
        Assert.Equal(["Header", "Packet"], layout.Rows.Select(r => r.SourceContainer).ToList());
        Assert.Equal(16, layout.TotalSizeInBits);
    }

    [Test]
    public void IncludedContainer_ExpandsInline()
    {
        var telemetry = new TelemetryMetaData(
            [EncodedType("U8", 8)],
            [new Parameter("A", "U8"), new Parameter("B", "U8")],
            ContainerSet:
            [
                new SequenceContainer("Sub", [new SequenceEntry(SequenceEntryKind.ParameterRef, "B")]),
                new SequenceContainer("Frame",
                [
                    new SequenceEntry(SequenceEntryKind.ParameterRef, "A"),
                    new SequenceEntry(SequenceEntryKind.ContainerRef, "Sub"),
                ]),
            ]);
        var root = new SpaceSystem("S", [], telemetry);

        var layout = PacketLayoutBuilder.Build(root, [], "Frame")!;

        Assert.Equal(["A", "B"], layout.Rows.Select(r => r.Name).ToList());
        Assert.Equal(16, layout.TotalSizeInBits);
    }

    [Test]
    public void UnknownSize_MakesFollowingOffsetsAndTotalNull()
    {
        var telemetry = new TelemetryMetaData(
            [EncodedType("U8", 8), new ParameterTypeDefinition("NoEnc", ParameterTypeKind.Integer)],
            [new Parameter("A", "NoEnc"), new Parameter("B", "U8")],
            ContainerSet:
            [
                new SequenceContainer("Frame",
                [
                    new SequenceEntry(SequenceEntryKind.ParameterRef, "A"),
                    new SequenceEntry(SequenceEntryKind.ParameterRef, "B"),
                ]),
            ]);
        var root = new SpaceSystem("S", [], telemetry);

        var layout = PacketLayoutBuilder.Build(root, [], "Frame")!;

        Assert.Equal(0, layout.Rows[0].OffsetInBits);
        Assert.Null(layout.Rows[0].SizeInBits);
        Assert.Null(layout.Rows[1].OffsetInBits); // unknowable after an unknown size
        Assert.Equal(8, layout.Rows[1].SizeInBits);
        Assert.Null(layout.TotalSizeInBits);
    }

    [Test]
    public void FixedContainerStartLocation_ReanchorsTheOffset()
    {
        var telemetry = new TelemetryMetaData(
            [EncodedType("U8", 8)],
            [new Parameter("A", "U8"), new Parameter("B", "U8")],
            ContainerSet:
            [
                new SequenceContainer("Frame",
                [
                    new SequenceEntry(SequenceEntryKind.ParameterRef, "A"),
                    new SequenceEntry(SequenceEntryKind.ParameterRef, "B", Preserved:
                    [
                        new RawXmlFragment("LocationInContainerInBits",
                            $"""<LocationInContainerInBits referenceLocation="containerStart" xmlns="{Ns}"><FixedValue>32</FixedValue></LocationInContainerInBits>"""),
                    ]),
                ]),
            ]);
        var root = new SpaceSystem("S", [], telemetry);

        var layout = PacketLayoutBuilder.Build(root, [], "Frame")!;

        Assert.Equal(32, layout.Rows[1].OffsetInBits);
        Assert.Equal(40, layout.TotalSizeInBits);
    }

    [Test]
    public void RawFixedValueEntry_ContributesItsOwnSize()
    {
        var telemetry = new TelemetryMetaData([], [],
            ContainerSet:
            [
                new SequenceContainer("Frame",
                [
                    new SequenceEntry(SequenceEntryKind.Raw, RawXml: new RawXmlFragment("FixedValueEntry",
                        $"""<FixedValueEntry binaryValue="5A" sizeInBits="7" xmlns="{Ns}"/>""")),
                ]),
            ]);
        var root = new SpaceSystem("S", [], telemetry);

        var layout = PacketLayoutBuilder.Build(root, [], "Frame")!;

        var row = Assert.Single(layout.Rows);
        Assert.Equal("FixedValueEntry", row.Kind);
        Assert.Equal(7, row.SizeInBits);
        Assert.Equal(7, layout.TotalSizeInBits);
    }

    [Test]
    public void InheritanceCycle_TruncatesInsteadOfHanging()
    {
        var telemetry = new TelemetryMetaData([], [],
            ContainerSet:
            [
                new SequenceContainer("A", [], new BaseContainer("B")),
                new SequenceContainer("B", [], new BaseContainer("A")),
            ]);
        var root = new SpaceSystem("S", [], telemetry);

        var layout = PacketLayoutBuilder.Build(root, [], "A")!;

        Assert.Contains(layout.Rows, r => r.Kind == "cycle");
        Assert.Null(layout.TotalSizeInBits);
    }

    [Test]
    public void ContainerInAChildSystem_IsAddressableBySystemPath()
    {
        var child = new SpaceSystem("Bus", [], new TelemetryMetaData(
            [EncodedType("U8", 8)],
            [new Parameter("A", "U8")],
            ContainerSet: [new SequenceContainer("BusFrame", [new SequenceEntry(SequenceEntryKind.ParameterRef, "A")])]));
        var root = new SpaceSystem("Sat", [child]);

        var layout = PacketLayoutBuilder.Build(root, [0], "BusFrame");

        Assert.NotNull(layout);
        Assert.Equal(8, layout!.TotalSizeInBits);
        Assert.Null(PacketLayoutBuilder.Build(root, [], "BusFrame"));
    }

    [Test]
    public void ModeledEncodings_DriveEntrySizes()
    {
        // Reader-built document: encodings are modeled (#96), not fragments — the layout
        // must take Integer/Float sizes from attributes (with XSD defaults) and the
        // String/Binary shapes from the preserved size children.
        var xml = $"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="U12"><IntegerDataEncoding sizeInBits="12"/></IntegerParameterType>
                  <FloatParameterType name="F"><FloatDataEncoding/></FloatParameterType>
                  <StringParameterType name="Fixed64">
                    <StringDataEncoding><SizeInBits><Fixed><FixedValue>64</FixedValue></Fixed></SizeInBits></StringDataEncoding>
                  </StringParameterType>
                  <StringParameterType name="Var256">
                    <StringDataEncoding><Variable maxSizeInBits="256"><DynamicValue><ParameterInstanceRef parameterRef="A"/></DynamicValue></Variable></StringDataEncoding>
                  </StringParameterType>
                </ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="A" parameterTypeRef="U12"/>
                  <Parameter name="B" parameterTypeRef="F"/>
                  <Parameter name="C" parameterTypeRef="Fixed64"/>
                  <Parameter name="D" parameterTypeRef="Var256"/>
                </ParameterSet>
                <ContainerSet>
                  <SequenceContainer name="Frame">
                    <EntryList>
                      <ParameterRefEntry parameterRef="A"/>
                      <ParameterRefEntry parameterRef="B"/>
                      <ParameterRefEntry parameterRef="C"/>
                      <ParameterRefEntry parameterRef="D"/>
                    </EntryList>
                  </SequenceContainer>
                </ContainerSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """;
        var root = XtceDocumentReader.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));

        var layout = PacketLayoutBuilder.Build(root, [], "Frame")!;

        Assert.Equal([12L, 32L, 64L, 256L], layout.Rows.Select(r => r.SizeInBits).ToList());
        Assert.Equal([false, false, false, true], layout.Rows.Select(r => r.IsVariable).ToList());
    }

    [Test]
    public void CommandContainer_LaysOut_FixedValuesAndArguments_WithBaseChain()
    {
        // Inline command containers lay out too (#97): the header comes from the base
        // command's container, the fixed sync marker has an explicit size, and argument
        // sizes come from the merged argument declarations' encodings.
        var xml = $"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <ArgumentTypeSet>
                  <IntegerArgumentType name="U8"><IntegerDataEncoding sizeInBits="8"/></IntegerArgumentType>
                  <IntegerArgumentType name="U16"><IntegerDataEncoding sizeInBits="16"/></IntegerArgumentType>
                </ArgumentTypeSet>
                <MetaCommandSet>
                  <MetaCommand name="Base" abstract="true">
                    <ArgumentList><Argument name="opcode" argumentTypeRef="U8"/></ArgumentList>
                    <CommandContainer name="BaseFrame">
                      <EntryList>
                        <FixedValueEntry name="sync" binaryValue="5A5A" sizeInBits="16"/>
                        <ArgumentRefEntry argumentRef="opcode"/>
                      </EntryList>
                    </CommandContainer>
                  </MetaCommand>
                  <MetaCommand name="Fire">
                    <BaseMetaCommand metaCommandRef="Base"/>
                    <ArgumentList><Argument name="duration" argumentTypeRef="U16"/></ArgumentList>
                    <CommandContainer name="FireFrame">
                      <EntryList>
                        <ArgumentRefEntry argumentRef="duration"/>
                      </EntryList>
                      <BaseContainer containerRef="BaseFrame"/>
                    </CommandContainer>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """;
        var root = XtceDocumentReader.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));

        var layout = PacketLayoutBuilder.Build(root, [], "FireFrame")!;

        Assert.Equal(["sync", "opcode", "duration"], layout.Rows.Select(r => r.Name).ToList());
        Assert.Equal(["fixed", "argument", "argument"], layout.Rows.Select(r => r.Kind).ToList());
        Assert.Equal([0L, 16L, 24L], layout.Rows.Select(r => r.OffsetInBits).ToList());
        Assert.Equal([16L, 8L, 16L], layout.Rows.Select(r => r.SizeInBits).ToList());
        Assert.Equal(40, layout.TotalSizeInBits);
    }

    [Test]
    public void CommandContainer_ParameterEntries_ResolveThroughCommandSideParameterSet()
    {
        // The #98 case: command header parameters live in CommandMetaData's own
        // ParameterSet — the layout must resolve them like telemetry parameters.
        var xml = $"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="Apid_Type" signed="false"><IntegerDataEncoding sizeInBits="11"/></IntegerParameterType>
                </ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="APID" parameterTypeRef="Apid_Type"/>
                </ParameterSet>
                <ArgumentTypeSet>
                  <IntegerArgumentType name="U8"><IntegerDataEncoding sizeInBits="8"/></IntegerArgumentType>
                </ArgumentTypeSet>
                <MetaCommandSet>
                  <MetaCommand name="Cmd">
                    <ArgumentList><Argument name="opcode" argumentTypeRef="U8"/></ArgumentList>
                    <CommandContainer name="Frame">
                      <EntryList>
                        <ParameterRefEntry parameterRef="APID"/>
                        <ArgumentRefEntry argumentRef="opcode"/>
                      </EntryList>
                    </CommandContainer>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """;
        var root = XtceDocumentReader.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));

        var layout = PacketLayoutBuilder.Build(root, [], "Frame")!;

        Assert.Equal([11L, 8L], layout.Rows.Select(r => r.SizeInBits).ToList());
        Assert.Equal([null, null], layout.Rows.Select(r => r.Note).ToList());
        Assert.Equal(19, layout.TotalSizeInBits);
    }

    [Test]
    public void TimeTypeEntries_TakeSizesFromTheModeledEncodingWrapper()
    {
        var xml = $"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <AbsoluteTimeParameterType name="T_Type">
                    <Encoding units="seconds"><IntegerDataEncoding sizeInBits="48"/></Encoding>
                  </AbsoluteTimeParameterType>
                </ParameterTypeSet>
                <ParameterSet><Parameter name="T" parameterTypeRef="T_Type"/></ParameterSet>
                <ContainerSet>
                  <SequenceContainer name="Frame">
                    <EntryList><ParameterRefEntry parameterRef="T"/></EntryList>
                  </SequenceContainer>
                </ContainerSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """;
        var root = XtceDocumentReader.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));

        var layout = PacketLayoutBuilder.Build(root, [], "Frame")!;

        Assert.Equal(48, Assert.Single(layout.Rows).SizeInBits);
        Assert.Equal(48, layout.TotalSizeInBits);
    }

    [Test]
    public void ModeledLocationsAndRepeats_ShapeTheLayout()
    {
        var xml = $"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="U8"><IntegerDataEncoding sizeInBits="8"/></IntegerParameterType>
                </ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="A" parameterTypeRef="U8"/>
                  <Parameter name="B" parameterTypeRef="U8"/>
                </ParameterSet>
                <ContainerSet>
                  <SequenceContainer name="Frame">
                    <EntryList>
                      <ParameterRefEntry parameterRef="A">
                        <LocationInContainerInBits referenceLocation="containerStart"><FixedValue>16</FixedValue></LocationInContainerInBits>
                      </ParameterRefEntry>
                      <ParameterRefEntry parameterRef="B">
                        <RepeatEntry><Count><FixedValue>4</FixedValue></Count></RepeatEntry>
                      </ParameterRefEntry>
                    </EntryList>
                  </SequenceContainer>
                </ContainerSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """;
        var root = XtceDocumentReader.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));

        var layout = PacketLayoutBuilder.Build(root, [], "Frame")!;

        Assert.Equal([16L, 24L], layout.Rows.Select(r => r.OffsetInBits).ToList());
        Assert.Equal([8L, 32L], layout.Rows.Select(r => r.SizeInBits).ToList());
        Assert.Equal("\u00d74 repeat", layout.Rows[1].Note);
        Assert.Equal(56, layout.TotalSizeInBits);
    }
}
