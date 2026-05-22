using MadAuthor.Domain.Entities;
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
}
