using MadAuthor.Domain.Common;

namespace MadAuthor.Domain.Entities;

public class Author : IAuditableEntity, ITenantEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public string PenName { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? Website { get; set; }
    public string? SocialLinksJson { get; set; }
    public string? GenresCsv { get; set; }
    public string? PreferredWritingStyle { get; set; }
    public string DefaultLanguage { get; set; } = "en";
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
