using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace MadAuthor.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "alive", utc = DateTime.UtcNow });

    [HttpGet("ready")]
    public IActionResult Ready() => Ok(new { status = "ready", db = "not_checked", utc = DateTime.UtcNow });
}
