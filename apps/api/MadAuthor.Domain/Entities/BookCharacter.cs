using MadAuthor.Domain.Common;

namespace MadAuthor.Domain.Entities;

public class BookCharacter : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid BookProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Personality { get; set; }
    public string? Background { get; set; }
    public string? Goals { get; set; }
    public string? Conflicts { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public BookProject? BookProject { get; set; }
}
