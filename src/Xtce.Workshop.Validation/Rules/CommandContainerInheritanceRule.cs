namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R21 (PARTIAL, warning): MetaCommand/CommandContainer inheritance wiring
/// (CCSDS 660.1-G-2 4.4.5.2.4.6/4.4.5.2.4.7, 5.6.2). Two statically clean directions:
/// - inheritance-without-wiring: a MetaCommand extending a modeled parent that HAS a
///   CommandContainer, whose own CommandContainer lacks a BaseContainer — "the
///   MetaCommand/CommandContainer/BaseContainer must be supplied in order for the child
///   command to inherit the EntryList of the parent", so the parent's entries will
///   silently not be inherited;
/// - wiring-without-inheritance: a MetaCommand with NO BaseMetaCommand whose
///   CommandContainer's BaseContainer references another MetaCommand's INLINE
///   CommandContainer — "It should not be included otherwise". References to
///   CommandContainerSet containers or telemetry SequenceContainers stay legal (headers).
/// Both statements are conditional-intent / "should" language, so this rule is a WARNING
/// (severity refined from the provisional matrix row). Wiring-without-inheritance only
/// sees unqualified refs to inline containers in the current-or-ancestor scope — the
/// recorded partial gap alongside opaque parents.
/// </summary>
public sealed class CommandContainerInheritanceRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R21-metacommand-commandcontainer-inheritance-requires-basecontainer";
    public ValidationSeverity Severity => ValidationSeverity.Warning;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var metaCommand in context.Node.CommandMetaData?.MetaCommands ?? [])
        {
            if (metaCommand.CommandContainer is not { } container)
            {
                continue;
            }
            var location = $"{context.Path}/CommandMetaData/MetaCommandSet/{metaCommand.Name}";

            if (metaCommand.BaseMetaCommandRef is { } baseRef)
            {
                var parent = NameReferenceResolver.Resolve(context, baseRef, NamedItemKind.MetaCommand).MetaCommand;
                if (parent?.CommandContainer is not null && container.BaseContainerRef is null)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        $"'{metaCommand.Name}' extends '{parent.Name}', which has a CommandContainer, but its own CommandContainer has no BaseContainer — the parent's EntryList will not be inherited.");
                }
            }
            else if (container.BaseContainerRef is { } wiredRef)
            {
                var lastSegment = wiredRef[(wiredRef.LastIndexOf('/') + 1)..];
                for (var scope = context; scope is not null; scope = scope.Parent)
                {
                    if (scope.InlineCommandContainerOwners.TryGetValue(lastSegment, out var owner))
                    {
                        yield return new ValidationIssue(RuleId, Severity, location,
                            $"'{metaCommand.Name}' does not extend any MetaCommand, but its CommandContainer's BaseContainer references '{owner.Name}''s inline CommandContainer — it should not be included outside MetaCommand inheritance.");
                        break;
                    }
                }
            }
        }
    }
}
