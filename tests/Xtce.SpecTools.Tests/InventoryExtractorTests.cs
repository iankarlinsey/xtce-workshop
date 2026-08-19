using Xunit;

namespace Xtce.SpecTools.Tests;

public class InventoryExtractorTests
{
    // Counts below were independently cross-validated during design discussion by two
    // separate parser implementations (Python xml.etree.ElementTree and .NET XDocument)
    // landing on identical numbers for the XTCE 1.2 SpaceSystem.xsd. These act as a
    // regression baseline — a change here means either the schema changed or the walker
    // has a bug, and either should be investigated, not silently accepted.
    [Fact]
    public void Extract_MatchesIndependentlyCrossValidatedCounts_ForXtce12()
    {
        var result = InventoryExtractor.Extract(TestPaths.Xtce12Xsd);

        Assert.Equal(3562, result.TotalNodes);
        Assert.Equal(393, result.Elements.Count);
        Assert.Equal(265, result.Attributes.Count);
        Assert.Equal(284, result.ComplexTypes.Count);
        Assert.Equal(50, result.SimpleTypes.Count);
        Assert.Equal(192, result.Enumerations.Count);
        Assert.Equal(6, result.Patterns.Count);
        Assert.Equal(11, result.Keys.Count);
        Assert.Equal(0, result.KeyRefs.Count);
        Assert.Equal(0, result.Uniques.Count);
        Assert.Equal(219, result.OccursConstraints.Count);
        Assert.Equal(68, result.RefTypedNodes.Count);
    }

    [Fact]
    public void Extract_FindsNoKeyrefs_ConfirmingReferentialIntegrityIsUnenforced()
    {
        // XTCE 1.2's schema declares uniqueness (xs:key) for names like parameterNameKey,
        // containerNameKey, etc., but never an xs:keyref pointing back at them — so every
        // *Ref element/attribute in the spec resolves at the semantic level only, never
        // via schema validation. This is the concrete basis for treating referential
        // integrity as a required category of hand-written validation rules.
        var result = InventoryExtractor.Extract(TestPaths.Xtce12Xsd);

        Assert.NotEmpty(result.Keys);
        Assert.Empty(result.KeyRefs);
        Assert.NotEmpty(result.RefTypedNodes);
    }
}
