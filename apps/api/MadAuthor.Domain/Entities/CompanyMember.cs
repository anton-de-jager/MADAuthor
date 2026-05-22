using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

public class CompanyMember : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public CompanyMemberRole Role { get; set; }
    public DateTime? InvitedDate { get; set; }
    public DateTime? AcceptedDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public Company? Company { get; set; }
}
