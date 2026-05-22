using System.Security.Claims;
using MadAuthor.Application.Auth;

namespace MadAuthor.Api.Auth;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), out var id)
            ? id : null;

    public Guid? CompanyId =>
        Guid.TryParse(Principal?.FindFirstValue("cid"), out var id) ? id : null;

    public string? Email =>
        Principal?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
}
