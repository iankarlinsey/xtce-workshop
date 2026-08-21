namespace Xtce.Workshop.Cli;

/// <summary>version.txt beside the binaries (image builds), or "dev".</summary>
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
