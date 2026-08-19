namespace Xtce.Workshop.Model.Tests;

internal static class TestPaths
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
                dir = dir.Parent;

            return dir?.FullName
                ?? throw new InvalidOperationException("Could not locate repo root (global.json) from " + AppContext.BaseDirectory);
        }
    }

    public static string MinimalSample => Path.Combine(RepoRoot, "samples", "minimal-1.2.xml");
    public static string NestedSample => Path.Combine(RepoRoot, "samples", "nested-1.2.xml");
    public static string TelemetrySample => Path.Combine(RepoRoot, "samples", "telemetry-1.2.xml");
    public static string PreservationSample => Path.Combine(RepoRoot, "samples", "preservation-1.2.xml");
    public static string XtceSchema => Path.Combine(RepoRoot, "reference", "1.2", "SpaceSystem.xsd");
    public static string XmlNamespaceSchema => Path.Combine(RepoRoot, "reference", "1.2", "xml.xsd");
}
