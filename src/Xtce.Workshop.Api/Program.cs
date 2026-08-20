using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Enums (ParameterTypeKind, ValidationSeverity) serialize as their string name, not the
// underlying int — self-documenting over the wire, and nothing depends on the numeric form.
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The frontend legitimately posts sparse documents (omitted collections); the
// XtceDocumentNormalizer owns null-shape repair. [ApiController]'s automatic 400 on
// model-state errors would reject those bodies before the normalizer ever runs.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
    options.SuppressModelStateInvalidFilter = true);

var app = builder.Build();

app.MapControllers();

app.Run();

// Exposed so Xtce.Workshop.Api.Tests can spin this app up via WebApplicationFactory<Program>.
public partial class Program { }
