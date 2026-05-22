using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

/// <summary>
/// A dev / operator task queued for the autonomous Claude Code worker to drain.
/// Free-form, distinct from the structured <see cref="AIJobQueueEntry"/> book pipeline.
/// See docs/08-claude-task-system.md.
/// </summary>
public class ClaudeTask : IAuditableEntity
{
    public int Id { get; set; } // autoincrement int -- short, human-friendly IDs for the operator UI
    public string Title { get; set; } = string.Empty; // [MaxLength(300)]
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public ClaudeTaskStatus Status { get; set; } = ClaudeTaskStatus.Pending;
    public byte Priority { get; set; } = 3; // 1=critical, 2=high, 3=normal, 4=low

    /// <summary>
    /// JSON array of attachment metadata. Shape: <c>[{filename, originalName, mimeType, size, url}]</c>.
    /// Files live under <c>claude-task-attachments/{taskId}/...</c> via <see cref="MadAuthor.Application.Storage.IFileStorage"/>.
    /// </summary>
    public string? AttachmentsJson { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
