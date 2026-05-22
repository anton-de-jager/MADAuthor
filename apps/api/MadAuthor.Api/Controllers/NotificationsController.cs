using MadAuthor.Application.Auth;
using MadAuthor.Contracts.Notifications;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(
    MadAuthorDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationListResponse>> List(
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("No user id on the request principal.");

        if (limit is < 1 or > 200) limit = 50;

        var items = await db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedDate)
            .Take(limit)
            .Select(n => new NotificationDto(
                n.Id, n.Type, n.Title, n.Message, n.LinkUrl, n.IsRead, n.CreatedDate))
            .ToListAsync(ct);

        var unreadCount = await db.Notifications
            .CountAsync(n => n.UserId == userId && !n.IsRead, ct);

        return new NotificationListResponse(items, unreadCount);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("No user id on the request principal.");

        var count = await db.Notifications
            .CountAsync(n => n.UserId == userId && !n.IsRead, ct);

        return Ok(new { unreadCount = count });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("No user id on the request principal.");

        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
        if (notification is null) return NotFound();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.UpdatedDate = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<object>> MarkAllRead(CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("No user id on the request principal.");

        var count = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.UpdatedDate, DateTime.UtcNow), ct);

        return Ok(new { marked = count });
    }
}
