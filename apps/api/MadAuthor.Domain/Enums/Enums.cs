namespace MadAuthor.Domain.Enums;

public enum CompanyPlan : byte
{
    Free = 0,
    Pro = 1,
    Business = 2,
}

public enum CompanyMemberRole : byte
{
    Owner = 0,
    Admin = 1,
    Editor = 2,
    Viewer = 3,
}

public enum FictionOrNonfiction : byte
{
    Fiction = 0,
    Nonfiction = 1,
    Mixed = 2,
}

public enum BookProjectStatus : byte
{
    Draft = 0,
    InProgress = 1,
    ReadyForReview = 2,
    Completed = 3,
    Archived = 4,
}

public enum BookProjectWorkflowStage : byte
{
    Intake = 0,
    Planning = 1,
    Drafting = 2,
    Editing = 3,
    Formatting = 4,
    Publishing = 5,
}

public enum BookRequestType : byte
{
    Idea = 0,
    Outline = 1,
    Manuscript = 2,
    Expansion = 3,
    SermonToBook = 4,
    NotesToBook = 5,
    BlogToBook = 6,
    CourseToBook = 7,
    JournalToBook = 8,
    VoiceTranscript = 9,
}

public enum BookRequestStatus : byte
{
    Submitted = 0,
    Queued = 1,
    InProgress = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
}

public enum BookChapterStatus : byte
{
    Planned = 0,
    Drafting = 1,
    Drafted = 2,
    Editing = 3,
    Final = 4,
}

public enum BookAssetType : byte
{
    Upload = 0,
    Cover = 1,
    Illustration = 2,
    Audio = 3,
    Generated = 4,
}

public enum StorageProvider : byte
{
    Local = 0,
    AzureBlob = 1,
    S3 = 2,
}

public enum ScanStatus : byte
{
    Pending = 0,
    Clean = 1,
    Infected = 2,
    Skipped = 3,
}

public enum BookExportType : byte
{
    Pdf = 0,
    Epub = 1,
    Docx = 2,
    PrintPdfKdp = 3,
    PrintPdfIngram = 4,
    Html = 5,
    Markdown = 6,
    // Mobi deliberately excluded — KDP no longer accepts it for new submissions.
}

public enum BookExportStatus : byte
{
    Queued = 0,
    Running = 1,
    Ready = 2,
    Failed = 3,
}

public enum BookCoverStatus : byte
{
    Pending = 0,
    Generating = 1,
    Ready = 2,
    Failed = 3,
    Selected = 4,
}

public enum NotificationType : byte
{
    JobStarted = 0,
    JobProgress = 1,
    JobCompleted = 2,
    ExportReady = 3,
    Error = 4,
    System = 5,
}

public enum NotificationChannel : byte
{
    InApp = 0,
    Email = 1,
    Sms = 2,
    WhatsApp = 3,
}

public enum DeliveryStatus : byte
{
    Pending = 0,
    Sent = 1,
    Delivered = 2,
    Failed = 3,
}

public enum AIJobType : byte
{
    PlanBook = 0,
    ResearchTopic = 1,
    DraftChapter = 2,
    EditChapter = 3,
    ContinuityCheck = 4,
    GenerateCover = 5,
    GenerateMetadata = 6,
    GenerateMarketing = 7,
}

public enum AIJobStatus : byte
{
    Pending = 0,
    Claimed = 1,
    InProgress = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
}

// Operator/dev task pipeline. Distinct from AIJobStatus because the state
// machines differ -- claude tasks have FAILED-as-terminal (no retry) and
// DEFERRED (operator-input needed). See docs/08-claude-task-system.md.
public enum ClaudeTaskStatus : byte
{
    Pending = 0,
    InProgress = 1,
    ToBeDeployed = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5,
    Deferred = 6,
}
