using System.Text.Json;
using System.Text.Json.Serialization;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Cli;

/// <summary>
/// The `stats` command: per-SpaceSystem and document-total counts.
/// Exit codes: 0 = report printed, 2 = unusable input.
/// </summary>
public static class StatsCommand
{
    public const int ExitOk = 0;
    public const int ExitError = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static int Run(string filePath, bool json, TextWriter output, TextWriter errorOutput)
    {
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

        var metrics = XtceDocumentMetrics.Compute(document);

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(metrics, JsonOptions));
        }
        else
        {
            output.WriteLine($"XTCE document metrics: {filePath}");
            output.WriteLine();
            output.WriteLine($"{"SYSTEM",-40} {"PARAMS",7} {"TYPES",7} {"CONTNRS",7} {"MSGS",5} {"CMDS",5} {"OPAQUE",7}");
            foreach (var system in metrics.Systems)
            {
                var local = system.Local;
                output.WriteLine($"{system.SystemPath,-40} {local.Parameters,7} {local.ParameterTypes,7} {local.Containers,7} {local.Messages,5} {local.MetaCommands,5} {local.PreservedFragments,7}");
            }
            output.WriteLine();
            var totals = metrics.Totals;
            output.WriteLine($"Totals: {metrics.Systems.Count} system(s), {totals.Parameters} parameter(s), " +
                $"{totals.ParameterTypes} type(s), {totals.Containers} container(s), {totals.Messages} message(s), " +
                $"{totals.MetaCommands} command(s), {totals.PreservedFragments} preserved fragment(s)");
            if (totals.ParameterTypesByKind.Count > 0)
            {
                output.WriteLine("Types by kind: " + string.Join(", ",
                    totals.ParameterTypesByKind.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
            }
        }

        return ExitOk;
    }
}
