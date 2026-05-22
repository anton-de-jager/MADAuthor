using MadAuthor.Application.Email;
using MadAuthor.Application.Ingestion;
using MadAuthor.Application.Security;
using MadAuthor.Application.Storage;
using MadAuthor.Infrastructure.Email;
using MadAuthor.Infrastructure.Identity;
using MadAuthor.Infrastructure.Ingestion;
using MadAuthor.Infrastructure.Security;
using MadAuthor.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MadAuthor.Infrastructure.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddMadAuthorPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<MadAuthorDbContext>(opts =>
            opts.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(MadAuthorDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            }));

        // Identity's default token providers (password reset, email confirm) require IDataProtectionProvider.
        // ASP.NET Core hosting registers this automatically, but EF design-time tools spin up a bare host
        // without it. Registering explicitly makes both contexts work.
        services.AddDataProtection();

        // SecurityStampValidator (registered transitively by AddSignInManager) needs TimeProvider on
        // .NET 8. The Web host provides one; the EF design-time host doesn't. TryAdd so we don't
        // override an existing registration when the Web host is active.
        services.TryAddSingleton(TimeProvider.System);

        services
            .AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MadAuthorDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        return services;
    }

    public static IServiceCollection AddMadAuthorStorage(
        this IServiceCollection services, string rootPath)
    {
        services.AddSingleton(new LocalFileStorageOptions { RootPath = rootPath });
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<ITextExtractor, TextExtractor>();
        return services;
    }

    public static IServiceCollection AddMadAuthorEmail(
        this IServiceCollection services, SmtpEmailOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }

    /// <summary>
    /// Wires up the file-upload virus scanner. When <paramref name="clamAvHost"/> is non-empty,
    /// uses the <see cref="ClamAvFileScanner"/> against the configured <c>clamd</c> daemon.
    /// Otherwise falls back to <see cref="NoOpFileScanner"/> which records every upload as Skipped.
    /// </summary>
    public static IServiceCollection AddMadAuthorFileScanner(
        this IServiceCollection services, string? clamAvHost, int clamAvPort)
    {
        if (string.IsNullOrWhiteSpace(clamAvHost))
        {
            services.AddSingleton<IFileScanner, NoOpFileScanner>();
        }
        else
        {
            services.AddSingleton(new ClamAvOptions { Host = clamAvHost, Port = clamAvPort });
            services.AddSingleton<IFileScanner, ClamAvFileScanner>();
        }
        return services;
    }
}
