using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

public class NameReferenceResolverTests
{
    // Root(SAT) defines type RootType, parameter RootParam, container RootFrame.
    // SAT/Bus defines BusParam; SAT/Bus/Eps defines EpsParam; SAT/Payload defines PayParam.
    private static SpaceSystemContext BuildTree()
    {
        var eps = new SpaceSystem("Eps", [], new TelemetryMetaData(
            [], [new Parameter("EpsParam", "RootType")]));
        var bus = new SpaceSystem("Bus", [eps], new TelemetryMetaData(
            [], [new Parameter("BusParam", "RootType")]));
        var payload = new SpaceSystem("Payload", [], new TelemetryMetaData(
            [], [new Parameter("PayParam", "RootType")]));
        var root = new SpaceSystem("SAT", [bus, payload], new TelemetryMetaData(
            [new ParameterTypeDefinition("RootType", ParameterTypeKind.Integer)],
            [new Parameter("RootParam", "RootType")],
            ContainerSet: [new SequenceContainer("RootFrame", [])]));
        return SpaceSystemContext.Build(root);
    }

    private static SpaceSystemContext At(SpaceSystemContext root, params string[] path)
    {
        var context = root;
        foreach (var segment in path)
        {
            context = context.ChildrenByName[segment];
        }
        return context;
    }

    [Test]
    public void Unqualified_ResolvesInTheSameSpaceSystem()
    {
        var root = BuildTree();
        var eps = At(root, "Bus", "Eps");

        Assert.True(NameReferenceResolver.Resolve(eps, "EpsParam", NamedItemKind.Parameter).Found);
    }

    [Test]
    public void Unqualified_FallsBackToAncestors()
    {
        var root = BuildTree();
        var eps = At(root, "Bus", "Eps");

        Assert.True(NameReferenceResolver.Resolve(eps, "BusParam", NamedItemKind.Parameter).Found);
        Assert.True(NameReferenceResolver.Resolve(eps, "RootParam", NamedItemKind.Parameter).Found);
    }

    [Test]
    public void Unqualified_DoesNotSearchSiblingsOrDescendants()
    {
        var root = BuildTree();
        var bus = At(root, "Bus");

        // PayParam lives in the sibling Payload system; EpsParam in Bus's own child.
        Assert.False(NameReferenceResolver.Resolve(bus, "PayParam", NamedItemKind.Parameter).Found);
        Assert.False(NameReferenceResolver.Resolve(bus, "EpsParam", NamedItemKind.Parameter).Found);
    }

    [Test]
    public void Relative_WalksChildPath()
    {
        var root = BuildTree();

        Assert.True(NameReferenceResolver.Resolve(root, "Bus/Eps/EpsParam", NamedItemKind.Parameter).Found);
    }

    [Test]
    public void Relative_SupportsDotDotAndDotSegments()
    {
        var root = BuildTree();
        var eps = At(root, "Bus", "Eps");

        Assert.True(NameReferenceResolver.Resolve(eps, "../../Payload/PayParam", NamedItemKind.Parameter).Found);
        Assert.True(NameReferenceResolver.Resolve(eps, "./EpsParam", NamedItemKind.Parameter).Found);
    }

    [Test]
    public void Relative_FallsBackToAncestorScopes()
    {
        var root = BuildTree();
        var eps = At(root, "Bus", "Eps");

        // "Payload/PayParam" doesn't resolve from Eps or Bus, but does from SAT.
        Assert.True(NameReferenceResolver.Resolve(eps, "Payload/PayParam", NamedItemKind.Parameter).Found);
    }

    [Test]
    public void Absolute_AcceptsBothRootInterpretations()
    {
        var root = BuildTree();
        var eps = At(root, "Bus", "Eps");

        // First segment names the root SpaceSystem...
        Assert.True(NameReferenceResolver.Resolve(eps, "/SAT/Bus/BusParam", NamedItemKind.Parameter).Found);
        // ...or segments start below the root.
        Assert.True(NameReferenceResolver.Resolve(eps, "/Bus/BusParam", NamedItemKind.Parameter).Found);
    }

