using Microsoft.AspNetCore.Mvc;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Api.Controllers;

[ApiController]
[Route("api/xtce/export")]
public sealed class XtceExportController : ControllerBase
{
    [HttpPost("parameters")]
    public ContentResult Parameters([FromBody] SpaceSystem spaceSystem) =>
        Content(XtceCsvExporter.ExportParameters(XtceDocumentNormalizer.Normalize(spaceSystem)), "text/csv");

    [HttpPost("containers")]
    public ContentResult Containers([FromBody] SpaceSystem spaceSystem) =>
        Content(XtceCsvExporter.ExportContainers(XtceDocumentNormalizer.Normalize(spaceSystem)), "text/csv");
}
