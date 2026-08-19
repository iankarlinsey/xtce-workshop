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
    var xml = XtceDocumentWriter.Write(spaceSystem);
    return Results.Text(xml, "application/xml");
});

app.MapPost("/api/xtce/validate", (SpaceSystem spaceSystem) =>
{
    var validationIssues = XtceValidator.Validate(spaceSystem);
    return Results.Ok(new { validationIssues });
});

app.Run();

// Exposed so Xtce.Workshop.Api.Tests can spin this app up via WebApplicationFactory<Program>.
public partial class Program { }
