using Microsoft.EntityFrameworkCore;
using Pottmayer.Tars.Caching.Abstractions;
using Pottmayer.Tars.Caching.DI;
using Pottmayer.Tars.Caching.Redis.DI;
using Pottmayer.Tars.Caching.Redis.Options;
using Pottmayer.Tars.Data.Abstractions.UnitOfWork;
using Pottmayer.Tars.Data.DI;
using Pottmayer.Tars.Data.Relational.DI;
using Pottmayer.Tars.Messaging.Abstractions;
using Pottmayer.Tars.Messaging.MassTransit.Kafka.DI;
using Pottmayer.UrlShortener.Contracts;
using Pottmayer.UrlShortener.Redirect;

var builder = WebApplication.CreateBuilder(args);

// Tars data pipeline — reads the shortening database directly (single database → keyless).
builder.Services.AddTarsDataContextAccessor();
builder.Services.AddTarsRelationalConfigurationConnectionResolver();
builder.Services.AddTarsDataContextFactory();
builder.Services.AddTarsUnitOfWorkFactory();
builder.Services.AddTarsRelationalData<RedirectDbContext>((_, descriptor) =>
    new DbContextOptionsBuilder<RedirectDbContext>()
        .UseNpgsql(descriptor.ConnectionString)
        .Options);
builder.Services.AddTarsDataRepositoriesFromAssemblies(typeof(Program).Assembly);

// Redis cache for the hot path (code -> longUrl).
builder.AddTarsRedisCachingOptions();
builder.Services.AddTarsCacheKeyBuilder<RedisCachingOptions>();
builder.Services.AddTarsCacheSerializer();
builder.Services.AddTarsRedisConnectionMultiplexer();
builder.Services.AddTarsRedisDatabase();
builder.Services.AddTarsRedisCacheProvider();

builder.Services.AddSingleton<CacheMetrics>();

// Kafka transport — publishes UrlAccessed (best-effort, no outbox).
builder.AddTarsMassTransitKafka(configure: o =>
{
    o.Messaging.EndpointName = "redirect";
    o.Messaging.RegisterEventsFromAssembly(typeof(UrlAccessed).Assembly);
});

var app = builder.Build();

var positive = new CacheEntryOptions(AbsoluteExpirationRelativeToNow: TimeSpan.FromHours(24));
var negative = new CacheEntryOptions(AbsoluteExpirationRelativeToNow: TimeSpan.FromSeconds(60));

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "redirect" }));

app.MapGet("/internal/cache-stats", (CacheMetrics metrics) =>
{
    var (hits, misses) = metrics.Snapshot();
    var total = hits + misses;
    return Results.Ok(new
    {
        hits,
        misses,
        ratio = total == 0 ? 0d : Math.Round((double)hits / total, 4),
    });
});

app.MapGet("/{code}", async (
    string code,
    ICacheStore cache,
    CacheMetrics metrics,
    IUnitOfWorkFactory uow,
    IIntegrationEventBus bus,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    // Cache-aside: try Redis first (a negative entry is still a hit — it spares Postgres).
    string? target;
    var cached = await cache.TryGetAsync<LinkCacheEntry>(code, ct);
    if (cached.Found)
    {
        metrics.Hit();
        target = cached.Value!.LongUrl;
    }
    else
    {
        metrics.Miss();
        var link = await uow.ExecuteAsync(
            async (ctx, token) => await ctx.AcquireRepository<IShortLinkRepository>().GetByIdAsync(code, token),
            options: new UnitOfWorkOptions { CommitOnSuccess = false },
            cancellationToken: ct);

        if (link is null)
        {
            await cache.SetAsync(code, new LinkCacheEntry(null), negative, ct);
            target = null;
        }
        else
        {
            await cache.SetAsync(code, new LinkCacheEntry(link.LongUrl), positive, ct);
            target = link.LongUrl;
        }
    }

    if (target is null)
        return Results.NotFound();

    // Best-effort click event (design.md §8): never block or fail the redirect on a Kafka hiccup.
    try
    {
        await bus.PublishAsync(new UrlAccessed(Guid.NewGuid(), DateTimeOffset.UtcNow, code), ct);
    }
    catch (Exception ex)
    {
        loggerFactory.CreateLogger("Redirect").LogWarning(ex, "Failed to publish UrlAccessed for {Code}", code);
    }

    // 302 (not 301) on purpose: keeps the browser hitting us so analytics keeps counting (design.md §7).
    return Results.Redirect(target, permanent: false);
});

app.Run();
