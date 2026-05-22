namespace MadAuthor.Domain.Common;

public interface IAuditableEntity
{
    DateTime CreatedDate { get; set; }
    DateTime? UpdatedDate { get; set; }
}
