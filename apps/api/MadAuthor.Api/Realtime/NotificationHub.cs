using System.Security.Claims;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Realtime;

/// <summary>
/// SignalR hub for in-app realtime updates. JWT-authenticated (the access-token factory
/// on the Angular side passes the bearer token). Project-group joins are authorized:
/// the connection must own the project (or be a CompanyMember of its company).
/// </summary>
[Authorize]
public class NotificationHub(MadAuthorDbContext db) : Hub
{
    /// <summary>
    /// Stable group name used by <c>ClaudeTasksController</c> to broadcast
    /// <see cref="MadAuthor.Contracts.ClaudeTasks.ClaudeTaskEvent"/> messages.
    /// Kept as a constant so controllers and hub agree on the spelling.
    /// </summary>
    public const string ClaudeTasksGroup = "claude-tasks";

    public async Task JoinProjectGroup(Guid projectId)
    {
        if (!await CanAccessProject(projectId))
            throw new HubException("Not authorized to subscribe to that project.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project:{projectId}");
    }

    public Task LeaveProjectGroup(Guid projectId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project:{projectId}");

    /// <summary>
    /// Subscribe to the global /claude operator task stream. Admin/Owner only.
    /// Broadcasts: <c>{ Type, TaskId, Task }</c> on every claude-task mutation.
    /// See docs/08-claude-task-system.md section 3.
    /// </summary>
    public async Task JoinClaudeTasksGroup()
    {
        if (!IsAdminOrOwner())
            throw new HubException("Not authorized to subscribe to /claude tasks.");
        await Groups.AddToGroupAsync(Context.ConnectionId, ClaudeTasksGroup);
    }

    public Task LeaveClaudeTasksGroup() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ClaudeTasksGroup);

    private bool IsAdminOrOwner() =>
        Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("Owner") == true;

    private async Task<bool> CanAccessProject(Guid projectId)
    {
        var sub = Context.User?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
        var cidClaim = Context.User?.FindFirstValue("cid");
        if (!Guid.TryParse(sub, out var userId) || !Guid.TryParse(cidClaim, out var companyId))
            return false;

        return await db.BookProjects.AnyAsync(p =>
            p.Id == projectId
            && p.CompanyId == companyId
            && p.OwnerUserId == userId);
    }
}
