using Microsoft.AspNetCore.Mvc;

namespace Xtce.Workshop.Api.Controllers;

/// <summary>
/// Polling counterpart to the synchronous load endpoints: start a job, poll its
/// progress, fetch the result once, or cancel it server-side.
/// </summary>
[ApiController]
[Route("api/xtce/jobs")]
public sealed class LoadJobsController : ControllerBase
{
    private readonly LoadJobService _jobs;

    public LoadJobsController(LoadJobService jobs)
    {
        _jobs = jobs;
    }

    [HttpPost]
    [RequestSizeLimit(1_073_741_824)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_073_741_824)]
    public async Task<IActionResult> StartFromFile(IFormFile? file)
    {
        if (file is null)
        {
            return BadRequest(new { error = "The upload did not include a usable multipart part named 'file'." });
        }
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        return Ok(new { jobId = _jobs.Start(buffer.ToArray()) });
    }

    [HttpPost("text")]
    [RequestSizeLimit(1_073_741_824)]
    public IActionResult StartFromText([FromBody] LoadTextRequest? request)
    {
        if (string.IsNullOrEmpty(request?.Xml))
        {
            return BadRequest(new { error = "The request body must be JSON with a non-empty 'xml' property." });
        }
        return Ok(new { jobId = _jobs.Start(System.Text.Encoding.UTF8.GetBytes(request.Xml)) });
    }

    [HttpGet("{id}")]
    public IActionResult Progress(string id)
    {
        var snapshot = _jobs.GetSnapshot(id);
        return snapshot is null ? NotFound(new { error = "Unknown or expired job." }) : Ok(snapshot);
    }

    [HttpGet("{id}/result")]
    public IActionResult Result(string id)
    {
        var snapshot = _jobs.GetSnapshot(id);
        if (snapshot is null)
        {
            return NotFound(new { error = "Unknown or expired job." });
        }
        if (snapshot.State == "failed")
        {
            return BadRequest(new { error = snapshot.Error ?? "The load failed." });
        }
        if (snapshot.State != "done")
        {
            return Conflict(new { error = $"The job is {snapshot.State}." });
        }
        var outcome = _jobs.TakeOutcome(id);
        return outcome is null
            ? NotFound(new { error = "The result was already collected." })
            : LoadPipeline.ToActionResult(outcome);
    }

    [HttpDelete("{id}")]
    public IActionResult Cancel(string id) =>
        _jobs.Cancel(id) ? NoContent() : NotFound(new { error = "Unknown or expired job." });
}