    [Test]
    public void Absolute_CollapsesRepeatedSlashes()
    {
        var root = BuildTree();

        Assert.True(NameReferenceResolver.Resolve(root, "//Bus//Eps//EpsParam", NamedItemKind.Parameter).Found);
    }

    [Test]
    public void NamespacesAreSeparate()
    {
        var root = BuildTree();

        Assert.True(NameReferenceResolver.Resolve(root, "RootFrame", NamedItemKind.Container).Found);
        Assert.False(NameReferenceResolver.Resolve(root, "RootFrame", NamedItemKind.Parameter).Found);
        Assert.False(NameReferenceResolver.Resolve(root, "RootParam", NamedItemKind.Container).Found);
    }

    [Test]
    public void ParameterType_ResolvesToTheModeledDefinition()
    {
        var root = BuildTree();
        var eps = At(root, "Bus", "Eps");

        var result = NameReferenceResolver.Resolve(eps, "RootType", NamedItemKind.ParameterType);

        Assert.Equal(ResolutionStatus.FoundModeled, result.Status);
        Assert.Equal("RootType", result.ParameterType!.Name);
    }

    [Test]
    public void ParameterType_PreservedFragmentResolvesAsOpaque()
    {
        var telemetry = new TelemetryMetaData(
            [], [],
            PreservedParameterTypes:
            [
                new RawXmlFragment("BinaryParameterType",
                    """<BinaryParameterType name="Blob_Type" xmlns="http://www.omg.org/spec/XTCE/20180204"/>"""),
            ]);
        var context = SpaceSystemContext.Build(new SpaceSystem("S", [], telemetry));

        var result = NameReferenceResolver.Resolve(context, "Blob_Type", NamedItemKind.ParameterType);

        Assert.Equal(ResolutionStatus.FoundOpaque, result.Status);
        Assert.Null(result.ParameterType);
    }

    [Test]
    public void PreservedCommandMetaData_ContributesItsDefinitionsToTheNamespaces()
    {
        var commandMetaData = """
            <CommandMetaData xmlns="http://www.omg.org/spec/XTCE/20180204">
              <ParameterTypeSet>
                <IntegerParameterType name="CmdCounterType"/>
              </ParameterTypeSet>
              <ParameterSet>
                <Parameter name="CmdCounter" parameterTypeRef="CmdCounterType"/>
              </ParameterSet>
              <MetaCommandSet>
                <MetaCommand name="Reboot">
                  <ArgumentList>
                    <Argument name="NotAParameter" argumentTypeRef="X"/>
                  </ArgumentList>
                </MetaCommand>
              </MetaCommandSet>
              <CommandContainerSet>
                <CommandContainer name="CmdFrame">
                  <EntryList/>
                </CommandContainer>
              </CommandContainerSet>
            </CommandMetaData>
            """;
        var spaceSystem = new SpaceSystem("S", [],
            Preserved: [new RawXmlFragment("CommandMetaData", commandMetaData)]);
        var context = SpaceSystemContext.Build(spaceSystem);

        Assert.True(NameReferenceResolver.Resolve(context, "CmdCounter", NamedItemKind.Parameter).Found);
        Assert.True(NameReferenceResolver.Resolve(context, "CmdCounterType", NamedItemKind.ParameterType).Found);
        Assert.True(NameReferenceResolver.Resolve(context, "CmdFrame", NamedItemKind.Container).Found);
        // A MetaCommand argument name is a different namespace and must NOT leak in.
        Assert.False(NameReferenceResolver.Resolve(context, "NotAParameter", NamedItemKind.Parameter).Found);
    }

    [Test]
    public void Dangling_ReturnsNotFound()
    {
        var root = BuildTree();

        Assert.False(NameReferenceResolver.Resolve(root, "NoSuchThing", NamedItemKind.Parameter).Found);
        Assert.False(NameReferenceResolver.Resolve(root, "Bus/NoSuchThing", NamedItemKind.Parameter).Found);
        Assert.False(NameReferenceResolver.Resolve(root, "/SAT/Nowhere/Item", NamedItemKind.Parameter).Found);
        Assert.False(NameReferenceResolver.Resolve(root, "../TooFarUp", NamedItemKind.Parameter).Found);
    }
}
