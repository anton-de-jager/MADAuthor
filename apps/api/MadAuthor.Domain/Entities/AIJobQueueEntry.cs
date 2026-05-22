using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

/// <summary>
/// A unit of work for the Claude Code Desktop worker. See docs/03-worker-and-job-lifecycle.md.
/// </summary>
public class AIJobQueueEntry : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid BookProjectId { get; set; }
    public Guid? BookRequestId { get; set; }
    public AIJobType JobType { get; set; }
    public byte Priority { get; set; } = 5;
    public AIJobStatus Status { get; set; } = AIJobStatus.Pending;
    public string? Stage { get; set; }
    public int Progress { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? LockExpiresAt { get; set; }
    public DateTime? StartedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string? ErrorMessage { get; set; }
    public byte RetryCount { get; set; }
    public byte MaxRetries { get; set; } = 3;
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
