using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R24: a MathOperation's postfix (RPN) program must be stack-consistent —
/// every Operator must find enough operands on the stack, and the finished program must
/// leave exactly one result. Stack effects come from the documentation on
/// MathOperatorsType (XSD 4676ff). Three operators carry self-contradictory spec docs
/// ("!" and "~" documented as consuming two operands though semantically unary, "div"
/// documented unary though semantically binary — same class of finding as the FLAGGED
/// doc-vs-schema notes in research/xtce-1.2-rule-matrix-README.md); a program containing
/// any of them, or an operator not in the XSD enumeration, is skipped rather than
/// guessed at. Warning severity: the check flags programs that cannot evaluate, but the
/// ambiguity in the spec's own stack notation argues against hard errors.
/// </summary>
public sealed class MathOperationStackSanityRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R24-math-operation-stack-sanity";
    public ValidationSeverity Severity => ValidationSeverity.Warning;

    // (pops, pushes) per operator, from the XSD's documented "(before -- after)" effects.
    private static readonly Dictionary<string, (int Pops, int Pushes)> StackEffects = new()
    {
        // binary: x1 x2 -- result
        ["+"] = (2, 1), ["-"] = (2, 1), ["*"] = (2, 1), ["/"] = (2, 1), ["%"] = (2, 1),
        ["^"] = (2, 1), ["y^x"] = (2, 1), ["atan2"] = (2, 1),
        ["<<"] = (2, 1), [">>"] = (2, 1), ["&"] = (2, 1), ["|"] = (2, 1),
        ["&&"] = (2, 1), ["||"] = (2, 1),
        [">"] = (2, 1), [">="] = (2, 1), ["<"] = (2, 1), ["<="] = (2, 1),
        ["=="] = (2, 1), ["!="] = (2, 1), ["min"] = (2, 1), ["max"] = (2, 1), ["xor"] = (2, 1),
        // unary: x -- result
        ["ln"] = (1, 1), ["log"] = (1, 1), ["e^x"] = (1, 1), ["1/x"] = (1, 1), ["x!"] = (1, 1),
        ["tan"] = (1, 1), ["cos"] = (1, 1), ["sin"] = (1, 1),
        ["atan"] = (1, 1), ["acos"] = (1, 1), ["asin"] = (1, 1),
        ["tanh"] = (1, 1), ["cosh"] = (1, 1), ["sinh"] = (1, 1),
        ["atanh"] = (1, 1), ["acosh"] = (1, 1), ["asinh"] = (1, 1),
        ["abs"] = (1, 1), ["int"] = (1, 1),
        // stack manipulation
        ["swap"] = (2, 2), ["drop"] = (1, 0), ["dup"] = (1, 2), ["over"] = (2, 3),
    };

    // In the enumeration, but the documented stack effect contradicts the operator's
    // semantics — never guess.
    private static readonly HashSet<string> AmbiguousOperators = ["!", "~", "div"];

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var (type, location) in ModeledTypeSets.All(context))
        {
            foreach (var encoding in new[] { type.DataEncoding, type.TimeEncoding?.DataEncoding })
            {
                var calibrators = (encoding?.ContextCalibrators ?? [])
                    .Select(c => c.Calibrator)
                    .Prepend(encoding?.DefaultCalibrator);
                foreach (var calibrator in calibrators)
                {
                    if (calibrator is { Kind: CalibratorKind.MathOperation, MathTerms: { } terms }
                        && Describe(terms) is { } problem)
                    {
                        yield return new ValidationIssue(RuleId, Severity, location,
                            $"MathOperationCalibrator: {problem}");
                    }
                }
            }
        }

        var algorithmSets = new (IReadOnlyList<Algorithm>? Set, string SetPath)[]
        {
            (context.Node.TelemetryMetaData?.AlgorithmSet, $"{context.Path}/AlgorithmSet"),
            (context.Node.CommandMetaData?.AlgorithmSet, $"{context.Path}/CommandMetaData/AlgorithmSet"),
        };
        foreach (var (algorithmSet, setPath) in algorithmSets)
        {
            foreach (var algorithm in algorithmSet ?? [])
            {
                if (algorithm.MathOperation is { } operation
                    && Describe(operation.Terms) is { } problem)
                {
                    yield return new ValidationIssue(RuleId, Severity, $"{setPath}/{algorithm.Name}",
                        $"MathOperation: {problem}");
                }
            }
        }
    }

    /// <summary>Null when the program is stack-consistent (or contains an operator we must not judge).</summary>
    internal static string? Describe(IReadOnlyList<MathOperationTerm> terms)
    {
        var depth = 0;
        var position = 0;
        foreach (var term in terms)
        {
            position++;
            if (term.Kind != MathOperandKind.Operator)
            {
                depth++;
                continue;
            }
            var operatorText = term.Text ?? "";
            if (AmbiguousOperators.Contains(operatorText) || !StackEffects.TryGetValue(operatorText, out var effect))
            {
                return null; // ambiguous spec docs or foreign operator — skip, never guess
            }
            if (depth < effect.Pops)
            {
                return $"operator '{operatorText}' at position {position} needs {effect.Pops} operand(s) but the stack holds {depth}.";
            }
            depth = depth - effect.Pops + effect.Pushes;
        }
        if (terms.Count > 0 && depth != 1)
        {
            return $"the program leaves {depth} value(s) on the stack — a calibration/algorithm must leave exactly 1.";
        }
        return null;
    }
}
