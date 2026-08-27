using System.Text;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>
/// The modeled command-argument surface (#95): ArgumentTypeSet, ArgumentList, and
/// ArgumentAssignmentList parsed by the reader, resolved through the ArgumentType name
/// namespace, and merged along the BaseMetaCommand chain.
/// </summary>
public class ModeledArgumentsTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Test]
    public void Merged_WalksTheBaseMetaCommandChain_ParentTypesResolveFromParentScope()
    {
        var document = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <ArgumentTypeSet>
                  <IntegerArgumentType name="U8" signed="false" sizeInBits="8"/>
                </ArgumentTypeSet>
                <MetaCommandSet>
                  <MetaCommand name="Base" abstract="true">
                    <ArgumentList><Argument name="A" argumentTypeRef="U8"/></ArgumentList>
                  </MetaCommand>
                  <MetaCommand name="Child">
                    <BaseMetaCommand metaCommandRef="Base"/>
                    <ArgumentList><Argument name="B" argumentTypeRef="U8" initialValue="1"/></ArgumentList>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);
        var context = SpaceSystemContext.Build(document);
        var child = context.ModeledMetaCommands["Child"];

        var merged = ModeledArguments.Merged(context, child);

        Assert.Equal(["B", "A"], merged.Select(a => a.Decl.Name));
        Assert.Equal("1", merged[0].Decl.InitialValue);
        Assert.All(merged, a => Assert.NotNull(ModeledArguments.ResolveType(a.Scope, a.Decl.ArgumentTypeRef)));
    }

    [Test]
    public void Merged_SelfReferentialBaseChain_Terminates()
    {
        var document = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <MetaCommandSet>
                  <MetaCommand name="Loop">
                    <BaseMetaCommand metaCommandRef="Loop"/>
                    <ArgumentList><Argument name="A" argumentTypeRef="U8"/></ArgumentList>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);
        var context = SpaceSystemContext.Build(document);

        var merged = ModeledArguments.Merged(context, context.ModeledMetaCommands["Loop"]);

        Assert.Equal(["A"], merged.Select(a => a.Decl.Name));
    }

    [Test]
    public void ResolveType_FallsBackToAncestors_AndResolvesPathQualifiedRefs()
    {
        var document = Load($"""
            <SpaceSystem xmlns="{Ns}" name="Root">
              <CommandMetaData>
                <ArgumentTypeSet><IntegerArgumentType name="U8"/></ArgumentTypeSet>
              </CommandMetaData>
              <SpaceSystem name="Child"/>
            </SpaceSystem>
            """);
        var childContext = SpaceSystemContext.Build(document).ChildrenByName["Child"];

        Assert.NotNull(ModeledArguments.ResolveType(childContext, "U8"));
        // Path-qualified refs resolve through the shared resolver — the fragment-era
        // scanner returned null for these.
        Assert.NotNull(ModeledArguments.ResolveType(childContext, "/Root/U8"));
        Assert.Null(ModeledArguments.ResolveType(childContext, "NoSuchType"));
    }

    [Test]
    public void Reader_ParsesArgumentAssignments_OnBaseMetaCommand()
    {
        var document = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <MetaCommandSet>
                  <MetaCommand name="Cmd">
                    <BaseMetaCommand metaCommandRef="Base">
                      <ArgumentAssignmentList>
                        <ArgumentAssignment argumentName="A" argumentValue="42"/>
                      </ArgumentAssignmentList>
                    </BaseMetaCommand>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);

        var metaCommand = document.CommandMetaData!.MetaCommands.Single();
        var assignment = Assert.Single(metaCommand.ArgumentAssignments ?? []);
        Assert.Equal(("A", "42"), (assignment.ArgumentName, assignment.ArgumentValue));
    }

    [Test]
    public void Reader_AcceptsBothRelativeTimeSpellings_WriterEmitsTheSchemaTypo()
    {
        // The XTCE 1.2 XSD's element is literally "RelativeTimeAgumentType" (sic). The
        // reader takes both spellings; the writer must emit the typo to stay schema-valid.
        var document = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <ArgumentTypeSet>
                  <RelativeTimeAgumentType name="RT1"/>
                  <RelativeTimeArgumentType name="RT2"/>
                </ArgumentTypeSet>
              </CommandMetaData>
            </SpaceSystem>
            """);

        var types = document.CommandMetaData!.ArgumentTypeSet ?? [];
        Assert.Equal([ParameterTypeKind.RelativeTime, ParameterTypeKind.RelativeTime], types.Select(t => t.Kind));

        var written = XtceDocumentWriter.Write(document);
        Assert.Contains("<RelativeTimeAgumentType", written);
        Assert.DoesNotContain("<RelativeTimeArgumentType", written);
    }
}
