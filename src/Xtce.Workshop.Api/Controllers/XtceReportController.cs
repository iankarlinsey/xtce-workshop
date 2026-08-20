using Microsoft.AspNetCore.Mvc;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Api.Controllers;

[ApiController]
[Route("api/xtce/report")]
public sealed class XtceReportController : ControllerBase
{
    /// <summary>The conformance report rendered as plain text, for saving to disk.</summary>
    [HttpPost("text")]
    public ContentResult Text([FromBody] SpaceSystem spaceSystem)
    {
        var document = XtceDocumentNormalizer.Normalize(spaceSystem);
        var report = ConformanceReportBuilder.Build(document);
        var text = ConformanceReportRenderer.ToText(report, document.Name, DateTimeOffset.UtcNow);
        return Content(text, "text/plain");
    }
}
