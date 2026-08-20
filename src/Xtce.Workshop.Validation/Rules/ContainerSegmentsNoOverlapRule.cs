using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R04 (PARTIAL): container/parameter segments composing a whole must not overlap
/// in sequence (ContainerSegmentRefEntryType / ParameterSegmentRefEntryType). Segment
/// entries are unmodeled (Raw), so this inspects their fragments: within one EntryList,
/// segments referencing the same target with DUPLICATE explicit `order` values are flagged.
/// This is the conservative, no-false-positive reading — segments without `order` are
/// spec-defined as sequential-in-time, and true bit-level overlap analysis would need the
/// segment sizes resolved against the whole container layout.
/// </summary>
public sealed class ContainerSegmentsNoOverlapRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R04-container-segments-no-overlap";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var container in context.Node.TelemetryMetaData?.ContainerSet ?? [])
        {
            // (segment element name, target ref) -> order values seen so far
            var seenOrders = new Dictionary<(string Element, string Target), HashSet<string>>();

            foreach (var entry in container.EntryList)
            {
                if (entry.Kind != SequenceEntryKind.Raw || entry.RawXml is not { } fragment)
                {
                    continue;
                }
                if (fragment.ElementName is not ("ContainerSegmentRefEntry" or "ParameterSegmentRefEntry"))
                {
                    continue;
                }

                var refAttribute = fragment.ElementName == "ContainerSegmentRefEntry" ? "containerRef" : "parameterRef";
                var target = XmlFragmentInspector.RootAttribute(fragment.OuterXml, refAttribute);
                var order = XmlFragmentInspector.RootAttribute(fragment.OuterXml, "order");
                if (target is null || order is null)
                {
                    continue;
                }

                var key = (fragment.ElementName, target);
                if (!seenOrders.TryGetValue(key, out var orders))
                {
                    seenOrders[key] = orders = [];
                }

                if (!orders.Add(order))
                {
                    yield return new ValidationIssue(
                        RuleId,
                        Severity,
                        $"{context.Path}/ContainerSet/{container.Name}",
                        $"Multiple {fragment.ElementName} entries for '{target}' share order=\"{order}\" — segments composing a whole must not overlap in sequence.",
                        CandidateNumber: fragment.ElementName == "ContainerSegmentRefEntry" ? 10 : 13);
                }
            }
        }
    }
}
