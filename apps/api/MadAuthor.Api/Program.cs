using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DotNetEnv;
using Hangfire;
using Hangfire.SqlServer;
using Hangfire.Dashboard;
using MadAuthor.Api.Auth;
using MadAuthor.Api.Configuration;
using MadAuthor.Api.Middleware;
using MadAuthor.Api.Realtime;
using MadAuthor.Application.Auth;
using MadAuthor.Infrastructure.Auth;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

// Load .env from the repo root by walking up the directory tree. No-op if not found.
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Config precedence: appsettings.json -> appsettings.{Env}.json -> appsettings.Local.json
// (gitignored, holds real creds) -> environment variables (including .env values).
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// --- Logging ---------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/madauthor-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

// --- Connection string ----------------------------------------------------
// Source: prefer ConnectionStrings:DefaultConnection from configuration
// (appsettings.json / appsettings.Local.json). Fall back to composing from
// DB_* env vars so prod hosting can ship without a Local file. The result
// is normalized to ensure TLS flags suit a self-signed/IP-based SQL Server.
var rawConnStr =
    NonBlank(builder.Configuration.GetConnectionString("DefaultConnection"))
    ?? ComposeConnectionStringFromEnv();

var rawHangfireConnStr =
    NonBlank(builder.Configuration.GetConnectionString("Hangfire"))
    ?? ComposeHangfireConnectionStringFromEnv();
var hangfireConnStr = NormalizeConnectionString(rawHangfireConnStr);
var connStr = NormalizeConnectionString(rawConnStr);
if (builder.Environment.IsDevelopment())
{
    EnsureSqlDatabaseExists(connStr);
    EnsureSqlDatabaseExists(hangfireConnStr);
}

// --- Persistence + Identity -----------------------------------------------
builder.Services.AddMadAuthorPersistence(connStr);

// --- File storage (local FS by default; swap for AzureBlob/S3 later) -----
var storageRoot = Environment.GetEnvironmentVariable("STORAGE_LOCAL_ROOT")
    ?? Path.Combine(builder.Environment.ContentRootPath, "storage");
Directory.CreateDirectory(storageRoot);
builder.Services.AddMadAuthorStorage(storageRoot);

// --- Audio transcription + image OCR (light up when OPENAI_API_KEY is set) ----
MadAuthor.Infrastructure.Ingestion.MediaProcessingDependencyInjection.AddMadAuthorMediaProcessing(builder.Services);

// --- Unsplash (royalty-free photo source for book covers) ----------------
builder.Services.AddSingleton(new MadAuthor.Infrastructure.Covers.UnsplashOptions
{
    AccessKey = Environment.GetEnvironmentVariable("UNSPLASH_ACCESS_KEY"),
    AppName = Environment.GetEnvironmentVariable("UNSPLASH_APP_NAME") ?? "MADAuthor",
});
builder.Services.AddHttpClient<MadAuthor.Application.Covers.IUnsplashClient,
    MadAuthor.Infrastructure.Covers.UnsplashClient>();

// --- AI image generation (OpenAI DALL-E preferred, Stability fallback, NoOp otherwise) --
MadAuthor.Infrastructure.Covers.ImageGenDependencyInjection.AddMadAuthorImageGen(builder.Services);

// --- Cover composer (QuestPDF-based typography overlay + print-wrap PDF) ----
// Singleton because the composer is stateless and there's no per-request setup.
builder.Services.AddSingleton<MadAuthor.Application.Covers.ICoverComposer,
    MadAuthor.Infrastructure.Covers.QuestPdfCoverComposer>();

// --- Translation (OpenAI primary, DeepL alternate, NoOp fallback) ---------
MadAuthor.Infrastructure.Translation.TranslationDependencyInjection.AddMadAuthorTranslation(builder.Services);

// --- Virus scanning (ClamAV when configured; no-op otherwise) -------------
builder.Services.AddMadAuthorFileScanner(
    clamAvHost: Environment.GetEnvironmentVariable("CLAMAV_HOST"),
    clamAvPort: int.TryParse(Environment.GetEnvironmentVariable("CLAMAV_PORT"), out var clamPort) ? clamPort : 3310);

