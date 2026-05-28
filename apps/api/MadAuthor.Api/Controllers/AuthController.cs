using System.Text;
using MadAuthor.Api.Configuration;
using MadAuthor.Application.Audit;
using MadAuthor.Application.Auth;
using MadAuthor.Application.Email;
using MadAuthor.Contracts.Auth;
using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Identity;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MadAuthor.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIns,
    MadAuthorDbContext db,
    IJwtService jwt,
    JwtOptions jwtOptions,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    IConfiguration configuration,
    IAuditService audit) : ControllerBase
{
    private const string RefreshCookieName = "madauthor_refresh";

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
            return StatusCode(500, new { error = "JWT signing is not configured on the server." });

        if (await users.FindByEmailAsync(req.Email) is not null)
            return Conflict(new { error = "An account with this email already exists." });

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = req.Email,
            Email = req.Email,
            FirstName = req.FirstName.Trim(),
            LastName = req.LastName.Trim(),
            CreatedDate = DateTime.UtcNow,
        };

        // First user in the system gets Admin/Owner roles automatically - bootstraps the
        // workspace without a separate admin console. Subsequent users are plain Users.
        var isFirstUser = await users.Users.AsNoTracking().AnyAsync() == false;

        var create = await users.CreateAsync(user, req.Password);
        if (!create.Succeeded)
        {
            return BadRequest(new { errors = create.Errors.Select(e => e.Description) });
        }

        // Default role(s)
        await users.AddToRoleAsync(user, "User");
        if (isFirstUser)
        {
            await users.AddToRoleAsync(user, "Admin");
            await users.AddToRoleAsync(user, "Owner");
        }

        // Every new user gets a personal Company by default.
        var companyName = string.IsNullOrWhiteSpace(req.CompanyName)
            ? $"{user.FirstName}'s Workspace"
            : req.CompanyName.Trim();
        var slug = await GenerateUniqueSlug(companyName);
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = companyName,
            Slug = slug,
            OwnerUserId = user.Id,
            Plan = CompanyPlan.Free,
            CreatedDate = DateTime.UtcNow,
        };
        db.Companies.Add(company);

        db.CompanyMembers.Add(new CompanyMember
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CompanyId = company.Id,
            Role = CompanyMemberRole.Owner,
            AcceptedDate = DateTime.UtcNow,
            CreatedDate = DateTime.UtcNow,
        });

        db.Authors.Add(new Author
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CompanyId = company.Id,
            PenName = $"{user.FirstName} {user.LastName}".Trim(),
            DefaultLanguage = "en",
            CreatedDate = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        await SendConfirmationEmailAsync(user);

        await audit.LogAsync("User", user.Id.ToString(), "Registered", new { user.Email });

        return Ok(new { needsEmailConfirmation = true, email = user.Email });
    }

    [HttpPost("login")]
    [HttpPost("signin")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req)
    {
        return await LoginCore(req);
    }

    [HttpPost("session")]
    public async Task<ActionResult<AuthResponse>> LoginSession(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LoginRequest? body)
    {
        var req = body ?? new LoginRequest(
            Request.Headers["X-MAD-Email"].ToString(),
            Request.Headers["X-MAD-Password"].ToString());
        return await LoginCore(req);
    }

    private async Task<ActionResult<AuthResponse>> LoginCore(LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Email and password are required." });

        var user = await users.FindByEmailAsync(req.Email);
        if (user is null || !user.IsActive)
            return Unauthorized(new { error = "Invalid credentials." });

        if (!await users.IsEmailConfirmedAsync(user))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "EmailNotConfirmed",
                email = user.Email,
            });
        }

        var check = await signIns.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            if (check.IsLockedOut) return Unauthorized(new { error = "Account temporarily locked. Try again later." });
            return Unauthorized(new { error = "Invalid credentials." });
        }

        var companyId = await ResolveCompanyId(user.Id);
        user.LastLoginDate = DateTime.UtcNow;
        await users.UpdateAsync(user);

        await audit.LogAsync("User", user.Id.ToString(), "Login", new { user.Email });

        return await IssueTokens(user, companyId);
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.UserId) || string.IsNullOrWhiteSpace(req.Token))
            return BadRequest(new { error = "Invalid token." });

        var user = await users.FindByIdAsync(req.UserId);
        if (user is null) return BadRequest(new { error = "Invalid token." });

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Token));
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "Invalid or expired token." });
        }

        var result = await users.ConfirmEmailAsync(user, decoded);
        if (!result.Succeeded) return BadRequest(new { error = "Invalid or expired token." });
        return Ok(new { confirmed = true });
    }

    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest req)
    {
        // Don't reveal whether the email exists: always return 200. Only send if the user exists
        // and is not already confirmed.
        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is not null && user.IsActive && !await users.IsEmailConfirmedAsync(user))
            {
                await SendConfirmationEmailAsync(user);
            }
        }
        return Ok(new { sent = true });
    }

    /// <summary>
    /// Refresh-token replay grace window. When the SPA boots, multiple parallel HTTP calls
    /// can each present the same refresh-token cookie before any of them has received the
    /// rotated cookie back. The first one rotates the token in DB; subsequent ones arrive
    /// with the now-revoked old token. Without grace we treat them as replay and clear the
    /// cookie, signing the user out of their just-loaded page.
    ///
    /// Within this window, a revoked token whose ReplacedByTokenHash chain is intact is
    /// treated as the legitimate predecessor of the current session - we issue a fresh
    /// access token without further rotating. Replays AFTER this window are still treated
    /// as compromise (a legitimate client would have the new cookie by then). 30s is a
    /// generous bound for SPA-bootstrap fan-out without weakening reuse detection.
    /// </summary>
    private static readonly TimeSpan RefreshRotationGrace = TimeSpan.FromSeconds(30);

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var raw) || string.IsNullOrEmpty(raw))
            return Unauthorized(new { error = "No refresh token." });

        var hash = jwt.HashRefreshToken(raw);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (token is null || token.ExpiresAt < DateTime.UtcNow)
        {
            ClearRefreshCookie();
            return Unauthorized(new { error = "Refresh token invalid or expired." });
        }

        // ---- Rotation-grace path ------------------------------------------
        // Token revoked, but recently - and a replacement exists. Most likely a legitimate
        // SPA-bootstrap race: another parallel refresh on the same cookie already rotated
        // it. Walk the ReplacedBy chain forward to the live tip and issue a fresh access
        // token without further rotating - idempotent under retry.
        if (token.RevokedAt is not null)
        {
            var revokedAt = token.RevokedAt.Value;
            var insideGrace = DateTime.UtcNow - revokedAt <= RefreshRotationGrace;
            if (insideGrace && !string.IsNullOrEmpty(token.ReplacedByTokenHash))
            {
                // Follow chain - cap at a few hops to bound work in case of pathological data.
                var current = token;
                for (var hops = 0; hops < 5 && !string.IsNullOrEmpty(current.ReplacedByTokenHash); hops++)
                {
                    var next = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == current.ReplacedByTokenHash);
                    if (next is null) break;
                    current = next;
                    if (current.RevokedAt is null) break;
                }

                if (current.RevokedAt is null && current.ExpiresAt > DateTime.UtcNow)
                {
                    var graceUser = await users.FindByIdAsync(current.UserId.ToString());
                    if (graceUser is not null && graceUser.IsActive)
                    {
                        // Cookie stays as-is - the caller is presenting an older raw token,
                        // but the SPA-side dedupe means most callers will already have the
                        // fresh cookie. This response just hands them a valid access token.
                        var graceCompanyId = await ResolveCompanyId(graceUser.Id);
                        var graceRoles = await users.GetRolesAsync(graceUser);
                        var (access, expiresAt) = jwt.IssueAccessToken(
                            graceUser.Id, graceUser.Email!, graceCompanyId, graceRoles);
                        return new AuthResponse(access, expiresAt, new UserSummary(
                            graceUser.Id, graceUser.Email!, graceUser.FirstName, graceUser.LastName,
                            graceUser.AvatarUrl, graceCompanyId, (IReadOnlyList<string>)graceRoles));
                    }
                }
            }

            // Past the grace window, or no live tip - treat as replay.
            ClearRefreshCookie();
            return Unauthorized(new { error = "Refresh token invalid or expired." });
        }

        var user = await users.FindByIdAsync(token.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            ClearRefreshCookie();
            return Unauthorized(new { error = "User no longer active." });
        }

        // Rotate: revoke the used token, issue a fresh one.
        var newRaw = jwt.GenerateRefreshTokenRaw();
        token.RevokedAt = DateTime.UtcNow;
        token.ReplacedByTokenHash = jwt.HashRefreshToken(newRaw);
        await db.SaveChangesAsync();

        var companyId = await ResolveCompanyId(user.Id);
        return await IssueTokens(user, companyId, replacementRefreshRaw: newRaw);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (Request.Cookies.TryGetValue(RefreshCookieName, out var raw) && !string.IsNullOrEmpty(raw))
        {
            var hash = jwt.HashRefreshToken(raw);
            var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null);
            if (token is not null)
            {
                token.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me()
    {
        if (currentUser.UserId is not { } uid) return Unauthorized();
        var user = await users.FindByIdAsync(uid.ToString());
        if (user is null) return Unauthorized();
        var companyId = currentUser.CompanyId ?? await ResolveCompanyId(uid);
        var roles = await users.GetRolesAsync(user);
        return new MeResponse(new UserSummary(
            user.Id, user.Email!, user.FirstName, user.LastName, user.AvatarUrl, companyId, (IReadOnlyList<string>)roles));
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<AuthResponse> IssueTokens(ApplicationUser user, Guid companyId, string? replacementRefreshRaw = null)
    {
        var roles = await users.GetRolesAsync(user);
        var (access, expiresAt) = jwt.IssueAccessToken(user.Id, user.Email!, companyId, roles);

        var refreshRaw = replacementRefreshRaw ?? jwt.GenerateRefreshTokenRaw();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = jwt.HashRefreshToken(refreshRaw),
            ExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.RefreshTokenDays),
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        SetRefreshCookie(refreshRaw);

        return new AuthResponse(access, expiresAt, new UserSummary(
            user.Id, user.Email!, user.FirstName, user.LastName, user.AvatarUrl, companyId, (IReadOnlyList<string>)roles));
    }

    private void SetRefreshCookie(string raw)
    {
        var opts = new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,                  // tighter in prod; dev over HTTP still works
            SameSite = SameSiteMode.Lax,               // same-site across sibling subdomains is fine
            Path = "/api/auth",                        // narrow to auth endpoints only
            Expires = DateTimeOffset.UtcNow.AddDays(jwtOptions.RefreshTokenDays),
        };
        // In prod the SPA + API live on sibling subdomains; setting Domain to the parent
        // (.madprospects.com) lets the cookie flow on requests from the SPA to the API.
        // Configured via COOKIE_DOMAIN env var so dev (localhost) stays unchanged.
        var domain = Environment.GetEnvironmentVariable("COOKIE_DOMAIN");
        if (!string.IsNullOrWhiteSpace(domain)) opts.Domain = domain;
        Response.Cookies.Append(RefreshCookieName, raw, opts);
    }

    private void ClearRefreshCookie()
    {
        var opts = new CookieOptions { Path = "/api/auth" };
        var domain = Environment.GetEnvironmentVariable("COOKIE_DOMAIN");
        if (!string.IsNullOrWhiteSpace(domain)) opts.Domain = domain;
        Response.Cookies.Delete(RefreshCookieName, opts);
    }

    private async Task<Guid> ResolveCompanyId(Guid userId)
    {
        // Phase 1: one user → one company. If multi-membership later, the user picks via header or claim.
        var membership = await db.CompanyMembers
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedDate)
            .FirstOrDefaultAsync();
        return membership?.CompanyId
            ?? throw new InvalidOperationException($"User {userId} has no company membership.");
    }

    private async Task<string> GenerateUniqueSlug(string source)
    {
        var baseSlug = new string(source.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');
        var slug = baseSlug;
        var n = 1;
        while (await db.Companies.AnyAsync(c => c.Slug == slug))
        {
            slug = $"{baseSlug}-{++n}";
        }
        return slug;
    }

    private async Task SendConfirmationEmailAsync(ApplicationUser user)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        // Prefer the configured frontend base URL (set per environment so the link points at the
        // SPA, not the API). Fall back to the current request's scheme+host for dev convenience.
        var frontendBaseUrl = configuration["App:FrontendBaseUrl"];
        if (string.IsNullOrWhiteSpace(frontendBaseUrl))
        {
            frontendBaseUrl = $"{Request.Scheme}://{Request.Host}";
        }
        frontendBaseUrl = frontendBaseUrl.TrimEnd('/');

        var url = $"{frontendBaseUrl}/confirm-email?uid={user.Id}&token={encoded}";
        var firstName = string.IsNullOrWhiteSpace(user.FirstName) ? "there" : user.FirstName;

        var html = $"""
            <p>Hi {System.Net.WebUtility.HtmlEncode(firstName)},</p>
            <p>Welcome to MADAuthor. Click the link below to confirm your email and activate your account:</p>
            <p><a href="{url}">Confirm email</a></p>
            <p>If the link doesn't work, paste this URL into your browser:</p>
            <p>{url}</p>
            <p>If you didn't sign up, you can safely ignore this message.</p>
            """;

        await emailSender.SendAsync(
            toAddress: user.Email!,
            toName: $"{user.FirstName} {user.LastName}".Trim(),
            subject: "Confirm your MADAuthor email",
            htmlBody: html);
    }
}

public record ConfirmEmailRequest(string UserId, string Token);

public record ResendConfirmationRequest(string Email);
