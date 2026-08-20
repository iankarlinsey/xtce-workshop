using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Cli;

/// <summary>
/// When a file fails to load, prints the complete evidence instead of one message:
/// every positioned load diagnostic from the best-effort reader, plus the full XSD
/// validation error list for the raw input.
/// </summary>
internal static class LoadFailure
{
    public static void Describe(string filePath, TextWriter errorOutput)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var result = XtceDocumentReader.LoadWithRecovery(stream);
            foreach (var diagnostic in result.Diagnostics)
            {
                var position = diagnostic.Line is { } line
                    ? $"({line}:{diagnostic.Column ?? 0}) "
                    : "";
                var kind = diagnostic.Kind == LoadDiagnosticKind.MalformedXml ? "xml" : "model";
                errorOutput.WriteLine($"  {kind} {position}{diagnostic.Path}: {diagnostic.Message}");
            }

            foreach (var schemaError in SchemaValidator.Validate(File.ReadAllText(filePath)))
            {
                errorOutput.WriteLine($"  schema: {schemaError}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The original error message already covers an unreadable file.
        }
    }
}
