using MadAuthor.Contracts.ClaudeTasks;
using MadAuthor.Domain.Entities;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Controllers;

/// <summary>
/// Generic operator-tunable settings (key/value JSON pairs). Currently used for the /claude
/// worker + scanner toggles (<c>workerActive</c>, <c>scannerActive</c>, <c>deployNext</c>).
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Owner")]
[Route("api/settings")]
[Route("api/ai-settings")]
public class SettingsController(MadAuthorDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppSettingDto>>> List(CancellationToken ct = default)
    {
        var rows = await db.AppSettings.OrderBy(s => s.Key).ToListAsync(ct);
        return rows.Select(s => new AppSettingDto(s.Key, s.ValueJson)).ToList();
    }

    [HttpPatch]
    public async Task<ActionResult<IReadOnlyList<AppSettingDto>>> UpsertMany(
        [FromBody] Dictionary<string, object?> req,
        CancellationToken ct = default)
    {
        if (req.Count == 0)
            return BadRequest(new { error = "At least one setting is required." });

        foreach (var (key, value) in req)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
                return BadRequest(new { error = "Setting keys must be 1-100 characters." });

            var valueJson = value is string s
                ? s
                : System.Text.Json.JsonSerializer.Serialize(value);

            var existing = await db.AppSettings.FindAsync(new object[] { key }, ct);
            if (existing is null)
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    ValueJson = valueJson,
                    UpdatedDate = DateTime.UtcNow,
                });
            }
            else
            {
                existing.ValueJson = valueJson;
                existing.UpdatedDate = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
        var rows = await db.AppSettings.OrderBy(s => s.Key).ToListAsync(ct);
        return rows.Select(s => new AppSettingDto(s.Key, s.ValueJson)).ToList();
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<AppSettingDto>> Get(string key, CancellationToken ct = default)
    {
        var s = await db.AppSettings.FindAsync(new object[] { key }, ct);
        return s is null ? NotFound() : new AppSettingDto(s.Key, s.ValueJson);
    }

    /// <summary>
    /// Upsert a single setting by key. Body is the JSON-serialised value as a STRING
    /// (e.g. <c>{ "ValueJson": "true" }</c> for a bool, <c>{ "ValueJson": "{\"foo\":1}" }</c>
    /// for an object). The server doesn't parse it -- the operator UI handles per-key shape.
    /// </summary>
    [HttpPatch("{key}")]
    public async Task<ActionResult<AppSettingDto>> Upsert(
        string key,
        [FromBody] UpdateAppSettingRequest req,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            return BadRequest(new { error = "Key must be 1-100 characters." });
        if (req.ValueJson is null)
            return BadRequest(new { error = "ValueJson is required (use \"null\" string for null)." });

        var existing = await db.AppSettings.FindAsync(new object[] { key }, ct);
        if (existing is null)
        {
            existing = new AppSetting
            {
                Key = key,
                ValueJson = req.ValueJson,
                UpdatedDate = DateTime.UtcNow,
            };
            db.AppSettings.Add(existing);
        }
        else
        {
            existing.ValueJson = req.ValueJson;
            existing.UpdatedDate = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return new AppSettingDto(existing.Key, existing.ValueJson);
    }
}
