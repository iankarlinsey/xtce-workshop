using Microsoft.AspNetCore.Mvc;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Api.Controllers;

[ApiController]
[Route("api/xtce")]
public sealed class XtceAnalysisController : ControllerBase
{
    /// <summary>The full per-candidate conformance report.</summary>
    [HttpPost("report")]
    public IActionResult Report([FromBody] SpaceSystem spaceSystem)
    {
        var report = ConformanceReportBuilder.Build(XtceDocumentNormalizer.Normalize(spaceSystem));
        return Ok(report);
    }

    /// <summary>Per-SpaceSystem and document-total counts.</summary>
    [HttpPost("metrics")]
    public IActionResult Metrics([FromBody] SpaceSystem spaceSystem)
    {
        var metrics = XtceDocumentMetrics.Compute(XtceDocumentNormalizer.Normalize(spaceSystem));
        return Ok(metrics);
    }

    /// <summary>Name/alias search across every named item kind.</summary>
    [HttpPost("search")]
    public IActionResult Search([FromBody] SearchRequest request)
    {
        var matches = XtceDocumentQuery.Search(XtceDocumentNormalizer.Normalize(request.Document), request.Query);
        return Ok(new { matches });
    }

    /// <summary>Every reference binding to the given parameter.</summary>
    [HttpPost("usages")]
    public IActionResult Usages([FromBody] UsagesRequest request)
    {
        var usages = XtceDocumentQuery.FindParameterUsages(
            XtceDocumentNormalizer.Normalize(request.Document), request.SystemPath, request.ParameterName);
        return Ok(new { usages });
    }

    /// <summary>Static bit layout for one container.</summary>
    [HttpPost("layout")]
    public IActionResult Layout([FromBody] LayoutRequest request)
    {
        var layout = PacketLayoutBuilder.Build(
            XtceDocumentNormalizer.Normalize(request.Document),
            request.SystemPath ?? [],
            request.ContainerName);
        return layout is null
            ? NotFound(new { error = "Container not found." })
            : Ok(layout);
    }
}
