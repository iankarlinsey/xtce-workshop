namespace Xtce.Workshop.Model;

/// <summary>
/// Replaces null collection properties with empty ones throughout a document. JSON binding
/// (System.Text.Json) happily passes null for an omitted array property — a client posting
/// {"name":"M"} without "children" would otherwise produce a SpaceSystem whose Children is
/// null and NRE deep inside the writer or validator. API endpoints normalize immediately
/// after binding so the rest of the codebase can keep its non-nullable invariants.
/// </summary>
public static class XtceDocumentNormalizer
{
    public static SpaceSystem Normalize(SpaceSystem spaceSystem) => spaceSystem with
    {
        Children = (spaceSystem.Children ?? []).Select(Normalize).ToList(),
        TelemetryMetaData = spaceSystem.TelemetryMetaData is { } telemetry
            ? telemetry with
            {
                ParameterTypeSet = telemetry.ParameterTypeSet ?? [],
                ParameterSet = telemetry.ParameterSet ?? [],
                ContainerSet = telemetry.ContainerSet
                    ?.Select(container => container with { EntryList = container.EntryList ?? [] })
                    .ToList(),
                MessageSet = telemetry.MessageSet is { } messageSet
                    ? messageSet with { Messages = messageSet.Messages ?? [] }
                    : null,
            }
            : null,
    };
}
