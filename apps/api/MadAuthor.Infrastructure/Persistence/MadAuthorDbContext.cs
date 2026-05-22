using MadAuthor.Domain.Common;
using MadAuthor.Domain.Entities;
using MadAuthor.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Infrastructure.Persistence;

public class MadAuthorDbContext(DbContextOptions<MadAuthorDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyMember> CompanyMembers => Set<CompanyMember>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<BookProject> BookProjects => Set<BookProject>();
    public DbSet<BookRequest> BookRequests => Set<BookRequest>();
    public DbSet<BookChapter> BookChapters => Set<BookChapter>();
    public DbSet<BookCharacter> BookCharacters => Set<BookCharacter>();
    public DbSet<BookAsset> BookAssets => Set<BookAsset>();
    public DbSet<BookExport> BookExports => Set<BookExport>();
    public DbSet<BookCover> BookCovers => Set<BookCover>();
    public DbSet<PublishingPlatform> PublishingPlatforms => Set<PublishingPlatform>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AIJobQueueEntry> AIJobQueue => Set<AIJobQueueEntry>();
    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // /claude operator task system. See docs/08-claude-task-system.md.
    public DbSet<ClaudeTask> ClaudeTasks => Set<ClaudeTask>();
    public DbSet<ClaudePromptTemplate> ClaudePromptTemplates => Set<ClaudePromptTemplate>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public override int SaveChanges()
    {
        StampAudit();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampAudit()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedDate == default)
            {
                entry.Entity.CreatedDate = now;
            }
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedDate = now;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        ConfigureIdentity(b);
        ConfigureCompanies(b);
        ConfigureAuthors(b);
        ConfigureBooks(b);
        ConfigureJobs(b);
        ConfigureNotifications(b);
        ConfigureAudit(b);
        ConfigureClaudeTasks(b);
    }

    private static void ConfigureIdentity(ModelBuilder b)
    {
        b.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            e.Property(x => x.AvatarUrl).HasMaxLength(1024);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(512).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
        });
    }

    private static void ConfigureCompanies(ModelBuilder b)
    {
        b.Entity<Company>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.Property(x => x.LogoUrl).HasMaxLength(1024);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.OwnerUserId);
        });

        b.Entity<CompanyMember>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company)
                .WithMany(c => c.Members)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.CompanyId }).IsUnique();
            e.HasIndex(x => x.CompanyId);
        });
    }

    private static void ConfigureAuthors(ModelBuilder b)
    {
        b.Entity<Author>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PenName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Website).HasMaxLength(500);
            e.Property(x => x.PreferredWritingStyle).HasMaxLength(200);
            e.Property(x => x.DefaultLanguage).HasMaxLength(10).IsRequired();
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.CompanyId);
        });
    }

    private static void ConfigureBooks(ModelBuilder b)
    {
        b.Entity<BookProject>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Subtitle).HasMaxLength(300);
            e.Property(x => x.Genre).HasMaxLength(100);
            e.Property(x => x.TargetAudience).HasMaxLength(200);
            e.Property(x => x.WritingTone).HasMaxLength(100);
            e.Property(x => x.Language).HasMaxLength(10).IsRequired();
            e.Property(x => x.TargetReadingLevel).HasMaxLength(50);
            e.Property(x => x.Isbn).HasMaxLength(20);
            e.Property(x => x.CopyrightText).HasMaxLength(500);
            e.Property(x => x.PublishingGoal).HasMaxLength(200);
            e.HasIndex(x => new { x.CompanyId, x.Status });
            e.HasIndex(x => x.OwnerUserId);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        // Children of BookProject get a matching soft-delete filter so EF doesn't warn about
        // required-end relationships to a filtered parent. See:
        // https://learn.microsoft.com/ef/core/querying/filters#use-of-required-navigations
        b.Entity<BookRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DesiredTone).HasMaxLength(100);
            e.Property(x => x.DesiredLength).HasMaxLength(100);
            e.Property(x => x.POVStyle).HasMaxLength(50);
            e.Property(x => x.WritingStyle).HasMaxLength(200);
            e.Property(x => x.EndingType).HasMaxLength(100);
            e.Property(x => x.ThemesCsv).HasMaxLength(500);
            e.Property(x => x.KeywordsCsv).HasMaxLength(500);
            e.Property(x => x.EducationalLevel).HasMaxLength(50);
            e.Property(x => x.CitationStyle).HasMaxLength(50);
            e.Property(x => x.TargetPlatformsCsv).HasMaxLength(200);
            e.Property(x => x.RequestedFormatsCsv).HasMaxLength(200);
            e.HasOne(x => x.BookProject)
                .WithMany(p => p.Requests)
                .HasForeignKey(x => x.BookProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BookProjectId, x.Status });
            e.HasQueryFilter(x => x.BookProject!.IsDeleted == false);
        });

        b.Entity<BookChapter>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.HasOne(x => x.BookProject)
                .WithMany(p => p.Chapters)
                .HasForeignKey(x => x.BookProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BookProjectId, x.ChapterNumber }).IsUnique();
            e.HasQueryFilter(x => x.BookProject!.IsDeleted == false);
        });

        b.Entity<BookCharacter>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.BookProject)
                .WithMany(p => p.Characters)
                .HasForeignKey(x => x.BookProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.BookProject!.IsDeleted == false);
        });

        b.Entity<BookAsset>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(500).IsRequired();
            e.Property(x => x.BlobContainer).HasMaxLength(200).IsRequired();
            e.Property(x => x.BlobKey).HasMaxLength(1024).IsRequired();
            e.Property(x => x.MimeType).HasMaxLength(200).IsRequired();
            e.Property(x => x.ChecksumSha256).HasMaxLength(64);
            e.HasOne(x => x.BookProject)
                .WithMany(p => p.Assets)
                .HasForeignKey(x => x.BookProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BookProjectId, x.AssetType });
            e.HasQueryFilter(x => x.BookProject!.IsDeleted == false);
        });

        b.Entity<BookExport>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.BlobKey).HasMaxLength(1024);
            e.Property(x => x.ChecksumSha256).HasMaxLength(64);
            e.Property(x => x.ErrorMessage).HasMaxLength(1000);
            e.HasOne(x => x.BookProject)
                .WithMany(p => p.Exports)
                .HasForeignKey(x => x.BookProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BookProjectId, x.ExportType, x.Status });
            e.HasQueryFilter(x => x.BookProject!.IsDeleted == false);
        });

        b.Entity<BookCover>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Style).HasMaxLength(200);
            e.HasOne(x => x.BookProject)
                .WithMany(p => p.Covers)
                .HasForeignKey(x => x.BookProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.BookProject!.IsDeleted == false);
        });

        b.Entity<PublishingPlatform>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });
    }

    private static void ConfigureJobs(ModelBuilder b)
    {
        b.Entity<AIJobQueueEntry>(e =>
        {
            e.ToTable("AIJobQueue");
            e.HasKey(x => x.Id);
            e.Property(x => x.Stage).HasMaxLength(100);
            e.Property(x => x.ClaimedBy).HasMaxLength(200);
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
            // The critical polling index. See docs/03-worker-and-job-lifecycle.md §3.
            e.HasIndex(x => new { x.Status, x.Priority, x.CreatedDate });
            e.HasIndex(x => x.BookProjectId);
        });

        b.Entity<WorkerHeartbeat>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkerId).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.WorkerId).IsUnique();
        });
    }

    private static void ConfigureNotifications(ModelBuilder b)
    {
        b.Entity<Notification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            e.Property(x => x.LinkUrl).HasMaxLength(500);
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedDate });
        });
    }

    private static void ConfigureAudit(ModelBuilder b)
    {
        b.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Entity).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Action).HasMaxLength(50).IsRequired();
            e.Property(x => x.IpAddress).HasMaxLength(45);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.HasIndex(x => new { x.CompanyId, x.Entity, x.CreatedDate });
            e.HasIndex(x => new { x.UserId, x.CreatedDate });
        });
    }

    private static void ConfigureClaudeTasks(ModelBuilder b)
    {
        b.Entity<ClaudeTask>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            // Description / Notes / AttachmentsJson are nvarchar(max) by default -- fine.
            // The critical polling index for ClaudeTasksController.Next(). Matches the
            // bucket-0 query (Status in active set, priority asc, id asc). See
            // docs/08-claude-task-system.md section 2.
            e.HasIndex(x => new { x.Status, x.Priority, x.Id });
        });

        b.Entity<ClaudePromptTemplate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
            // ValueJson is nvarchar(max) -- callers stringify whatever fits.
        });
    }
}
