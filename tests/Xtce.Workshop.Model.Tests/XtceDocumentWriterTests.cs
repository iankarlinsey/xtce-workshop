using System.Text;
using Xunit;

namespace Xtce.Workshop.Model.Tests;

public class XtceDocumentWriterTests
{
    [Fact]
    public void Write_ChildlessSpaceSystem_RoundTripsThroughReader()
    {
        var original = new SpaceSystem("Minimal", []);

        var xml = XtceDocumentWriter.Write(original);
        var reloaded = XtceDocumentReader.Load(ToStream(xml));

        Assert.Equal(original, reloaded);
    }

    [Fact]
    public void Write_NestedSpaceSystem_RoundTripsThroughReader()
    {
        var original = new SpaceSystem("Mission", [
            new SpaceSystem("Bus", [
                new SpaceSystem("Power", []),
                new SpaceSystem("Thermal", []),
            ]),
            new SpaceSystem("Payload", []),
        ]);

        var xml = XtceDocumentWriter.Write(original);
        var reloaded = XtceDocumentReader.Load(ToStream(xml));

        Assert.Equal(original, reloaded);
    }

    [Fact]
    public void Write_MinimalSampleLoadedThenWritten_RoundTripsThroughReader()
    {
        using var stream = File.OpenRead(TestPaths.MinimalSample);
        var loaded = XtceDocumentReader.Load(stream);

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(ToStream(xml));

        Assert.Equal(loaded, reloaded);
    }

    [Fact]
    public void Write_NestedSampleLoadedThenWritten_RoundTripsThroughReader()
    {
        using var stream = File.OpenRead(TestPaths.NestedSample);
        var loaded = XtceDocumentReader.Load(stream);

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(ToStream(xml));

        Assert.Equal(loaded, reloaded);
    }

    [Fact]
    public void Write_IncludesXtceNamespaceOnRootElement()
    {
        var spaceSystem = new SpaceSystem("Minimal", []);

        var xml = XtceDocumentWriter.Write(spaceSystem);

        Assert.Contains("http://www.omg.org/spec/XTCE/20180204", xml);
    }

    private static MemoryStream ToStream(string xml) => new(Encoding.UTF8.GetBytes(xml));
}
