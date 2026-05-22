namespace MadAuthor.Application.Auth;

/// <summary>Reads identity context from the active request (claims-based).</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? CompanyId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
}
