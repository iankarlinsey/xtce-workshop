using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Xtce.Workshop.Api.Tests;

public class XtceExportEndpointTests 
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    [Test]
    public async Task PostExportParameters_ReturnsCsv()
    {
        var client = _factory.CreateClient();
        var document = new
        {
            name = "Sat",
            children = Array.Empty<object>(),
            telemetryMetaData = new
            {
                parameterTypeSet = new object[] { new { name = "T", kind = "Integer" } },
                parameterSet = new object[] { new { name = "P", parameterTypeRef = "T" } },
            },
        };

        var response = await client.PostAsJsonAsync("/api/xtce/export/parameters", document);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("SystemPath,Name,ParameterTypeRef", csv);
        Assert.Contains("Sat,P,T,Integer", csv);
    }
}
