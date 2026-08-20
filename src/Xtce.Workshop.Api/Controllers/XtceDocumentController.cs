using Microsoft.AspNetCore.Mvc;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Api.Controllers;

[ApiController]
[Route("api/xtce")]
public sealed class XtceDocumentController : ControllerBase
{
    private readonly ILogger<XtceDocumentController> _logger;

    public XtceDocumentController(ILogger<XtceDocumentController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads an uploaded XTCE file into the editable document model, best-effort: broken
    /// modeled elements are quarantined (preserved verbatim) with positioned diagnostics,
    /// and any load problem also triggers full XSD validation of the raw input so the
    /// response carries the complete evidence rather than a single message.
    /// </summary>
    [HttpPost("load")]
    public async Task<IActionResult> Load(IFormFile file)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        buffer.Position = 0;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = XtceDocumentReader.LoadWithRecovery(buffer);

        IReadOnlyList<string> schemaErrors = [];
        if (result.Diagnostics.Count > 0)
        {
            buffer.Position = 0;
            using var text = new StreamReader(buffer, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            schemaErrors = SchemaValidator.Validate(await text.ReadToEndAsync());
        }

        if (result.Document is null)
        {
            _logger.LogWarning("Rejected unloadable file {FileName} ({SizeBytes} bytes): {DiagnosticCount} diagnostic(s), {SchemaErrorCount} schema error(s)",
                file.FileName, file.Length, result.Diagnostics.Count, schemaErrors.Count);
            return BadRequest(new
            {
                error = result.Diagnostics.FirstOrDefault()?.Message ?? "The file could not be loaded.",
                diagnostics = result.Diagnostics,
                schemaErrors,
            });
        }

        var spaceSystem = result.Document;
        var tree = TreeNode.FromSpaceSystem(spaceSystem);
        var validationIssues = XtceValidator.Validate(spaceSystem);
        _logger.LogInformation(
            "Loaded {Document} ({SizeBytes} bytes): {IssueCount} validation issue(s), {DiagnosticCount} load diagnostic(s) in {ElapsedMs} ms",
            spaceSystem.Name, file.Length, validationIssues.Count, result.Diagnostics.Count, stopwatch.ElapsedMilliseconds);
        return Ok(new
        {
            name = spaceSystem.Name,
            tree,
            document = spaceSystem,
            validationIssues,
            diagnostics = result.Diagnostics,
            schemaErrors,
        });
    }

    /// <summary>Writes the document back out as XTCE XML.</summary>
    [HttpPost("save")]
    public ContentResult Save([FromBody] SpaceSystem spaceSystem)
    {
        var xml = XtceDocumentWriter.Write(XtceDocumentNormalizer.Normalize(spaceSystem));
        return Content(xml, "application/xml");
    }

    /// <summary>Runs every validation rule against the document.</summary>
    [HttpPost("validate")]
    public IActionResult Validate([FromBody] SpaceSystem spaceSystem)
    {
        var validationIssues = XtceValidator.Validate(XtceDocumentNormalizer.Normalize(spaceSystem));
        return Ok(new { validationIssues });
    }
}
