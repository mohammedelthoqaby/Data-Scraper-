using System.Threading.RateLimiting;
using LazyBean.Api.Middleware;
using LazyBean.Application;
using LazyBean.Infrastructure;
using LazyBean.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var wwwroot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwroot))
{
    builder.Environment.WebRootPath = wwwroot;
    builder.Environment.WebRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wwwroot);
}

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .MinimumLevel.Override("Microsoft.AspNetCore.StaticFiles", Serilog.Events.LogEventLevel.Debug)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Clean-architecture composition: each layer registers its own services.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Trust X-Forwarded-For/-Proto only from known proxy networks (default: Docker's private
// ranges + loopback for local testing). Configure via ForwardedHeaders:TrustedNetworks.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    var trustedNetworks = builder.Configuration.GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>()
        ?? ["127.0.0.1/32", "::1/128", "172.28.5.0/24"];

    foreach (var cidr in trustedNetworks)
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
    }
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Name = "lazybean.auth";
        // Dev runs plain HTTP on localhost; everywhere else, never send the auth cookie over HTTP.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        // API, not a page-based app: return plain status codes instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers(options =>
{
    // [Authorize] by default on every action; endpoints opt out with [AllowAnonymous]
    // (login, logout) or by not being an MVC action at all (/health).
    options.Filters.Add(new AuthorizeFilter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "LazyBean API",
        Version = "v1",
        Description = "Competitor price-intelligence backend (Clean Architecture: Domain / Application / Infrastructure / API)."
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Per-client-IP fixed window; protects write-heavy and read endpoints alike. After
    // UseForwardedHeaders (first in the pipeline) rewrites Connection.RemoteIpAddress from a
    // trusted X-Forwarded-For, this partitions on the real client IP, not the proxy's.
    options.AddPolicy("api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 100,
                QueueLimit = 0
            }));

    // Stricter policy for the login endpoint - slows down credential-guessing.
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 5,
                QueueLimit = 0
            }));

    // Policy for internal endpoints (scheduled runs, ads ingestion)
    options.AddPolicy("internal", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 60,
                QueueLimit = 0
            }));

    options.OnRejected = async (context, ct) =>
    {
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)Math.Ceiling(retryAfter.TotalSeconds)
            : 60;

        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsync(
            $$"""{"status":429,"title":"Too many requests. Please slow down.","retryAfterSeconds":{{retryAfterSeconds}}}""",
            ct);
    };
});

var app = builder.Build();

var redisConfigured = !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Redis"));
app.Logger.LogInformation(
    "Cache mode: {Mode}",
    redisConfigured ? "hybrid (in-process L1 + Redis L2)" : "memory-only (no Redis connection string configured)");

app.Logger.LogInformation(
    "WebRootPath: {WebRootPath}, WebRootExists: {Exists}",
    app.Environment.WebRootPath,
    Directory.Exists(app.Environment.WebRootPath ?? ""));

var testFile = app.Environment.WebRootFileProvider.GetFileInfo("logo.svg");
app.Logger.LogInformation("Test logo.svg Exists: {Exists}, PhysicalPath: {PhysicalPath}", testFile.Exists, testFile.PhysicalPath);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();

    var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
    app.Logger.LogInformation("Database applied migrations ({Count}): {Migrations}",
        appliedMigrations.Count, string.Join(", ", appliedMigrations));

    var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();
    if (pendingMigrations.Count > 0)
    {
        app.Logger.LogInformation(
            "Applying {Count} pending database migration(s): {Migrations}",
            pendingMigrations.Count, string.Join(", ", pendingMigrations));
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("Database migrations applied successfully.");
    }
    else
    {
        app.Logger.LogInformation("Database is up to date (no pending migrations).");
    }
}

await DbInitializer.SeedAdminUserAsync(app.Services);
await DbInitializer.SeedCompetitorsAsync(app.Services);
await DbInitializer.RecoverInterruptedRunsAsync(app.Services);

// Pipeline order (first to last):
// 1. UseForwardedHeaders        - rewrites scheme/remote-IP from X-Forwarded-*, trusted
//                                  proxies/networks only (see ForwardedHeadersOptions above).
//                                  MUST run before anything that reads the client IP or scheme
//                                  (rate limiter, HTTPS redirection, logging).
// 2. CorrelationIdMiddleware    - assigns/propagates X-Correlation-Id, pushes it into Serilog's LogContext
// 3. ExceptionHandlingMiddleware - catches everything downstream, shapes problem+json responses
// 4. Swagger / SwaggerUI        - Development only
// 5. Serilog request logging    - one structured log line per request, tagged with the correlation id
// 6. HTTPS redirection           - configurable; disabled when TLS is terminated upstream (reverse proxy)
// 7. Rate limiter                - per-IP fixed window, 429 + Retry-After on rejection
// 8. Authentication               - reads the auth cookie
// 9. Authorization                - [Authorize]-by-default via the global AuthorizeFilter
// 10. MVC controller routing / health check endpoint
app.UseMiddleware<NetlifyForwardedHeadersMiddleware>();
app.UseForwardedHeaders();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<MemoryMonitoringMiddleware>();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "LazyBean API v1"));
}

var staticFileOptions = new StaticFileOptions
{
    FileProvider = app.Environment.WebRootFileProvider,
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/_astro/", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
        else if (path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase) || path == "/" || string.IsNullOrEmpty(path))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";
        }
    }
};

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = app.Environment.WebRootFileProvider
});
app.UseStaticFiles(staticFileOptions);

app.UseRouting();

app.UseSerilogRequestLogging();

// Off by default behind a reverse proxy that already terminates TLS (e.g. the nginx
// container in docker-compose) - turning it on there would incorrectly redirect the
// proxy's plain-HTTP-to-the-container traffic. Set Security:UseHttpsRedirection=true
// only when Kestrel itself is internet-facing and terminating TLS.
if (builder.Configuration.GetValue("Security:UseHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Anonymous by design (not an MVC action, so the global AuthorizeFilter never applies) and
// unrated (no [EnableRateLimiting] attached) - monitoring/orchestrators must always reach it.
app.MapHealthChecks("/health");

app.MapFallbackToFile("{*path:regex(^(?!api|health|swagger).*$)}", "index.html", staticFileOptions);

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var curProc = System.Diagnostics.Process.GetCurrentProcess();

lifetime.ApplicationStarted.Register(() =>
{
    var wsMb = curProc.WorkingSet64 / (1024.0 * 1024.0);
    app.Logger.LogInformation(
        "Application started. Process ID: {Pid}, Machine: {Machine}, WorkingSet: {WsMb:F1} MB, 64-bit: {Is64Bit}",
        curProc.Id, Environment.MachineName, wsMb, Environment.Is64BitProcess);
});

lifetime.ApplicationStopping.Register(() =>
{
    var wsMb = curProc.WorkingSet64 / (1024.0 * 1024.0);
    app.Logger.LogWarning(
        "Application stopping (shutdown/recycle triggered). WorkingSet: {WsMb:F1} MB, ActiveRun: {ActiveRun}",
        wsMb, LazyBean.Infrastructure.Collection.Orchestration.CollectionRunBackgroundService.IsRunActive);
});

lifetime.ApplicationStopped.Register(() =>
{
    app.Logger.LogInformation("Application stopped cleanly.");
});

app.Run();

public partial class Program;
