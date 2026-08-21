namespace Xtce.Workshop.Api;

/// <summary>
/// The deployment's identity: contents of version.txt written next to the binaries at
/// image build time (short commit SHA + UTC build time), or "dev" when running outside
/// a stamped build (local dotnet run, test hosts).
/// </summary>
public static class BuildInfo
{
    public static string Version { get; } = Read();

    private static string Read()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "version.txt");
            var text = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            return text.Length > 0 ? text : "dev";
        }
        catch (IOException)
        {
            return "dev";
        }
    }
}
