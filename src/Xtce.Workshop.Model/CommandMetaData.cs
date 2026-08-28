namespace Xtce.Workshop.Model;

/// <summary>
/// A MetaCommand's inline CommandContainer: name, the BaseContainer reference
/// (inheritance wiring), and — since issue #97 — the EntryList with the command-side
/// entry kinds modeled (ArgumentRefEntry, FixedValueEntry, plus the shared
/// ParameterRefEntry/ContainerRefEntry; the rest ride as Raw entries in position).
/// EntryList is null only for a container that had none in the source (schema-invalid
/// but preserved as-was); an empty element is an empty, non-null list.
/// </summary>
public sealed record CommandContainer(
    string Name,
    string? BaseContainerRef = null,
    IReadOnlyList<RawXmlFragment>? BaseContainerPreserved = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    IReadOnlyList<SequenceEntry>? EntryList = null,
    Description? Description = null)
{
    public bool Equals(CommandContainer? other) =>
        other is not null
        && Name == other.Name
        && BaseContainerRef == other.BaseContainerRef
        && Structural.ListEquals(BaseContainerPreserved, other.BaseContainerPreserved)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Structural.ListEquals(EntryList, other.EntryList)
        && Equals(Description, other.Description);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(BaseContainerRef);
        Structural.AddList(ref hash, BaseContainerPreserved);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        Structural.AddList(ref hash, EntryList);
        hash.Add(Description);
        return hash.ToHashCode();
    }
}

/// <summary>
/// An Argument declared on a MetaCommand's ArgumentList: name, type reference, optional
/// initial value; unmodeled children (LongDescription, AliasSet, ...) preserved.
/// </summary>
public sealed record Argument(
    string Name,
    string ArgumentTypeRef,
    string? InitialValue = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    Description? Description = null)
{
    public bool Equals(Argument? other) =>
        other is not null
        && Name == other.Name
        && ArgumentTypeRef == other.ArgumentTypeRef
        && InitialValue == other.InitialValue
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(Description, other.Description);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(ArgumentTypeRef);
        hash.Add(InitialValue);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(Description);
        return hash.ToHashCode();
    }
}

/// <summary>One ArgumentAssignment inside a BaseMetaCommand's ArgumentAssignmentList.</summary>
public sealed record ArgumentAssignment(string ArgumentName, string ArgumentValue);

