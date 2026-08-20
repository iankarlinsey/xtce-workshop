using System.Diagnostics;
using Xtce.Workshop.Model;
using Xunit;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>Issue #43: R15 radix literals + validator performance at scale.</summary>
public class HardeningTests
{
    private const string R15 = "XTCE-1.2-R15-typed-value-valid-for-type";

    private static SpaceSystem WithInitialValue(string value, bool? signed = false, long? sizeInBits = 8) =>
        new("S", [], new TelemetryMetaData(
            [new ParameterTypeDefinition("T", ParameterTypeKind.Integer, Signed: signed, SizeInBits: sizeInBits)],
            [new Parameter("P", "T", value)]));

    [Theory]
    [InlineData("0x1F")]      // 31 — hex, in range for unsigned 8-bit
    [InlineData("0XFF")]      // 255 — upper-case prefix, boundary
    [InlineData("0o17")]      // 15 — octal
    [InlineData("0b1010")]    // 10 — binary
    [InlineData("42")]        // plain base 10 still works
    public void RadixPrefixedIntegerLiterals_AreValid(string value)
    {
        var issues = XtceValidator.Validate(WithInitialValue(value));

        Assert.DoesNotContain(issues, i => i.RuleId == R15);
    }

    [Theory]
    [InlineData("0x100")]     // 256 — hex, OUT of unsigned 8-bit range: radix parsing must feed range checking
    [InlineData("0xZZ")]      // not hex digits
    [InlineData("0b102")]     // not binary digits
    public void BadRadixLiterals_AreStillFlagged(string value)
    {
        var issues = XtceValidator.Validate(WithInitialValue(value));

        Assert.Single(issues, i => i.RuleId == R15);
    }

    [Fact]
    public void NegativeHexLiteral_ParsesWithSign()
    {
        // signed 8-bit: -0x10 = -16, in range.
        var issues = XtceValidator.Validate(WithInitialValue("-0x10", signed: true));

        Assert.DoesNotContain(issues, i => i.RuleId == R15);
    }

    [Fact]
    public void RadixLiteralInComparisonValue_IsValid()
    {
        var telemetry = new TelemetryMetaData(
            [new ParameterTypeDefinition("T", ParameterTypeKind.Integer, Signed: false, SizeInBits: 16)],
            [new Parameter("Apid", "T")],
            ContainerSet:
            [
                new SequenceContainer("Base", []),
                new SequenceContainer("Sub", [], new BaseContainer("Base", new RestrictionCriteria(
                    Comparison: new Comparison("Apid", "0x2A")))),
            ]);

        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry));

        Assert.DoesNotContain(issues, i => i.RuleId == R15);
    }

    [Fact]
    public void Validate_FiveThousandParametersTypesAndContainers_CompletesQuickly()
    {
        // The project's central constraint is performance at scale. This exercises the
        // full validator (context build, name resolution, all 21 rules including the
        // document-wide R09/R16 walks) over a densely cross-referenced document.
        const int count = 5000;
        var types = new List<ParameterTypeDefinition>(count);
        var parameters = new List<Parameter>(count);
        var containers = new List<SequenceContainer>(count);
        for (var i = 0; i < count; i++)
        {
            types.Add(new ParameterTypeDefinition($"T{i}", ParameterTypeKind.Integer, InitialValue: "1",
                Signed: false, SizeInBits: 16));
            parameters.Add(new Parameter($"P{i}", $"T{i}", "42"));
            containers.Add(new SequenceContainer($"C{i}",
                [new SequenceEntry(SequenceEntryKind.ParameterRef, $"P{i}")],
                i > 0 ? new BaseContainer($"C{i - 1}", new RestrictionCriteria(
                    Comparison: new Comparison($"P{i}", "1"))) : null));
        }
        var root = new SpaceSystem("Big", [], new TelemetryMetaData(types, parameters, ContainerSet: containers));

        var stopwatch = Stopwatch.StartNew();
        var issues = XtceValidator.Validate(root);
        stopwatch.Stop();

        Assert.Empty(issues);
        // Generous bound (CI containers are slow); the point is catching accidental
        // quadratic blowups, not micro-benchmarks. Under coverage instrumentation
        // (issue #55: CI sets XTCE_COVERAGE_RUN) wall-clock is meaningless — coverlet's
        // per-branch hit tracking roughly doubles this workload — so only the functional
        // assertion applies there.
        if (Environment.GetEnvironmentVariable("XTCE_COVERAGE_RUN") is null)
        {
            Assert.True(stopwatch.ElapsedMilliseconds < 10_000,
                $"Validation of {count}x3 items took {stopwatch.ElapsedMilliseconds}ms.");
        }
    }
}
