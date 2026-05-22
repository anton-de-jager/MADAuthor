using System.Text.Json;
using MadAuthor.Application.Audit;
using MadAuthor.Application.Auth;
using MadAuthor.Domain.Entities;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Audit;

public class AuditService(
    MadAuthorDbContext db,
    ICurrentUserService currentUser,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditService> logger) : IAuditService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task LogAsync(string entity, string? entityId, string action, object? changes = null)
    {
        try
        {
            var ctx = httpContextAccessor.HttpContext;
            var entry = new AuditLog
            {
                UserId = currentUser.UserId,
                CompanyId = currentUser.CompanyId,
                Entity = entity,
                EntityId = entityId,
                Action = action,
                ChangesJson = changes is not null ? JsonSerializer.Serialize(changes, JsonOpts) : null,
                IpAddress = ctx?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = ctx?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
                    ? (ua.Length > 500 ? ua[..500] : ua)
                    : null,
                CreatedDate = DateTime.UtcNow,
            };
            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write audit log entry for {Entity}/{Action}", entity, action);
        }
    }
}
