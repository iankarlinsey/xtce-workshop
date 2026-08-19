using Xtce.Workshop.Model;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/xtce/load", async (IFormFile file) =>
{
    await using var stream = file.OpenReadStream();

    try
    {
        var spaceSystem = XtceDocumentReader.Load(stream);
        return Results.Ok(new { name = spaceSystem.Name });
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

app.Run();

// Exposed so Xtce.Workshop.Api.Tests can spin this app up via WebApplicationFactory<Program>.
public partial class Program { }