// --- Email (SMTP - falls back to no-op log if not configured) -------------
builder.Services.AddMadAuthorEmail(new MadAuthor.Infrastructure.Email.SmtpEmailOptions
{
    Host = Environment.GetEnvironmentVariable("SMTP_HOST"),
    Port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var port) ? port : 465,
    Secure = !string.Equals(Environment.GetEnvironmentVariable("SMTP_SECURE"), "false", StringComparison.OrdinalIgnoreCase),
    Username = Environment.GetEnvironmentVariable("SMTP_USER"),
    Password = Environment.GetEnvironmentVariable("SMTP_PASS"),
    FromAddress = Environment.GetEnvironmentVariable("SMTP_FROM_ADDRESS"),
    FromName = Environment.GetEnvironmentVariable("SMTP_FROM_NAME") ?? "MADAuthor",
});

// --- JWT options (bound from env; enforced when auth endpoints are wired) -
var jwtOptions = new JwtOptions
{
    SigningKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY") ?? string.Empty,
    Issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "madauthor",
    Audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "madauthor-web",
    AccessTokenMinutes = int.TryParse(Environment.GetEnvironmentVariable("JWT_ACCESS_TOKEN_MINUTES"), out var a) ? a : 15,
    RefreshTokenDays = int.TryParse(Environment.GetEnvironmentVariable("JWT_REFRESH_TOKEN_DAYS"), out var r) ? r : 14,
};
builder.Services.AddSingleton(jwtOptions);

// /claude operator task system: shared secret the worker + scanner present in the
// `X-Worker-Token` header so they bypass the JWT bearer flow. Generated fresh per
// environment via `[Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLower()`
// and stored in .env as CLAUDE_WORKER_TOKEN. Compared with `CryptographicOperations.FixedTimeEquals`
// in the middleware below to prevent timing attacks. See docs/08-claude-task-system.md section 4.
var claudeWorkerToken = NormalizeSecret(Environment.GetEnvironmentVariable("CLAUDE_WORKER_TOKEN"));
if (string.IsNullOrWhiteSpace(claudeWorkerToken))
{
    Log.Warning("CLAUDE_WORKER_TOKEN is not set - the /claude worker + scanner cannot authenticate. " +
                "Generate one and add to .env before running them.");
}

if (!string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            // Don't remap 'sub' → ClaimTypes.NameIdentifier (Microsoft's legacy default).
            // CurrentUserService reads JwtRegisteredClaimNames.Sub directly; with mapping
            // on, that lookup returns null and every authenticated request 500s in Identify().
            opts.MapInboundClaims = false;
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                ClockSkew = TimeSpan.FromMinutes(2),
            };
            // SignalR's browser client can't set Authorization headers on WebSocket/SSE,
            // so it appends ?access_token=... on hub connections. Read it here so the
            // hub auth succeeds.
            opts.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var accessToken = ctx.Request.Query["access_token"];
                    var path = ctx.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        ctx.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
            };
        });
    builder.Services.AddAuthorization();
}
else
{
    Log.Warning("JWT_SIGNING_KEY is not set - auth endpoints will reject requests. " +
                "Add it to .env or appsettings.Local.json before testing auth.");
}

// Auth services
builder.Services.AddSingleton(new JwtServiceOptions
{
    SigningKey = jwtOptions.SigningKey,
    Issuer = jwtOptions.Issuer,
    Audience = jwtOptions.Audience,
    AccessTokenMinutes = jwtOptions.AccessTokenMinutes,
});
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<MadAuthor.Application.Audit.IAuditService, MadAuthor.Infrastructure.Audit.AuditService>();
builder.Services.AddHostedService<JobProgressBroadcaster>();
builder.Services.AddHostedService<PipelineOrchestrator>();
builder.Services.AddHostedService<ExportRendererService>();

// QuestPDF is free for non-commercial use under the Community license; required to set.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// --- Hangfire (deterministic background jobs - exports, notifications) ----
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(hangfireConnStr, new SqlServerStorageOptions
    {
        PrepareSchemaIfNecessary = true,
        QueuePollInterval = TimeSpan.FromSeconds(5),
    }));
