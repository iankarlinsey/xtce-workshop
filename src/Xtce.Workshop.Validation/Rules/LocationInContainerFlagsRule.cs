using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R08 (warning): per LocationInContainerInBitsType's own documentation (XSD line
/// 550), negative containerStart/containerEnd offsets "are implementation dependent — these
/// should be flagged as likely errors", and "the nextEntry attribute value is proposed for
/// deprecation and should be avoided". Locations are inspected wherever they're reachable
/// in a container's entries: preserved children of modeled ref entries, and descendants of
/// raw (unmodeled) entry fragments.
/// </summary>
public sealed class LocationInContainerFlagsRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R08-location-in-container-flags";
    public ValidationSeverity Severity => ValidationSeverity.Warning;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var (entries, location) in EntryLists(context))
        {
            foreach (var entry in entries)
            {
                // Modeled locations (#109) — the fixed shape carries the same flags.
                if (entry.Location is { } modeled)
                {
                    if (modeled.ReferenceLocation == "nextEntry")
                    {
                        yield return new ValidationIssue(RuleId, Severity, location,
                            "LocationInContainerInBits uses referenceLocation=\"nextEntry\", which is proposed for deprecation and should be avoided.",
                            CandidateNumber: 12);
                    }
                    else if (modeled.ReferenceLocation is "containerStart" or "containerEnd" && modeled.FixedValue < 0)
                    {
                        yield return new ValidationIssue(RuleId, Severity, location,
                            $"LocationInContainerInBits has a negative {modeled.ReferenceLocation} offset ({modeled.FixedValue}) — implementation dependent and a likely error.",
                            CandidateNumber: 12);
                    }
                }
                var fragments = entry.Kind == SequenceEntryKind.Raw
                    ? (entry.RawXml is { } raw ? [raw] : Array.Empty<RawXmlFragment>())
                    : (entry.Preserved ?? (IReadOnlyList<RawXmlFragment>)[]);

                foreach (var fragment in fragments)
                {
                    foreach (var info in XmlFragmentInspector.FindLocations(fragment.OuterXml))
                    {
                        if (info.ReferenceLocation == "nextEntry")
                        {
                            yield return new ValidationIssue(RuleId, Severity, location,
                                "LocationInContainerInBits uses referenceLocation=\"nextEntry\", which is proposed for deprecation and should be avoided.",
                                CandidateNumber: 12);
                        }
                        else if (info.ReferenceLocation is "containerStart" or "containerEnd" && info.FixedValue < 0)
                        {
                            yield return new ValidationIssue(RuleId, Severity, location,
                                $"LocationInContainerInBits has a negative {info.ReferenceLocation} offset ({info.FixedValue}) — implementation dependent and a likely error.",
                                CandidateNumber: 12);
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<(IReadOnlyList<SequenceEntry> Entries, string Location)> EntryLists(SpaceSystemContext context)
    {
        foreach (var container in context.Node.TelemetryMetaData?.ContainerSet ?? [])
        {
            yield return (container.EntryList, $"{context.Path}/ContainerSet/{container.Name}");
        }
        foreach (var metaCommand in context.Node.CommandMetaData?.MetaCommands ?? [])
        {
            if (metaCommand.CommandContainer?.EntryList is { } entryList)
            {
                yield return (entryList,
                    $"{context.Path}/CommandMetaData/MetaCommandSet/{metaCommand.Name}/CommandContainer");
            }
        }
    }
}
