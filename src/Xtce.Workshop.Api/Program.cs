using System.Text.Json.Serialization;
using Xtce.Workshop.Api;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

var builder = WebApplication.CreateBuilder(args);
// Enums (ParameterTypeKind, ValidationSeverity) serialize as their string name, not the
// underlying int — self-documenting over the wire, and nothing depends on the numeric form.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/xtce/load", async (IFormFile file) =>
{
    await using var stream = file.OpenReadStream();

    try
    {
        var spaceSystem = XtceDocumentReader.Load(stream);
        var tree = TreeNode.FromSpaceSystem(spaceSystem);
        var validationIssues = XtceValidator.Validate(spaceSystem);
        return Results.Ok(new { name = spaceSystem.Name, tree, document = spaceSystem, validationIssues });
    }
    catch (XtceParseException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).DisableAntiforgery();

app.MapPost("/api/xtce/save", (SpaceSystem spaceSystem) =>
{
    var xml = XtceDocumentWriter.Write(XtceDocumentNormalizer.Normalize(spaceSystem));
    return Results.Text(xml, "application/xml");
});

app.MapPost("/api/xtce/validate", (SpaceSystem spaceSystem) =>
{
    var validationIssues = XtceValidator.Validate(XtceDocumentNormalizer.Normalize(spaceSystem));
    return Results.Ok(new { validationIssues });
});

app.MapPost("/api/xtce/search", (SearchRequest request) =>
{
    var matches = XtceDocumentQuery.Search(XtceDocumentNormalizer.Normalize(request.Document), request.Query);
    return Results.Ok(new { matches });
});

app.MapPost("/api/xtce/usages", (UsagesRequest request) =>
{
    var usages = XtceDocumentQuery.FindParameterUsages(
        XtceDocumentNormalizer.Normalize(request.Document), request.SystemPath, request.ParameterName);
    return Results.Ok(new { usages });
});

app.MapPost("/api/xtce/metrics", (SpaceSystem spaceSystem) =>
{
    var metrics = XtceDocumentMetrics.Compute(XtceDocumentNormalizer.Normalize(spaceSystem));
    return Results.Ok(metrics);
});

app.MapPost("/api/xtce/report", (SpaceSystem spaceSystem) =>
{
    var report = ConformanceReportBuilder.Build(XtceDocumentNormalizer.Normalize(spaceSystem));
    return Results.Ok(report);
});

app.MapPost("/api/xtce/layout", (LayoutRequest request) =>
{
    var layout = PacketLayoutBuilder.Build(
        XtceDocumentNormalizer.Normalize(request.Document),
        request.SystemPath ?? [],
        request.ContainerName);
    return layout is null
        ? Results.NotFound(new { error = "Container not found." })
        : Results.Ok(layout);
});

app.Run();

// Exposed so Xtce.Workshop.Api.Tests can spin this app up via WebApplicationFactory<Program>.
public partial class Program { }

/// <summary>Request body for /api/xtce/layout.</summary>
public sealed record LayoutRequest(SpaceSystem Document, string ContainerName, int[]? SystemPath = null);

/// <summary>Request body for /api/xtce/search.</summary>
public sealed record SearchRequest(SpaceSystem Document, string Query);

/// <summary>Request body for /api/xtce/usages. SystemPath is a context path like "Root/Bus".</summary>
public sealed record UsagesRequest(SpaceSystem Document, string SystemPath, string ParameterName);
