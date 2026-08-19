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
