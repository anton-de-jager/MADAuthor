using MadAuthor.Domain.Common;

namespace MadAuthor.Domain.Entities;

/// <summary>
/// A reusable prompt template the operator picks when creating a new <see cref="ClaudeTask"/>.
/// The chosen template's <see cref="Content"/> pre-fills the task description.
/// </summary>
public class ClaudePromptTemplate : IAuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // unique, [MaxLength(200)]
    public string? Description { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
