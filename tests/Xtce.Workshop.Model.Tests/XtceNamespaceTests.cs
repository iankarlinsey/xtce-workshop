using System.Text;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Model.Tests;

public class XtceNamespaceTests
{
    private static Stream AsStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Test]
    public void ReadRootNamespace_Xtce12Document_ReturnsTheV12Namespace()
    {
        using var stream = AsStream(
            """<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat"/>""");

        Assert.Equal(XtceNamespace.V1_2, XtceNamespace.ReadRootNamespace(stream));
    }

    [Test]
    public void ReadRootNamespace_LegacyDocument_ReturnsTheSharedLegacyNamespace()
    {
        using var stream = AsStream(
            """<SpaceSystem xmlns="http://www.omg.org/space/xtce" name="Sat"/>""");

        Assert.Equal(XtceNamespace.Legacy, XtceNamespace.ReadRootNamespace(stream));
    }

    [Test]
    public void ReadRootNamespace_NoNamespace_ReturnsEmptyString()
    {
        using var stream = AsStream("""<SpaceSystem name="Sat"/>""");

        Assert.Equal("", XtceNamespace.ReadRootNamespace(stream));
    }

    [Test]
    public void ReadRootNamespace_CommentProlog_StillFindsTheRoot()
    {
        using var stream = AsStream(
            """
            <?xml version="1.0"?>
            <!-- license header -->
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat"/>
            """);

        Assert.Equal(XtceNamespace.V1_2, XtceNamespace.ReadRootNamespace(stream));
    }

    [Test]
    public void ReadRootNamespace_UnreadableInput_ReturnsNull()
    {
        using var notXml = AsStream("this is not xml at all <<<");
        using var empty = AsStream("");

        Assert.Null(XtceNamespace.ReadRootNamespace(notXml));
        Assert.Null(XtceNamespace.ReadRootNamespace(empty));
    }

    [Test]
    public void ReadRootNamespace_MalformedAfterRoot_StillReportsTheRootNamespace()
    {
        // The probe only needs the root start tag; damage further in is the loader's problem.
        using var stream = AsStream(
            """<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat"><Unclosed>""");

        Assert.Equal(XtceNamespace.V1_2, XtceNamespace.ReadRootNamespace(stream));
    }

    [TestCase("http://www.omg.org/spec/XTCE/20180204", "1.2")]
    [TestCase("http://www.omg.org/space/xtce", "1.0/1.1")]
    [TestCase("http://example.com/other", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void VersionFor_MapsRecognizedNamespacesOnly(string? namespaceUri, string? expected)
    {
        Assert.Equal(expected, XtceNamespace.VersionFor(namespaceUri));
    }
}
