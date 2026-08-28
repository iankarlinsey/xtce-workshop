using Xtce.Workshop.Model;

namespace Xtce.Workshop.Api.Tests;

/// <summary>
/// The session API's kind plumbing (#129): every kind string must resolve through
/// ItemsOf/NameOf/WithList/ClrType, so a future modeled kind that forgets this map
/// fails here rather than silently 404ing in the session endpoints.
/// </summary>
public class DocumentItemsTests
{
    private static SpaceSystem OneOfEverything() => new(
        "Sat",
        [new SpaceSystem("Bus", [])],
        new TelemetryMetaData(
            [new ParameterTypeDefinition("TmType", ParameterTypeKind.Integer)],
            [new Parameter("TmParam", "TmType")],
            ContainerSet: [new SequenceContainer("Frame", [])],
            MessageSet: new MessageSet([new Message("Msg", "Frame")]),
            AlgorithmSet: [new Algorithm("TmAlgo", AlgorithmKind.Custom)],
            StreamSet: [new StreamDefinition("TmStream", StreamKind.FixedFrame)]),
        CommandMetaData: new CommandMetaData(
            [new MetaCommand("Cmd")],
            ArgumentTypeSet: [new ParameterTypeDefinition("ArgType", ParameterTypeKind.Integer)],
            ParameterTypeSet: [new ParameterTypeDefinition("CmdType", ParameterTypeKind.Integer)],
            ParameterSet: [new Parameter("CmdParam", "CmdType")],
            AlgorithmSet: [new Algorithm("CmdAlgo", AlgorithmKind.Custom)],
            CommandContainerSet: [new CommandContainer("CmdFrame")],
            BlockMetaCommands: [new BlockMetaCommand("Block", [])]),
        ServiceSet: [new Service("Svc")]);

    private static readonly Dictionary<string, string> ExpectedNames = new()
    {
        ["parameterType"] = "TmType",
        ["parameter"] = "TmParam",
        ["container"] = "Frame",
        ["message"] = "Msg",
        ["algorithm"] = "TmAlgo",
        ["stream"] = "TmStream",
        ["service"] = "Svc",
        ["metaCommand"] = "Cmd",
        ["blockMetaCommand"] = "Block",
        ["argumentType"] = "ArgType",
        ["commandParameterType"] = "CmdType",
        ["commandParameter"] = "CmdParam",
        ["commandAlgorithm"] = "CmdAlgo",
        ["commandContainer"] = "CmdFrame",
    };

    [Test]
    public void EveryKind_ResolvesThroughItemsOfNameOfAndClrType()
    {
        var system = OneOfEverything();

        Assert.Equal(ExpectedNames.Keys.OrderBy(k => k), DocumentItems.Kinds.OrderBy(k => k));
        foreach (var kind in DocumentItems.Kinds)
        {
            var items = DocumentItems.ItemsOf(system, kind);
            Assert.NotNull(items);
            var item = Assert.Single(items!);
            Assert.Equal(ExpectedNames[kind], DocumentItems.NameOf(item));
            Assert.NotNull(DocumentItems.ClrType(kind));
            Assert.True(DocumentItems.ClrType(kind)!.IsInstanceOfType(item));
        }
    }

    [Test]
    public void EveryKind_RoundTripsThroughWithList()
    {
        var system = OneOfEverything();

        foreach (var kind in DocumentItems.Kinds)
        {
            var items = DocumentItems.ItemsOf(system, kind)!;
            var replaced = DocumentItems.WithList(system, kind, items.ToList());
            Assert.Equal(ExpectedNames[kind],
                DocumentItems.NameOf(Assert.Single(DocumentItems.ItemsOf(replaced, kind)!)));
            // Everything else survives the rebuild untouched.
            foreach (var otherKind in DocumentItems.Kinds.Where(k => k != kind))
            {
                Assert.Equal(ExpectedNames[otherKind],
                    DocumentItems.NameOf(Assert.Single(DocumentItems.ItemsOf(replaced, otherKind)!)));
            }
        }
    }

    [Test]
    public void WithList_OnBareSystem_CreatesTheContainingMetadata()
    {
        var bare = new SpaceSystem("Empty", []);

        foreach (var kind in DocumentItems.Kinds)
        {
            var source = OneOfEverything();
            var item = DocumentItems.ItemsOf(source, kind)![0];
            var populated = DocumentItems.WithList(bare, kind, [item]);
            Assert.Equal(ExpectedNames[kind],
                DocumentItems.NameOf(Assert.Single(DocumentItems.ItemsOf(populated, kind)!)));
        }
    }

    [Test]
    public void UnknownKind_AnswersNullEverywhere()
    {
        var system = OneOfEverything();

        Assert.Null(DocumentItems.ItemsOf(system, "mystery"));
        Assert.Null(DocumentItems.ClrType("mystery"));
        Assert.Equal(system, DocumentItems.WithList(system, "mystery", []));
        Assert.Equal("?", DocumentItems.NameOf(42));
    }

    [Test]
    public void Resolve_FollowsIndexPaths_AndRejectsBadOnes()
    {
        var root = new SpaceSystem("Sat", [new SpaceSystem("Bus", [new SpaceSystem("Eps", [])])]);

        Assert.Equal("Sat", DocumentItems.Resolve(root, "")!.Name);
        Assert.Equal("Sat", DocumentItems.Resolve(root, null)!.Name);
        Assert.Equal("Bus", DocumentItems.Resolve(root, "0")!.Name);
        Assert.Equal("Eps", DocumentItems.Resolve(root, "0/0")!.Name);
        Assert.Null(DocumentItems.Resolve(root, "1"));
        Assert.Null(DocumentItems.Resolve(root, "0/7"));
        Assert.Null(DocumentItems.Resolve(root, "bogus"));
    }

    [Test]
    public void UpdateAt_ReplacesOnlyTheAddressedSystem()
    {
        var root = new SpaceSystem("Sat",
        [
            new SpaceSystem("Bus", [new SpaceSystem("Eps", [])]),
            new SpaceSystem("Payload", []),
        ]);

        var updated = DocumentItems.UpdateAt(root, "0/0", node => node with { Name = "EpsRenamed" });

        Assert.Equal("EpsRenamed", DocumentItems.Resolve(updated, "0/0")!.Name);
        Assert.Equal("Payload", DocumentItems.Resolve(updated, "1")!.Name);
        Assert.Equal("Eps", DocumentItems.Resolve(root, "0/0")!.Name); // original untouched

        var rootRenamed = DocumentItems.UpdateAt(root, "", node => node with { Name = "SatRenamed" });
        Assert.Equal("SatRenamed", rootRenamed.Name);
    }
}
