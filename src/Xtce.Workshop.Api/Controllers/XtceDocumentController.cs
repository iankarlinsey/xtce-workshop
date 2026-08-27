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
    [RequestSizeLimit(1_073_741_824)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_073_741_824)]
    public async Task<IActionResult> Load(IFormFile? file)
    {
        // With the automatic model-state 400 suppressed (sparse JSON documents), a failed
        // multipart binding — oversized upload, missing/misnamed form part — reaches this
        // action as null instead of short-circuiting. Answer with the evidence, never 500.
        if (file is null)
        {
            var bindingErrors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            // Say exactly which failure shape this is — these have distinct causes:
            // a 'file' FIELD (part without a filename parses as a form value, not a file),
            // a part under a different name, or binding errors (e.g. size limits).
            string error;
            if (bindingErrors.Count > 0)
            {
                error = string.Join(" ", bindingErrors);
            }
            else if (Request.HasFormContentType && Request.Form.TryGetValue("file", out _))
            {
                error = "The 'file' part arrived without a filename, so it was parsed as a plain form field "
                    + "instead of a file upload. Whatever sent this request stripped or omitted the filename "
                    + "from the multipart Content-Disposition.";
            }
            else if (Request.HasFormContentType && Request.Form.Files.Count > 0)
            {
                error = $"The upload contains file part(s) named [{string.Join(", ", Request.Form.Files.Select(f => f.Name))}] "
                    + "but none named 'file'.";
            }
            else
            {
                error = "The upload did not include a multipart part named 'file'.";
            }

            _logger.LogWarning("Load request without a usable file part: {Error}", error);
            return BadRequest(new
            {
                error,
                diagnostics = Array.Empty<LoadDiagnostic>(),
                schemaErrors = Array.Empty<SchemaError>(),
            });
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        buffer.Position = 0;

        return await LoadFromBuffer(buffer, file.FileName, file.Length);
    }

    /// <summary>
    /// Loads XTCE from raw text — the source-view path, where the document arrives as the
    /// editor's contents rather than an uploaded file. Same pipeline and response shape
    /// as the multipart load.
    /// </summary>
    [HttpPost("load-text")]
    [RequestSizeLimit(1_073_741_824)]
    public async Task<IActionResult> LoadText([FromBody] LoadTextRequest? request)
    {
        if (string.IsNullOrEmpty(request?.Xml))
        {
            return BadRequest(new
            {
                error = "The request body must be JSON with a non-empty 'xml' property.",
                diagnostics = Array.Empty<LoadDiagnostic>(),
                schemaErrors = Array.Empty<SchemaError>(),
            });
        }

        using var buffer = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(request.Xml));
        return await LoadFromBuffer(buffer, "(source editor)", buffer.Length);
    }

    private Task<IActionResult> LoadFromBuffer(MemoryStream buffer, string sourceName, long sizeBytes)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = LoadPipeline.Run(buffer.ToArray());

        if (outcome.Load.Document is null)
        {
            _logger.LogWarning("Rejected unloadable input {SourceName} ({SizeBytes} bytes): {DiagnosticCount} diagnostic(s), {SchemaErrorCount} schema error(s)",
                sourceName, sizeBytes, outcome.Load.Diagnostics.Count, outcome.SchemaErrors.Count);
        }
        else
        {
            _logger.LogInformation(
                "Loaded {Document} ({SizeBytes} bytes): {IssueCount} validation issue(s), {DiagnosticCount} load diagnostic(s) in {ElapsedMs} ms",
                outcome.Load.Document.Name, sizeBytes, outcome.ValidationIssues.Count, outcome.Load.Diagnostics.Count, stopwatch.ElapsedMilliseconds);
        }
        return Task.FromResult(LoadPipeline.ToActionResult(outcome));
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

public sealed record LoadTextRequest(string? Xml);
