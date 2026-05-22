using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MadAuthor.Application.Auth;
using Microsoft.IdentityModel.Tokens;

namespace MadAuthor.Infrastructure.Auth;

public class JwtServiceOptions
{
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "madauthor";
    public string Audience { get; set; } = "madauthor-web";
    public int AccessTokenMinutes { get; set; } = 15;
}

public class JwtService(JwtServiceOptions options) : IJwtService
{
    public (string Token, DateTime ExpiresAt) IssueAccessToken(
        Guid userId, string email, Guid companyId, IEnumerable<string> roles)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
            throw new InvalidOperationException("JWT signing key is not configured.");

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("cid", companyId.ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string GenerateRefreshTokenRaw()
    {
        // 256-bit random token, base64url-encoded — opaque to clients.
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncoder.Encode(bytes.ToArray());
    }

    public string HashRefreshToken(string raw)
    {
        // Refresh tokens are stored as SHA-256 hashes so a DB leak doesn't surrender live tokens.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
