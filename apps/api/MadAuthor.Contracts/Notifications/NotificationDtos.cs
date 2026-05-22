using MadAuthor.Domain.Enums;

namespace MadAuthor.Contracts.Notifications;

public record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Message,
    string? LinkUrl,
    bool IsRead,
    DateTime CreatedDate);

public record NotificationListResponse(
    IReadOnlyList<NotificationDto> Items,
    int UnreadCount);
