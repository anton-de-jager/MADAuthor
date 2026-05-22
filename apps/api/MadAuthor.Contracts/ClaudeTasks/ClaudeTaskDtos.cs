using MadAuthor.Domain.Enums;

namespace MadAuthor.Contracts.ClaudeTasks;

/// <summary>
/// Single attachment metadata entry inside <see cref="ClaudeTaskDetail.Attachments"/>.
/// Files live under <c>claude-task-attachments/{taskId}/...</c> via <c>IFileStorage</c>.
/// </summary>
public record ClaudeTaskAttachment(
    string Filename,      // storage key (relative to container) -- e.g. "12/abc123.pdf"
    string OriginalName,  // user-supplied filename for display
    string MimeType,
    long Size,
    string Url);          // resolved URL (or relative path) the operator UI can render

public record ClaudeTaskSummary(
    int Id,
    string Title,
    string? Description,
    string? Notes,
    ClaudeTaskStatus Status,
    byte Priority,
    int AttachmentCount,
    DateTime CreatedDate,
    DateTime? UpdatedDate);

public record ClaudeTaskDetail(
    int Id,
    string Title,
    string? Description,
    string? Notes,
    ClaudeTaskStatus Status,
    byte Priority,
    IReadOnlyList<ClaudeTaskAttachment> Attachments,
    DateTime CreatedDate,
    DateTime? UpdatedDate);

public record CreateClaudeTaskRequest(
    string Title,
    string? Description,
    string? Notes,
    ClaudeTaskStatus? Status,    // optional -- defaults to Pending server-side
    byte? Priority);             // optional -- defaults to 3

/// <summary>
/// Partial update. Only non-null fields are written. The worker PATCHes bodies like
/// <c>{ "Status": "InProgress" }</c> and the service MUST NOT blow away description.
/// </summary>
public record UpdateClaudeTaskRequest(
    string? Title,
    string? Description,
    string? Notes,
    ClaudeTaskStatus? Status,
    byte? Priority);

public record ImportBulkClaudeTasksRequest(
    IReadOnlyList<ImportBulkClaudeTaskItem> Items);

public record ImportBulkClaudeTaskItem(
    string Title,
    string? Description,
    string? Notes,
    byte? Priority);

public record ImportBulkClaudeTasksResponse(
    int Created,
    int Skipped,
    IReadOnlyList<int> CreatedIds,
    IReadOnlyList<string> SkippedTitles);

/// <summary>
/// Wrapper for <c>GET /api/claude-tasks/next</c>. JSON-parseable even when the queue
/// is empty (in which case the controller returns 204 with no body, but the shape
/// here is what the 200 response wraps).
/// </summary>
public record ClaudeTaskNextResponse(ClaudeTaskDetail? Task);

/// <summary>
/// Bucketed response for <c>GET /api/claude-tasks</c>. The UI uses the buckets to
/// section the page (active at top, terminal at bottom).
/// </summary>
public record ClaudeTaskListResponse(
    IReadOnlyList<ClaudeTaskSummary> Active,        // Pending + InProgress + Deferred (priority ASC, createdDate ASC)
    IReadOnlyList<ClaudeTaskSummary> ToBeDeployed,  // ToBeDeployed (priority ASC, createdDate ASC)
    IReadOnlyList<ClaudeTaskSummary> Terminal);     // Completed + Cancelled + Failed (createdDate DESC, capped)

public record ClaudePromptTemplateDto(
    int Id,
    string Name,
    string? Description,
    string Content,
    DateTime CreatedDate,
    DateTime? UpdatedDate);

public record CreateClaudePromptTemplateRequest(
    string Name,
    string? Description,
    string Content);

public record UpdateClaudePromptTemplateRequest(
    string? Name,
    string? Description,
    string? Content);

public record AppSettingDto(string Key, string ValueJson);

public record UpdateAppSettingRequest(string ValueJson);

/// <summary>
/// Realtime SignalR payload broadcast on every claude-task mutation to the
/// <c>claude-tasks</c> hub group. The Angular operator page subscribes after
/// joining via <c>NotificationHub.JoinClaudeTasksGroup</c>.
/// </summary>
public record ClaudeTaskEvent(
    string Type,    // "task.created" | "task.updated" | "task.deleted"
    int TaskId,
    ClaudeTaskDetail? Task);   // null on delete
