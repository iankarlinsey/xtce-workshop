using Microsoft.AspNetCore.Mvc;
using Xtce.Workshop.Api;

namespace Xtce.Workshop.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", version = BuildInfo.Version });
}
