namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R13: a SplineCalibrator of interpolation order N requires at least N+1
/// SplinePoints (SplineCalibratorType/order, XSD line 2772 — "Order 2 would be quadratic
/// and in this special case, 3 points would be required, etc."). The XSD only enforces a
/// minimum of 2 points; the order dependency is the semantic gap this rule closes.
/// Splines live inside data encodings — preserved fragments — so every fragment reachable
/// from the node is scanned, including preserved CommandMetaData content.
/// </summary>
public sealed class SplineOrderRequiresMinPointsRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R13-spline-order-requires-min-points";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var (fragment, location) in FragmentEnumerator.EnumerateNode(context))
        {
            foreach (var spline in XmlFragmentInspector.FindSplineCalibrators(fragment.OuterXml))
            {
                if (spline.PointCount < spline.Order + 1)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        $"SplineCalibrator of order {spline.Order} has {spline.PointCount} point(s) — order {spline.Order} requires at least {spline.Order + 1}.",
                        CandidateNumber: 55);
                }
            }
        }
    }
}

/// <summary>
/// XTCE-1.2-R03: a Checksum with name="custom" must set InputAlgorithm (ChecksumType/name,
/// XSD line 2393) — without it there is no definition of what the custom checksum computes.
/// Checksums live inside binary data encodings — preserved fragments — so every fragment
/// reachable from the node is scanned.
/// </summary>
public sealed class ChecksumCustomRequiresInputAlgorithmRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R03-checksum-custom-requires-inputalgorithm";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var (fragment, location) in FragmentEnumerator.EnumerateNode(context))
        {
            foreach (var checksum in XmlFragmentInspector.FindChecksums(fragment.OuterXml))
            {
                if (checksum.Name == "custom" && !checksum.HasInputAlgorithm)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        "Checksum name=\"custom\" requires an InputAlgorithm element defining the custom computation.",
                        CandidateNumber: 49);
                }
            }
        }
    }
}
