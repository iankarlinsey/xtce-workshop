using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Model-backed argument access — the replacement for ArgumentScanner's argument duties
/// now that ArgumentTypeSet, ArgumentList, and ArgumentAssignmentList are first-class.
/// </summary>
public static class ModeledArguments
{
    /// <summary>An argument paired with the SpaceSystem scope its declaring command lives in.</summary>
    public sealed record Scoped(Argument Decl, SpaceSystemContext Scope);

    /// <summary>
    /// All arguments visible on a MetaCommand — its own plus those inherited along the
    /// BaseMetaCommand chain (cycle-guarded).
    /// </summary>
    public static IReadOnlyList<Scoped> Merged(SpaceSystemContext usageContext, MetaCommand metaCommand)
    {
        var merged = new List<Scoped>();
        var visited = new HashSet<MetaCommand>(ReferenceEqualityComparer.Instance);
        var current = metaCommand;
        var scope = usageContext;

        while (current is not null && visited.Add(current))
        {
            merged.AddRange((current.Arguments ?? []).Select(a => new Scoped(a, scope)));
            if (current.BaseMetaCommandRef is not { } baseRef)
            {
                break;
            }
            var resolution = NameReferenceResolver.Resolve(scope, baseRef, NamedItemKind.MetaCommand);
            current = resolution.MetaCommand;
            scope = resolution.DefinedIn ?? scope;
        }
        return merged;
    }

    /// <summary>Resolves an argumentTypeRef from the given scope (self, ancestors, or path).</summary>
    public static ParameterTypeDefinition? ResolveType(SpaceSystemContext scope, string typeRef) =>
        NameReferenceResolver.Resolve(scope, typeRef, NamedItemKind.ArgumentType).ParameterType;
}
