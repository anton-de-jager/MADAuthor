using Hangfire.Dashboard;
using System.Security.Claims;

namespace MadAuthor.Api.Auth;

/// <summary>
/// Hangfire's built-in dashboard has no auth. We require the connected user to carry the
/// Admin role on their JWT. (Dev convenience: if `MADAUTHOR_HANGFIRE_OPEN=true` env var is
/// set, allow any authenticated user — handy when bootstrapping a new local DB.)
/// </summary>
public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var principal = http.User;
        if (principal?.Identity?.IsAuthenticated != true) return false;

        if (string.Equals(Environment.GetEnvironmentVariable("MADAUTHOR_HANGFIRE_OPEN"), "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return principal.IsInRole("Admin") || principal.IsInRole("Owner");
    }
}
