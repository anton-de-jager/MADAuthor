using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

public class Company : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? BrandingJson { get; set; }
    public Guid OwnerUserId { get; set; }
    public CompanyPlan Plan { get; set; } = CompanyPlan.Free;
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public ICollection<CompanyMember> Members { get; set; } = new List<CompanyMember>();
}