builder.Services.AddHangfireServer();

// --- ASP.NET basics --------------------------------------------------------
// NOTE: we deliberately do NOT register JsonStringEnumConverter globally.
// Project DTOs (BookSummary.Status, WorkflowStage, BookChapter.Status, etc.) serialize
// enums-as-integers, and the SPA's TS types are numeric literal unions with integer-keyed
// label maps (STATUS_LABELS, STAGE_LABELS). The narrow "I'm-a-string-on-the-wire" enums
// (CoverTemplate, CoverSide) carry the [JsonConverter] attribute on the enum itself.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MADAuthor API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "Authorization",
        Scheme = "Bearer",
        BearerFormat = "JWT",
        Type = SecuritySchemeType.Http,
        Description = "Paste the access token returned from /api/auth/login.",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
        }] = []
    });
});
builder.Services.AddSignalR();

// CORS - dev (Angular at localhost:3012) + prod (madauthor.madprospects.com) + any extras
// from CORS_ORIGINS env var (comma-separated). Credentials allowed for the refresh cookie.
var corsOrigins = new List<string>
{
    "http://localhost:3012",
    "https://madauthor.madprospects.com",
};
var extraOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS");
if (!string.IsNullOrWhiteSpace(extraOrigins))
{
    corsOrigins.AddRange(extraOrigins
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
builder.Services.AddCors(o => o.AddPolicy("default", p => p
    .WithOrigins(corsOrigins.Distinct().ToArray())
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// Apply any pending EF migrations on startup. Cheap for our small schema, and removes the
// "deployed but forgot to run dotnet ef database update" failure mode. Logged so it's visible.
using (var scope = app.Services.CreateScope())
{
    var dbCtx = scope.ServiceProvider.GetRequiredService<MadAuthorDbContext>();
    try
    {
        var pending = (await dbCtx.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            Log.Information("Applying {Count} pending EF migration(s): {Names}",
                pending.Count, string.Join(", ", pending));
            await dbCtx.Database.MigrateAsync();
            Log.Information("EF migrations applied.");
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "EF migration step failed - the app will still start, but some features may misbehave.");
    }
}

// Seed roles + publishing platforms on every startup (idempotent).
await DbSeeder.SeedAsync(app.Services);

// JobProgressBroadcaster is registered as a HostedService - see Realtime/.

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

// HTTP method override. Plesk on Windows + IIS reject:
//   1. DELETE / PUT / PATCH at the server level
//   2. POST to URLs ending in a {guid}
//   3. URLs containing HTTP-verb keywords ('delete', 'put', 'patch' anywhere)
//   4. The `X-HTTP-Method-Override` header itself (known attack vector)
// All four fire a static IIS/Plesk 403 HTML page BEFORE our CORS middleware,
// so the browser surfaces them as "No Access-Control-Allow-Origin header".
// The SPA tunnels DELETE/PUT/PATCH as POST with the verb encoded in a single
// non-keyword URL suffix:
//   /_d  → DELETE
//   /_p  → PUT
//   /_h  → PATCH (since 'p' is taken)
// Each suffix is 2 chars, starts with underscore (not a normal route segment),
// has no HTTP-verb keyword in it, isn't a guid - slips past all four IIS/WAF
// filters. This middleware reads the suffix, strips it, and swaps the method
// back before routing, so [HttpDelete] / [HttpPut] / [HttpPatch] endpoints
// match normally.
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsPost(ctx.Request.Method))
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        var (verb, suffixLen) = path switch
        {
            _ when path.EndsWith("/_d", StringComparison.OrdinalIgnoreCase) => (HttpMethods.Delete, 3),
            _ when path.EndsWith("/_p", StringComparison.OrdinalIgnoreCase) => (HttpMethods.Put,    3),
            _ when path.EndsWith("/_h", StringComparison.OrdinalIgnoreCase) => (HttpMethods.Patch,  3),
            _ => (null, 0),
        };
        if (verb is not null)
        {
            ctx.Request.Path = path[..^suffixLen];
            ctx.Request.Method = verb;
        }
    }
    await next();
});

app.UseCors("default");
// Catch everything below this point - including auth + controllers - and surface a JSON body.
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseRouting();

