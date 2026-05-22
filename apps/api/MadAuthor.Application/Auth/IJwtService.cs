namespace MadAuthor.Application.Auth;

public interface IJwtService
{
    /// <summary>Issue a signed JWT for the user. Returns the token and its expiry time.</summary>
    (string Token, DateTime ExpiresAt) IssueAccessToken(Guid userId, string email, Guid companyId, IEnumerable<string> roles);

    /// <summary>Generate a fresh refresh token (raw, not yet hashed).</summary>
    string GenerateRefreshTokenRaw();

    /// <summary>Hash a raw refresh token for storage and lookup.</summary>
    string HashRefreshToken(string raw);
}
