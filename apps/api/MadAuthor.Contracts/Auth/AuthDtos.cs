namespace MadAuthor.Contracts.Auth;

public record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? CompanyName);

public record LoginRequest(string Email, string Password);

public record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    UserSummary User);

public record UserSummary(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    Guid CompanyId,
    IReadOnlyList<string> Roles);

public record RefreshRequest;  // refresh token comes from httpOnly cookie

public record MeResponse(UserSummary User);
