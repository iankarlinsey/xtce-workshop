using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Cli;

/// <summary>
/// The `export` command: parameters or containers as CSV (issue #54), to stdout or a file.
/// Exit codes: 0 = exported, 2 = unusable input/arguments.
/// </summary>
public static class ExportCommand
{
    public const int ExitOk = 0;
    public const int ExitError = 2;

    public static int Run(string filePath, string what, string? outPath, TextWriter output, TextWriter errorOutput)
    {
        if (what is not ("--parameters" or "--containers"))
        {
            errorOutput.WriteLine("error: expected --parameters or --containers.");
            return ExitError;
        }

        SpaceSystem document;
        try
        {
            using var stream = File.OpenRead(filePath);
            document = XtceDocumentReader.Load(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XtceParseException)
        {
            errorOutput.WriteLine($"error: {ex.Message}");
            return ExitError;
        }

        var csv = what == "--parameters"
            ? XtceCsvExporter.ExportParameters(document)
            : XtceCsvExporter.ExportContainers(document);

        if (outPath is null)
        {
            output.Write(csv);
        }
        else
        {
            try
            {
                File.WriteAllText(outPath, csv);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errorOutput.WriteLine($"error: {ex.Message}");
                return ExitError;
            }
            output.WriteLine($"wrote {outPath}");
        }

        return ExitOk;
    }
}
