namespace MadAuthor.Application.Audit;

/// <summary>
/// Writes rows to the AuditLog table. Fire-and-forget safe - failures are
/// swallowed so audit logging never breaks the calling action.
/// </summary>
public interface IAuditService
{
    Task LogAsync(string entity, string? entityId, string action, object? changes = null);
}