// /claude worker-token bypass. Runs before UseAuthentication so a successful match
// pre-fills ctx.User with a forged Admin/Owner principal; the downstream
// [Authorize(Roles="Admin,Owner")] passes without consulting the JWT bearer scheme.
// Wrong-length or empty headers fall through harmlessly. Constant-time compare per
// docs/08-claude-task-system.md section 4.
if (!string.IsNullOrWhiteSpace(claudeWorkerToken))
{
    var expectedBytes = Encoding.UTF8.GetBytes(claudeWorkerToken);
    app.Use(async (ctx, next) =>
    {
        var presented = NormalizeSecret(ctx.Request.Headers["X-Worker-Token"].ToString());
        if (!string.IsNullOrEmpty(presented))
        {
            var presentedBytes = Encoding.UTF8.GetBytes(presented);
            if (presentedBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes))
            {
                // Forge an admin identity so [Authorize(Roles="Admin,Owner")] passes.
                // The "worker" claim lets controllers tell apart a worker call from
                // a real operator if they need to (e.g. for audit logs).
                var identity = new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.Name, "claude-task-worker"),
                        new Claim(ClaimTypes.NameIdentifier, "claude-task-worker"),
                        new Claim(ClaimTypes.Role, "Admin"),
                        new Claim(ClaimTypes.Role, "Owner"),
                        new Claim("worker", "claude-task-worker"),
                    },
                    authenticationType: "WorkerToken",
                    nameType: ClaimTypes.Name,
                    roleType: ClaimTypes.Role);
                ctx.User = new ClaimsPrincipal(identity);
            }
        }
        await next();
    });
}

if (!string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

// Hangfire dashboard gated by Admin/Owner role (or MADAUTHOR_HANGFIRE_OPEN=true for dev bootstrap).
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireDashboardAuthFilter() },
});

// --- Angular SPA (served when wwwroot has content - i.e. in the Docker image) ---
// In dev, wwwroot is empty and `ng serve` handles the SPA via the proxy. In prod
// the multi-stage Dockerfile copies the Angular build into wwwroot, and these
// middlewares serve it. SPA fallback only catches non-API/hub routes.
var wwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwroot) && File.Exists(Path.Combine(wwwroot, "index.html")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapFallback(async ctx =>
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        // Don't fall through to index.html for API / hub / Hangfire / swagger / health paths.
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hangfire", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(Path.Combine(wwwroot, "index.html"));
    });
}

app.Run();

static string Required(string key)
{
    var value = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Required environment variable '{key}' is not set. Check your .env file.");
    }
    return value;
}

static string NormalizeSecret(string? value) =>
    (value ?? string.Empty).Trim().Trim('"');

static string? NonBlank(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value;

static void EnsureSqlDatabaseExists(string connectionString)
{
    var target = new SqlConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(target.InitialCatalog)) return;

    var databaseName = target.InitialCatalog;
    target.InitialCatalog = "master";

    using var conn = new SqlConnection(target.ConnectionString);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"""
        IF DB_ID(@databaseName) IS NULL
        BEGIN
            DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@databaseName);
            EXEC (@sql);
        END
        """;
    cmd.Parameters.AddWithValue("@databaseName", databaseName);
    cmd.ExecuteNonQuery();
}

static string ComposeConnectionStringFromEnv() =>
    $"Server={Required("DB_HOST")};Database={Required("DB_DATABASE")};" +
    $"User Id={Required("DB_USERNAME")};Password={Required("DB_PASSWORD")};";

static string ComposeHangfireConnectionStringFromEnv() =>
    $"Server={Required("DB_HOST")};Database={Required("DB_HANGFIRE_DATABASE")};" +
    $"User Id={Required("DB_USERNAME")};Password={Required("DB_PASSWORD")};";

static string NormalizeConnectionString(string input)
{
    var b = new SqlConnectionStringBuilder(input)
    {
        // Defaults safe for SQL Server reached by IP/self-signed cert. Overridable by
        // setting these explicitly in the source connection string.
        TrustServerCertificate = true,
        Encrypt = true,
        MultipleActiveResultSets = true,
    };
    return b.ConnectionString;
}