/// <summary>
/// One MetaCommandStepList entry inside a BlockMetaCommand (issue #116). The XSD's
/// step-level list element is literally "ArgumentAssigmentList" (sic — missing n); the
/// reader accepts both spellings and the writer re-emits the typo for schema validity.
/// </summary>
public sealed record MetaCommandStep(
    string MetaCommandRef,
    IReadOnlyList<ArgumentAssignment>? ArgumentAssignments = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(MetaCommandStep? other) =>
        other is not null
        && MetaCommandRef == other.MetaCommandRef
        && Structural.ListEquals(ArgumentAssignments, other.ArgumentAssignments)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MetaCommandRef);
        Structural.AddList(ref hash, ArgumentAssignments);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>A BlockMetaCommand: a named sequence of MetaCommand steps (issue #116).</summary>
public sealed record BlockMetaCommand(
    string Name,
    IReadOnlyList<MetaCommandStep>? Steps = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    Description? Description = null)
{
    public bool Equals(BlockMetaCommand? other) =>
        other is not null
        && Name == other.Name
        && Structural.ListEquals(Steps, other.Steps)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(Description, other.Description);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        Structural.AddList(ref hash, Steps);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(Description);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A MetaCommand in a CommandMetaData's MetaCommandSet — modeled just deeply enough for
/// verifier-inheritance validation (rule R12): name, abstract flag, the BaseMetaCommand
/// reference, and the verifier lists. ExecutionVerifiers/CompleteVerifiers are kept as raw
/// fragments because their identity for duplicate detection is (whitespace-normalized) XML
/// equality — modeling their internals buys nothing for that. PreservedVerifiers carries
/// the six 0..1 verifier kinds; BaseMetaCommandPreserved carries BaseMetaCommand's optional
/// ArgumentAssignmentList; Preserved carries every other MetaCommand child (ArgumentList,
/// CommandContainer, constraints, significance, interlock, ParameterToSetList, ...).
/// </summary>
/// <summary>
/// One VerifierSet entry (issue #106). Kind is the element name ("CompleteVerifier",
/// "ExecutionVerifier", or one of the six 0..1 kinds). The check choice is modeled for
/// its Comparison/ComparisonList/ContainerRef forms (reusing the shared Comparison
/// record — the XSD uses plain ComparisonType here); BooleanExpression, CustomAlgorithm,
/// ParameterValueChange, and CheckWindowAlgorithms ride in Preserved. CheckWindow's
/// attributes are modeled verbatim when present.
/// </summary>
public sealed record CommandVerifier(
    string Kind,
    Comparison? Comparison = null,
    IReadOnlyList<Comparison>? ComparisonList = null,
    string? ContainerRef = null,
    bool HasCheckWindow = false,
    string? TimeToStartChecking = null,
    string? TimeToStopChecking = null,
    string? TimeWindowIsRelativeTo = null,
    IReadOnlyList<RawAttribute>? CheckWindowPreservedAttributes = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    RawXmlFragment? RawXml = null,
    Description? Description = null)
{
    public bool Equals(CommandVerifier? other) =>
        other is not null
        && Kind == other.Kind
        && Equals(Comparison, other.Comparison)
        && Structural.ListEquals(ComparisonList, other.ComparisonList)
        && ContainerRef == other.ContainerRef
        && HasCheckWindow == other.HasCheckWindow
        && TimeToStartChecking == other.TimeToStartChecking
        && TimeToStopChecking == other.TimeToStopChecking
        && TimeWindowIsRelativeTo == other.TimeWindowIsRelativeTo
        && Structural.ListEquals(CheckWindowPreservedAttributes, other.CheckWindowPreservedAttributes)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(RawXml, other.RawXml)
        && Equals(Description, other.Description);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Comparison);
        Structural.AddList(ref hash, ComparisonList);
        hash.Add(ContainerRef);
        hash.Add(HasCheckWindow);
        hash.Add(TimeToStartChecking);
        hash.Add(TimeToStopChecking);
        hash.Add(TimeWindowIsRelativeTo);
        Structural.AddList(ref hash, CheckWindowPreservedAttributes);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(RawXml);
        hash.Add(Description);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One TransmissionConstraintList entry (issue #107): timeOut/suspendable attributes and
/// the match-criteria choice's Comparison/ComparisonList forms modeled (plain
/// MatchCriteriaType — the shared Comparison record); BooleanExpression/CustomAlgorithm
/// ride in Preserved. A foreign element in constraint position rides whole as RawXml.
/// </summary>
public sealed record TransmissionConstraint(
    string? TimeOut = null,
    bool? Suspendable = null,
    Comparison? Comparison = null,
    IReadOnlyList<Comparison>? ComparisonList = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    RawXmlFragment? RawXml = null)
{
    public bool Equals(TransmissionConstraint? other) =>
        other is not null
        && TimeOut == other.TimeOut
        && Suspendable == other.Suspendable
        && Equals(Comparison, other.Comparison)
        && Structural.ListEquals(ComparisonList, other.ComparisonList)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(RawXml, other.RawXml);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TimeOut);
        hash.Add(Suspendable);
        hash.Add(Comparison);
        Structural.AddList(ref hash, ComparisonList);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(RawXml);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One ParameterToSetList entry (issue #107): parameterRef/setOnVerification attributes
/// and the literal NewValue modeled; Derivation (and an unmodelable NewValue) ride in
/// Preserved. A foreign element in list position rides whole as RawXml.
/// </summary>
public sealed record ParameterToSet(
    string? ParameterRef = null,
    string? NewValue = null,
    string? SetOnVerification = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    RawXmlFragment? RawXml = null)
{
    public bool Equals(ParameterToSet? other) =>
        other is not null
        && ParameterRef == other.ParameterRef
        && NewValue == other.NewValue
        && SetOnVerification == other.SetOnVerification
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(RawXml, other.RawXml);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ParameterRef);
        hash.Add(NewValue);
        hash.Add(SetOnVerification);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(RawXml);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One ContextSignificanceList entry (issue #121): a ContextMatch plus the Significance
/// it puts in effect. First matching context overrides DefaultSignificance, so entries
/// stay IN POSITION; an entry the model can't represent rides whole as RawXml.
/// </summary>
public sealed record ContextSignificance(
    MatchCriteria? Context = null,
    Significance? Significance = null,
    RawXmlFragment? RawXml = null);

/// <summary>A MetaCommand's DefaultSignificance (issue #112) — attributes verbatim.</summary>
public sealed record Significance(
    string? SpaceSystemAtRisk = null,
    string? ReasonForWarning = null,
    string? ConsequenceLevel = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(Significance? other) =>
        other is not null
        && SpaceSystemAtRisk == other.SpaceSystemAtRisk
        && ReasonForWarning == other.ReasonForWarning
        && ConsequenceLevel == other.ConsequenceLevel
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SpaceSystemAtRisk);
        hash.Add(ReasonForWarning);
        hash.Add(ConsequenceLevel);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>A MetaCommand's Interlock (issue #112) — attributes verbatim.</summary>
public sealed record Interlock(
    string? ScopeToSpaceSystem = null,
    string? VerificationToWaitFor = null,
    string? VerificationProgressPercentage = null,
    bool? Suspendable = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(Interlock? other) =>
        other is not null
        && ScopeToSpaceSystem == other.ScopeToSpaceSystem
        && VerificationToWaitFor == other.VerificationToWaitFor
        && VerificationProgressPercentage == other.VerificationProgressPercentage
        && Suspendable == other.Suspendable
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ScopeToSpaceSystem);
        hash.Add(VerificationToWaitFor);
        hash.Add(VerificationProgressPercentage);
        hash.Add(Suspendable);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

public sealed record MetaCommand(
    string Name,
    bool? Abstract = null,
    string? BaseMetaCommandRef = null,
    IReadOnlyList<RawXmlFragment>? BaseMetaCommandPreserved = null,
    IReadOnlyList<CommandVerifier>? Verifiers = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    CommandContainer? CommandContainer = null,
    IReadOnlyList<Argument>? Arguments = null,
    IReadOnlyList<RawXmlFragment>? PreservedArguments = null,
    IReadOnlyList<ArgumentAssignment>? ArgumentAssignments = null,
    IReadOnlyList<TransmissionConstraint>? TransmissionConstraints = null,
    IReadOnlyList<ParameterToSet>? ParameterToSets = null,
    Significance? DefaultSignificance = null,
    Interlock? Interlock = null,
    Description? Description = null,
    IReadOnlyList<ContextSignificance>? ContextSignificances = null)
{
    public bool Equals(MetaCommand? other) =>
        other is not null
        && Name == other.Name
        && Abstract == other.Abstract
        && BaseMetaCommandRef == other.BaseMetaCommandRef
        && Structural.ListEquals(BaseMetaCommandPreserved, other.BaseMetaCommandPreserved)
        && Structural.ListEquals(Verifiers, other.Verifiers)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(CommandContainer, other.CommandContainer)
        && Structural.ListEquals(Arguments, other.Arguments)
        && Structural.ListEquals(PreservedArguments, other.PreservedArguments)
        && Structural.ListEquals(ArgumentAssignments, other.ArgumentAssignments)
        && Structural.ListEquals(TransmissionConstraints, other.TransmissionConstraints)
        && Structural.ListEquals(ParameterToSets, other.ParameterToSets)
        && Equals(DefaultSignificance, other.DefaultSignificance)
        && Equals(Interlock, other.Interlock)
        && Equals(Description, other.Description)
        && Structural.ListEquals(ContextSignificances, other.ContextSignificances);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Abstract);
        hash.Add(BaseMetaCommandRef);
        Structural.AddList(ref hash, BaseMetaCommandPreserved);
        Structural.AddList(ref hash, Verifiers);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(CommandContainer);
        Structural.AddList(ref hash, Arguments);
        Structural.AddList(ref hash, PreservedArguments);
        Structural.AddList(ref hash, ArgumentAssignments);
        Structural.AddList(ref hash, TransmissionConstraints);
        Structural.AddList(ref hash, ParameterToSets);
        hash.Add(DefaultSignificance);
        hash.Add(Interlock);
        hash.Add(Description);
        Structural.AddList(ref hash, ContextSignificances);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The CommandMetaData element of a SpaceSystem, modeled for MetaCommandSet access.
/// PreservedEntries holds MetaCommandSet's non-MetaCommand entries (MetaCommandRef,
/// BlockMetaCommand); Preserved holds CommandMetaData's other children as whole fragments
/// (ParameterTypeSet, ParameterSet, ArgumentTypeSet, CommandContainerSet, StreamSet,
/// AlgorithmSet), re-emitted in XSD sequence order. Definitions inside those fragments
/// still contribute to the reference namespaces via scanning — see SpaceSystemContext.
/// </summary>
public sealed record CommandMetaData(
    IReadOnlyList<MetaCommand> MetaCommands,
    IReadOnlyList<RawXmlFragment>? PreservedEntries = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<ParameterTypeDefinition>? ArgumentTypeSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedArgumentTypes = null,
    IReadOnlyList<ParameterTypeDefinition>? ParameterTypeSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedParameterTypes = null,
    IReadOnlyList<Parameter>? ParameterSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedParameters = null,
    IReadOnlyList<Algorithm>? AlgorithmSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedAlgorithms = null,
    IReadOnlyList<CommandContainer>? CommandContainerSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedCommandContainers = null,
    IReadOnlyList<StreamDefinition>? StreamSet = null,
    IReadOnlyList<BlockMetaCommand>? BlockMetaCommands = null,
    IReadOnlyList<string>? MetaCommandRefs = null)
{
    public bool Equals(CommandMetaData? other) =>
        other is not null
        && MetaCommands.SequenceEqual(other.MetaCommands)
        && Structural.ListEquals(PreservedEntries, other.PreservedEntries)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(ArgumentTypeSet, other.ArgumentTypeSet)
        && Structural.ListEquals(PreservedArgumentTypes, other.PreservedArgumentTypes)
        && Structural.ListEquals(ParameterTypeSet, other.ParameterTypeSet)
        && Structural.ListEquals(PreservedParameterTypes, other.PreservedParameterTypes)
        && Structural.ListEquals(ParameterSet, other.ParameterSet)
        && Structural.ListEquals(PreservedParameters, other.PreservedParameters)
        && Structural.ListEquals(AlgorithmSet, other.AlgorithmSet)
        && Structural.ListEquals(PreservedAlgorithms, other.PreservedAlgorithms)
        && Structural.ListEquals(CommandContainerSet, other.CommandContainerSet)
        && Structural.ListEquals(PreservedCommandContainers, other.PreservedCommandContainers)
        && Structural.ListEquals(StreamSet, other.StreamSet)
        && Structural.ListEquals(BlockMetaCommands, other.BlockMetaCommands)
        && Structural.ListEquals(MetaCommandRefs, other.MetaCommandRefs);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var metaCommand in MetaCommands)
            hash.Add(metaCommand);
        Structural.AddList(ref hash, PreservedEntries);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, ArgumentTypeSet);
        Structural.AddList(ref hash, PreservedArgumentTypes);
        Structural.AddList(ref hash, ParameterTypeSet);
        Structural.AddList(ref hash, PreservedParameterTypes);
        Structural.AddList(ref hash, ParameterSet);
        Structural.AddList(ref hash, PreservedParameters);
        Structural.AddList(ref hash, AlgorithmSet);
        Structural.AddList(ref hash, PreservedAlgorithms);
        Structural.AddList(ref hash, CommandContainerSet);
        Structural.AddList(ref hash, PreservedCommandContainers);
        Structural.AddList(ref hash, StreamSet);
        Structural.AddList(ref hash, BlockMetaCommands);
        Structural.AddList(ref hash, MetaCommandRefs);
        return hash.ToHashCode();
    }
}
