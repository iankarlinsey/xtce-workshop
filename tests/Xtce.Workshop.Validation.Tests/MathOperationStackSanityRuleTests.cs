using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R24: MathOperation postfix programs must be stack-consistent.</summary>
public class MathOperationStackSanityRuleTests
{
    private const string R24 = "XTCE-1.2-R24-math-operation-stack-sanity";

    private static MathOperationTerm Value(string text) => new(MathOperandKind.Value, text);
    private static MathOperationTerm Op(string text) => new(MathOperandKind.Operator, text);
    private static MathOperationTerm This() => new(MathOperandKind.ThisParameter);

    private static SpaceSystem WithCalibratorTerms(params MathOperationTerm[] terms) =>
        new("S", [], new TelemetryMetaData(
            [
                new ParameterTypeDefinition("T", ParameterTypeKind.Integer,
                    DataEncoding: new DataEncoding(DataEncodingKind.Integer,
                        DefaultCalibrator: new Calibrator(CalibratorKind.MathOperation, MathTerms: terms))),
            ],
            []));

    [Test]
    public void ConsistentProgram_IsClean()
    {
        var issues = XtceValidator.Validate(WithCalibratorTerms(
            This(), Value("2"), Op("*"), Value("1.5"), Op("+")));

        Assert.DoesNotContain(issues, i => i.RuleId == R24);
    }

    [Test]
    public void OperatorUnderflow_IsFlagged()
    {
        var issues = XtceValidator.Validate(WithCalibratorTerms(This(), Op("*")));

        var issue = Assert.Single(issues, i => i.RuleId == R24);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Contains("needs 2 operand(s) but the stack holds 1", issue.Message);
    }

    [Test]
    public void LeftoverStackValues_AreFlagged()
    {
        var issues = XtceValidator.Validate(WithCalibratorTerms(This(), Value("2")));

        var issue = Assert.Single(issues, i => i.RuleId == R24);
        Assert.Contains("leaves 2 value(s)", issue.Message);
    }

    [Test]
    public void StackManipulationOperators_UseTheirDocumentedEffects()
    {
        // this dup * → x², one result; over/drop round-trip a value.
        var clean = XtceValidator.Validate(WithCalibratorTerms(This(), Op("dup"), Op("*")));
        Assert.DoesNotContain(clean, i => i.RuleId == R24);

        var alsoClean = XtceValidator.Validate(WithCalibratorTerms(
            This(), Value("3"), Op("over"), Op("drop"), Op("+")));
        Assert.DoesNotContain(alsoClean, i => i.RuleId == R24);
    }

    [TestCase("!")]
    [TestCase("~")]
    [TestCase("div")]
    public void AmbiguouslyDocumentedOperators_SkipTheWholeProgram(string ambiguous)
    {
        // The spec's own stack notation contradicts these operators' semantics — never guess,
        // even when the rest of the program would be flagged.
        var issues = XtceValidator.Validate(WithCalibratorTerms(Op(ambiguous), Op("*")));

        Assert.DoesNotContain(issues, i => i.RuleId == R24);
    }

    [Test]
    public void AlgorithmMathOperations_AreCheckedToo()
    {
        var document = new SpaceSystem("S", [], new TelemetryMetaData([], [],
            AlgorithmSet:
            [
                new Algorithm("Bad", AlgorithmKind.Math, MathOperation: new TriggeredMathOperation(
                    [Value("2"), Op("abs"), Op("+")], "Out")),
            ]));

        var issues = XtceValidator.Validate(document);

        var issue = Assert.Single(issues, i => i.RuleId == R24);
        Assert.Contains("'+' at position 3", issue.Message);
        Assert.Contains("AlgorithmSet/Bad", issue.Location);
    }

    [Test]
    public void ContextCalibratorPrograms_AreCheckedToo()
    {
        var document = new SpaceSystem("S", [], new TelemetryMetaData(
            [
                new ParameterTypeDefinition("T", ParameterTypeKind.Integer,
                    DataEncoding: new DataEncoding(DataEncodingKind.Integer, ContextCalibrators:
                    [
                        new ContextCalibrator(
                            new MatchCriteria(new Comparison("Mode", "1")),
                            new Calibrator(CalibratorKind.MathOperation, MathTerms: [Op("sin")])),
                    ])),
            ],
            []));

        var issues = XtceValidator.Validate(document);

        var issue = Assert.Single(issues, i => i.RuleId == R24);
        Assert.Contains("'sin' at position 1", issue.Message);
    }

    [Test]
    public void EmptyTermList_IsNotFlagged()
    {
        // Schema-invalid (the choice requires at least one child) but not this rule's business.
        var issues = XtceValidator.Validate(WithCalibratorTerms());

        Assert.DoesNotContain(issues, i => i.RuleId == R24);
    }
}
