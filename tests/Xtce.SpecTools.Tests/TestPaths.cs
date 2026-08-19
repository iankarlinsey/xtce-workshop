namespace Xtce.SpecTools.Tests;

internal static class TestPaths
{
    /// <summary>
    /// Walks up from the test assembly's location to find the repo root (marked by
    /// global.json) so tests can locate reference/ regardless of the build output path.
    /// </summary>
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

    public static string Xtce12Xsd => Path.Combine(RepoRoot, "reference", "1.2", "SpaceSystem.xsd");
}
