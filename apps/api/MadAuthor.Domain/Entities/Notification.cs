using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

public class Notification : IAuditableEntity, ITenantEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;
    public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.Pending;
    public DateTime? DeliveredAt { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
