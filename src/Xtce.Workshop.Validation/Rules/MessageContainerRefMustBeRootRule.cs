using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R09 (PARTIAL — a heuristic that only flags demonstrable non-roots): a
/// Message's ContainerRef "should point to ROOT container that will describe an entire
/// packet/minor frame or chunk of telemetry" (MessageType/ContainerRef, XSD line 736).
/// Flagged: a ref that doesn't resolve at all (this site belongs to R09, not R11, to
/// avoid double reporting), one that resolves to an abstract container (not instantiable,
/// so it can't describe a whole packet), or one that resolves to a container some
/// EntryList includes as a sub-piece (ContainerRefEntry / ContainerSegmentRefEntry
/// target). Proving general rootness isn't attempted.
///
/// Runs once per document (at the root context) because the sub-piece set needs every
/// container's entries, resolved from each entry's own scope.
/// </summary>
public sealed class MessageContainerRefMustBeRootRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R09-messagetype-containerref-must-be-root";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        if (context.Parent is not null)
        {
            yield break;
        }

        var subPieces = CollectSubPieceContainers(context);

        foreach (var system in context.SelfAndDescendants())
        {
            foreach (var message in system.Node.TelemetryMetaData?.MessageSet?.Messages ?? [])
            {
                var location = $"{system.Path}/MessageSet/{message.Name}";
                var resolution = NameReferenceResolver.Resolve(system, message.ContainerRef, NamedItemKind.Container);

                if (!resolution.Found)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        $"ContainerRef '{message.ContainerRef}' does not resolve to any container.");
                }
                else if (resolution.Container is { } container)
                {
                    if (container.Abstract == true)
                    {
                        yield return new ValidationIssue(RuleId, Severity, location,
                            $"ContainerRef '{message.ContainerRef}' targets an abstract container — not instantiable, so it cannot describe a whole packet.");
                    }
                    else if (subPieces.Contains(container))
                    {
                        yield return new ValidationIssue(RuleId, Severity, location,
                            $"ContainerRef '{message.ContainerRef}' targets a container that other containers include as a sub-piece — a Message must reference a root-level container.");
                    }
                }
            }
        }
    }

    private static HashSet<SequenceContainer> CollectSubPieceContainers(SpaceSystemContext root)
    {
        var subPieces = new HashSet<SequenceContainer>(ReferenceEqualityComparer.Instance);

        foreach (var system in root.SelfAndDescendants())
        {
            foreach (var container in system.Node.TelemetryMetaData?.ContainerSet ?? [])
            {
                foreach (var entry in container.EntryList)
                {
                    string? target = entry.Kind switch
                    {
                        SequenceEntryKind.ContainerRef => entry.Ref,
                        SequenceEntryKind.Raw when entry.RawXml?.ElementName == "ContainerSegmentRefEntry" =>
                            XmlFragmentInspector.RootAttribute(entry.RawXml.OuterXml, "containerRef"),
                        _ => null,
                    };

                    if (target is not null &&
                        NameReferenceResolver.Resolve(system, target, NamedItemKind.Container).Container is { } piece)
                    {
                        subPieces.Add(piece);
                    }
                }
            }
        }

        return subPieces;
    }
}
