using System.Text.Json;
using MadAuthor.Api.Realtime;
using MadAuthor.Contracts.ClaudeTasks;
using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Controllers;

/// <summary>
/// MADCloud callback surface. MADAuthor keeps AI execution outside the app and accepts
/// completed results through this small contract.
/// </summary>
[ApiController]
[Route("api/madcloud")]
public class MadCloudController(
    MadAuthorDbContext db,
    IHubContext<NotificationHub> hub) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [HttpGet("provider")]
    public IActionResult Provider() => Ok(new
    {
        provider = "MADCloud",
        onlyAiIntegration = true,
        callbackPath = "/api/madcloud/ai-results",
        operatorRoutes = new[] { "/ai", "/admin/ai" },
        directProvidersDisabled = new[] { "OpenAI", "Stability", "DeepL", "Whisper" },
    });

    [HttpPost("ai-results")]
    public async Task<IActionResult> ReceiveAiResult(
        [FromBody] MadCloudAiResult request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) && string.IsNullOrWhiteSpace(request.TaskId))
            return BadRequest(new { error = "requestId or taskId is required." });

        var resultKey = $"madcloud:last-result:{request.RequestId ?? request.TaskId}";
        var serialized = JsonSerializer.Serialize(request, JsonOpts);
        var existingResult = await db.AppSettings.FindAsync(new object[] { resultKey }, ct);
        if (existingResult is null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = resultKey,
                ValueJson = serialized,
                UpdatedDate = DateTime.UtcNow,
            });
        }
        else
        {
            existingResult.ValueJson = serialized;
            existingResult.UpdatedDate = DateTime.UtcNow;
        }

        if (int.TryParse(request.TaskId, out var taskId))
        {
            var task = await db.ClaudeTasks.FindAsync(new object[] { taskId }, ct);
            if (task is not null)
            {
                task.Status = request.Success ? ClaudeTaskStatus.Completed : ClaudeTaskStatus.Failed;
                task.Notes = string.IsNullOrWhiteSpace(request.Summary)
                    ? task.Notes
                    : $"{request.Summary}\n\nMADCloud request: {request.RequestId}".Trim();
                task.UpdatedDate = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);

        if (int.TryParse(request.TaskId, out var broadcastTaskId))
        {
            var task = await db.ClaudeTasks.FindAsync(new object[] { broadcastTaskId }, ct);
            if (task is not null)
            {
                var detail = ToDetail(task);
                await hub.Clients.Group(NotificationHub.AiTasksGroup)
                    .SendAsync("AiTaskEvent", new ClaudeTaskEvent("task.updated", task.Id, detail), ct);
                await hub.Clients.Group(NotificationHub.ClaudeTasksGroup)
                    .SendAsync("ClaudeTaskEvent", new ClaudeTaskEvent("task.updated", task.Id, detail), ct);
            }
        }

        return Ok(new { accepted = true, provider = "MADCloud" });
    }

    private static ClaudeTaskDetail ToDetail(ClaudeTask t) => new(
        Id: t.Id,
        Title: t.Title,
        Description: t.Description,
        Notes: t.Notes,
        Status: t.Status,
        Priority: t.Priority,
        Attachments: Array.Empty<ClaudeTaskAttachment>(),
        CreatedDate: t.CreatedDate,
        UpdatedDate: t.UpdatedDate);

    public sealed record MadCloudAiResult(
        string? RequestId,
        string? TaskId,
        bool Success,
        string? Summary,
        string? ResultText,
        JsonElement? ResultJson,
        string? Patch,
        string? Error,
        string? SourceApp,
        string? OutputType);
}
