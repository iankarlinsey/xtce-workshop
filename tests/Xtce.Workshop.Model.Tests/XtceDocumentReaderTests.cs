using System.Text;
using Xunit;

namespace Xtce.Workshop.Model.Tests;

public class XtceDocumentReaderTests
{
    [Fact]
    public void Load_MinimalSampleFile_ReturnsSpaceSystemWithName()
    {
        using var stream = File.OpenRead(TestPaths.MinimalSample);

        var result = XtceDocumentReader.Load(stream);

        Assert.Equal("Minimal", result.Name);
    }

    [Fact]
    public void Load_MinimalSampleFile_ReturnsEmptyChildrenNotNull()
    {
        using var stream = File.OpenRead(TestPaths.MinimalSample);

        var result = XtceDocumentReader.Load(stream);

        Assert.NotNull(result.Children);
        Assert.Empty(result.Children);
    }

    [Fact]
    public void Load_NestedSampleFile_ReturnsCorrectStructureAtEveryLevel()
    {
        using var stream = File.OpenRead(TestPaths.NestedSample);

        var result = XtceDocumentReader.Load(stream);

        Assert.Equal("Mission", result.Name);
        Assert.Equal(2, result.Children.Count);

        var bus = result.Children[0];
        Assert.Equal("Bus", bus.Name);
        Assert.Equal(2, bus.Children.Count);
        Assert.Equal("Power", bus.Children[0].Name);
        Assert.Empty(bus.Children[0].Children);
        Assert.Equal("Thermal", bus.Children[1].Name);
        Assert.Empty(bus.Children[1].Children);

        var payload = result.Children[1];
        Assert.Equal("Payload", payload.Name);
        Assert.Empty(payload.Children);
    }

    [Fact]
    public void Load_NotWellFormedXml_ThrowsXtceParseException()
    {
        using var stream = ToStream("<SpaceSystem name=\"Broken\"");

        var ex = Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
        Assert.Contains("not well-formed", ex.Message);
    }

    [Fact]
    public void Load_WrongRootElement_ThrowsXtceParseException()
    {
        using var stream = ToStream("<NotASpaceSystem name=\"Wrong\"/>");

        var ex = Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
        Assert.Contains("SpaceSystem", ex.Message);
    }

    [Fact]
    public void Load_MissingNameAttribute_ThrowsXtceParseException()
    {
        using var stream = ToStream("<SpaceSystem xmlns=\"http://www.omg.org/spec/XTCE/20180204\"/>");

        var ex = Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Load_EmptyInput_ThrowsXtceParseException()
    {
        using var stream = ToStream("");

        Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
    }

    private static MemoryStream ToStream(string xml) => new(Encoding.UTF8.GetBytes(xml));
}
