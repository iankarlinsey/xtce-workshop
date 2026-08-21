using Xtce.Workshop.Model;

namespace Xtce.Workshop.Cli;

/// <summary>Renders a file's declared root namespace as one human-readable line.</summary>
public static class DeclaredNamespace
{
    public static string Describe(string filePath)
    {
        string? rootNamespace;
        try
        {
            using var stream = File.OpenRead(filePath);
            rootNamespace = XtceNamespace.ReadRootNamespace(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(unknown)";
        }

        return rootNamespace switch
        {
            null or "" => "(none)",
            _ => XtceNamespace.VersionFor(rootNamespace) is string version
                ? $"{rootNamespace} (XTCE {version})"
                : $"{rootNamespace} (not an XTCE namespace)",
        };
    }
}
