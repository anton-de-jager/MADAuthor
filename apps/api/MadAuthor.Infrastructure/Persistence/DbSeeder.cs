using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Persistence;

public static class DbSeeder
{
    /// <summary>
    /// Idempotent seed: roles, publishing platforms. Safe to run on every startup.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "User", "Author", "Admin", "Owner" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
                log.LogInformation("Seeded role {Role}", role);
            }
        }

        var db = sp.GetRequiredService<MadAuthorDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        await SeedBootstrapAdminAsync(db, userManager, log, ct);

        var platformSeeds = new[]
        {
            "Amazon KDP", "IngramSpark", "Lulu", "Barnes & Noble Press", "Gumroad", "Shopify", "Direct Web",
        };
        foreach (var name in platformSeeds)
        {
            if (!await db.PublishingPlatforms.AnyAsync(p => p.Name == name, ct))
            {
                db.PublishingPlatforms.Add(new PublishingPlatform
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    IsEnabled = true,
                    CreatedDate = DateTime.UtcNow,
                });
            }
        }

        // /claude operator task system defaults. See docs/08-claude-task-system.md.
        // Booleans are stored as JSON strings ("true" / "false") for consistency with
        // future non-bool settings.
        var claudeSettingSeeds = new (string Key, string Value)[]
        {
            ("workerActive",  "true"),
            ("scannerActive", "true"),
            ("deployNext",    "false"),
        };
        foreach (var (key, value) in claudeSettingSeeds)
        {
            if (!await db.AppSettings.AnyAsync(s => s.Key == key, ct))
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    ValueJson = value,
                    UpdatedDate = DateTime.UtcNow,
                });
                log.LogInformation("Seeded AppSetting {Key}={Value}", key, value);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedBootstrapAdminAsync(
        MadAuthorDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger log,
        CancellationToken ct)
    {
        var email = FirstNonBlank(
            Environment.GetEnvironmentVariable("PLATFORM_BOOTSTRAP_ADMIN_EMAIL"),
            Environment.GetEnvironmentVariable("DEFAULT_USER_EMAIL"),
            Environment.GetEnvironmentVariable("ADMIN_USER_EMAIL"));
        var password = FirstNonBlank(
            Environment.GetEnvironmentVariable("PLATFORM_BOOTSTRAP_ADMIN_PASSWORD"),
            Environment.GetEnvironmentVariable("DEFAULT_USER_PASSWORD"));

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            log.LogWarning("Bootstrap admin seed skipped because admin email/password environment variables are not set.");
            return;
        }

        var now = DateTime.UtcNow;
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                FirstName = "Super",
                LastName = "Admin",
                IsActive = true,
                CreatedDate = now,
            };

            var create = await userManager.CreateAsync(user, password);
            ThrowIfFailed(create, "create bootstrap admin");
            log.LogInformation("Seeded bootstrap admin user {Email}", email);
        }
        else
        {
            user.UserName = email;
            user.Email = email;
            user.EmailConfirmed = true;
            user.IsActive = true;
            if (string.IsNullOrWhiteSpace(user.FirstName)) user.FirstName = "Super";
            if (string.IsNullOrWhiteSpace(user.LastName)) user.LastName = "Admin";

            var update = await userManager.UpdateAsync(user);
            ThrowIfFailed(update, "update bootstrap admin");

            if (!await userManager.CheckPasswordAsync(user, password))
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var reset = await userManager.ResetPasswordAsync(user, token, password);
                ThrowIfFailed(reset, "reset bootstrap admin password");
                log.LogInformation("Reset bootstrap admin password for {Email}", email);
            }
        }

        foreach (var role in new[] { "User", "Admin", "Owner" })
        {
            if (!await userManager.IsInRoleAsync(user, role))
            {
                var addRole = await userManager.AddToRoleAsync(user, role);
                ThrowIfFailed(addRole, $"add bootstrap admin role {role}");
                log.LogInformation("Assigned bootstrap admin role {Role} to {Email}", role, email);
            }
        }

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Slug == "madauthor", ct);
        if (company is null)
        {
            company = new Company
            {
                Id = Guid.NewGuid(),
                Name = "MADAuthor",
                Slug = "madauthor",
                OwnerUserId = user.Id,
                Plan = CompanyPlan.Business,
                CreatedDate = now,
            };
            db.Companies.Add(company);
            log.LogInformation("Seeded bootstrap company {Company}", company.Name);
        }
        else
        {
            company.OwnerUserId = user.Id;
            company.Plan = CompanyPlan.Business;
        }

        var membership = await db.CompanyMembers
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.CompanyId == company.Id, ct);
        if (membership is null)
        {
            db.CompanyMembers.Add(new CompanyMember
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CompanyId = company.Id,
                Role = CompanyMemberRole.Owner,
                AcceptedDate = now,
                CreatedDate = now,
            });
        }
        else
        {
            membership.Role = CompanyMemberRole.Owner;
            membership.AcceptedDate ??= now;
        }
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static void ThrowIfFailed(IdentityResult result, string operation)
    {
        if (result.Succeeded) return;

        var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
        throw new InvalidOperationException($"Failed to {operation}: {errors}");
    }
}
