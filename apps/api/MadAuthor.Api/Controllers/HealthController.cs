using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(MadAuthorDbContext db) : ControllerBase
{
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "alive", utc = DateTime.UtcNow });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready()
    {
        var dbOk = await db.Database.CanConnectAsync();
        return Ok(new { status = dbOk ? "ready" : "degraded", db = dbOk, utc = DateTime.UtcNow });
    }
}
