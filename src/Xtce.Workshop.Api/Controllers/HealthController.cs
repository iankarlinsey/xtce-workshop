using Microsoft.AspNetCore.Mvc;

namespace Xtce.Workshop.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });
}
