namespace MadAuthor.Domain.Entities;

/// <summary>
/// Generic key-value app settings table. Used by the claude-task system for worker toggles
/// (<c>workerActive</c>, <c>scannerActive</c>, <c>deployNext</c>) and reserved for future
/// generic operator-tunable knobs that don't deserve their own table.
/// </summary>
/// <remarks>
/// Values are stored as JSON strings so booleans, numbers, and small objects all fit.
/// The operator UI handles deserialisation per key.
/// </remarks>
public class AppSetting
{
    public string Key { get; set; } = string.Empty; // [MaxLength(100)] -- primary key
    public string ValueJson { get; set; } = string.Empty;
    public DateTime UpdatedDate { get; set; }
}
