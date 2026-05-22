using MadAuthor.Domain.Common;

namespace MadAuthor.Domain.Entities;

public class PublishingPlatform : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ApiSettingsJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
