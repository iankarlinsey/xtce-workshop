using System.Text.Json.Serialization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog is wired only here; application code depends on ILogger<T> exclusively.
// Sinks/levels come from the Serilog configuration section (env-overridable, e.g.
// Serilog__MinimumLevel__Default=Debug), defaulting to single-line JSON on stdout.
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Enums (ParameterTypeKind, ValidationSeverity) serialize as their string name, not the
// underlying int — self-documenting over the wire, and nothing depends on the numeric form.
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The frontend legitimately posts sparse documents (omitted collections); the
// XtceDocumentNormalizer owns null-shape repair. [ApiController]'s automatic 400 on
// model-state errors would reject those bodies before the normalizer ever runs.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
    options.SuppressModelStateInvalidFilter = true);

// Kestrel serves the Angular build directly (wwwroot in the container image) — there is
// no reverse proxy in front, so compression happens here.
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
// SPA deep links (any non-API, non-file route) resolve to the Angular entry point.
app.MapFallbackToFile("index.html");

app.Run();

// Exposed so Xtce.Workshop.Api.Tests can spin this app up via WebApplicationFactory<Program>.
public partial class Program { }
