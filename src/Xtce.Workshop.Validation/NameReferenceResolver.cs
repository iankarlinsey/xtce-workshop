using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

public enum ResolutionStatus
{
    /// <summary>No item of the requested kind is reachable via the reference.</summary>
    NotFound,

    /// <summary>The reference resolves to an item the object model represents.</summary>
    FoundModeled,

    /// <summary>
    /// The reference resolves to an item that exists but is only known through preserved
    /// (unmodeled) content — rules can trust its existence but not inspect it.
    /// </summary>
    FoundOpaque,
}

public sealed record ResolutionResult(ResolutionStatus Status, ParameterTypeDefinition? ParameterType = null)
{
    public bool Found => Status != ResolutionStatus.NotFound;
}

/// <summary>
/// Resolves XTCE name references (NameReferenceType, XSD line 5013) against a
/// SpaceSystemContext tree. The spec defines three forms: unqualified ("Voltage"),
/// relative ("Bus/Voltage", "../EPDS/Voltage", "." and ".." as segments), and absolute
/// (leading "/"). Multiple consecutive '/' are treated as one, per the spec.
///
/// Two documented lenience decisions, both to avoid false positives from an
/// error-severity dangling-reference rule:
/// - Unqualified and relative references FALL BACK TO ANCESTOR SpaceSystems when they
///   don't resolve at the point of use. The strict spec text scopes the unqualified form
///   to "the SpaceSystem the reference is used in", but major implementations (another implementation)
///   search enclosing systems and real files rely on it.
/// - Absolute references accept both interpretations of the first segment (it names the
///   root SpaceSystem, or paths start below the root) — the spec's own examples
///   ("SimpleSat/Bus/EPDS/BatteryOne/Voltage") are ambiguous about whether the root's
///   name appears in the path.
/// </summary>
public static class NameReferenceResolver
{
    public static ResolutionResult Resolve(SpaceSystemContext usageContext, string reference, NamedItemKind kind)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new ResolutionResult(ResolutionStatus.NotFound);
        }

        var isAbsolute = reference.StartsWith('/');
        var segments = reference.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return new ResolutionResult(ResolutionStatus.NotFound);
        }

        if (isAbsolute)
        {
            var root = usageContext.Root;

            // Interpretation A: the first segment names the root SpaceSystem itself.
            if (segments[0] == root.Node.Name &&
                ResolveFrom(root, segments.AsSpan(1), kind) is { Found: true } viaRootName)
            {
                return viaRootName;
            }

            // Interpretation B: segments start below the root.
            return ResolveFrom(root, segments, kind);
        }

        // Unqualified and relative forms: try at the point of use, then each ancestor.
        for (var scope = usageContext; scope is not null; scope = scope.Parent)
        {
            if (ResolveFrom(scope, segments, kind) is { Found: true } found)
            {
                return found;
            }
        }

        return new ResolutionResult(ResolutionStatus.NotFound);
    }

    private static ResolutionResult ResolveFrom(SpaceSystemContext start, ReadOnlySpan<string> segments, NamedItemKind kind)
    {
        if (segments.Length == 0)
        {
            return new ResolutionResult(ResolutionStatus.NotFound);
        }

        var system = start;
        foreach (var segment in segments[..^1])
        {
            if (segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                system = system.Parent;
                if (system is null)
                {
                    return new ResolutionResult(ResolutionStatus.NotFound);
                }
                continue;
            }
            if (!system.ChildrenByName.TryGetValue(segment, out var child))
            {
                return new ResolutionResult(ResolutionStatus.NotFound);
            }
            system = child;
        }

        var itemName = segments[^1];
        if (itemName is "." or "..")
        {
            return new ResolutionResult(ResolutionStatus.NotFound);
        }

        if (!system.NamesOf(kind).Contains(itemName))
        {
            return new ResolutionResult(ResolutionStatus.NotFound);
        }

        if (kind == NamedItemKind.ParameterType &&
            system.ModeledParameterTypes.TryGetValue(itemName, out var modeledType))
        {
            return new ResolutionResult(ResolutionStatus.FoundModeled, modeledType);
        }

        return new ResolutionResult(
            kind == NamedItemKind.ParameterType ? ResolutionStatus.FoundOpaque : ResolutionStatus.FoundModeled);
    }
}
