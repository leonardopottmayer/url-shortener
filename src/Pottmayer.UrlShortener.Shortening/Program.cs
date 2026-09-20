using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pottmayer.Tars.Caching.DI;
using Pottmayer.Tars.Caching.Redis.DI;
using Pottmayer.Tars.Caching.Redis.Options;
using Pottmayer.Tars.Data.Abstractions.Keys;
using Pottmayer.Tars.Data.Abstractions.UnitOfWork;
using Pottmayer.Tars.Data.DI;
using Pottmayer.Tars.Data.Relational.DI;
using Pottmayer.Tars.Messaging.Abstractions;
using Pottmayer.Tars.Messaging.Broker.DI;
using Pottmayer.Tars.Messaging.EntityFrameworkCore.DI;
using Pottmayer.Tars.Messaging.MassTransit.Kafka.DI;
using Pottmayer.UrlShortener.Contracts;
using Pottmayer.UrlShortener.Shortening;

var builder = WebApplication.CreateBuilder(args);

// Tars data pipeline — single database, so the keyless ("default") registration.
builder.Services.AddTarsDataContextAccessor();
builder.Services.AddTarsRelationalConfigurationConnectionResolver();
builder.Services.AddTarsDataContextFactory();
builder.Services.AddTarsUnitOfWorkFactory();
builder.Services.AddTarsRelationalData<ShorteningDbContext>((_, descriptor) =>
    new DbContextOptionsBuilder<ShorteningDbContext>()
        .UseNpgsql(descriptor.ConnectionString)
        .Options);
builder.Services.AddTarsDataRepositoriesFromAssemblies(typeof(Program).Assembly);

// Redis holds the KGS buffer cursor so a restart resumes the range instead of leaving gaps.
builder.AddTarsRedisCachingOptions();
builder.Services.AddTarsCacheKeyBuilder<RedisCachingOptions>();
builder.Services.AddTarsCacheSerializer();
builder.Services.AddTarsRedisConnectionMultiplexer();
builder.Services.AddTarsRedisDatabase();
builder.Services.AddTarsRedisCacheProvider();

// Short codes now come from the KGS (base62 over a buffered counter range), not random generation.
builder.Services.AddHttpClient<IKgsClient, KgsClient>((sp, http) =>
    http.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Kgs:BaseUrl"] ?? "http://localhost:8084"));
builder.Services.AddSingleton<KeyProvider>();

// Kafka transport (in-memory host bus carries the rider) — publishes UrlCreated.
builder.AddTarsMassTransitKafka(configure: o =>
{
    o.Messaging.EndpointName = "shortening";
    o.Messaging.RegisterEventsFromAssembly(typeof(UrlCreated).Assembly);
});

// Transactional outbox -> Kafka: PublishAsync writes an OutboxMessage in the link's transaction, and
// the relay drains it to the keyed "events" Kafka bus (the one clean path to a Kafka outbox in tars).
builder.Services.AddSingleton(TimeProvider.System);   // outbox bus + relay depend on it
builder.AddTarsOutboxOptions();
builder.Services.AddTarsIntegrationEventSerializer();
builder.Services.AddTarsOutboxBus();     // replaces the default bus so PublishAsync -> outbox row
builder.Services.AddTarsOutboxStore();
builder.Services.AddTarsKeyedKafkaIntegrationEventBus("events");  // after AddTarsOutboxBus's RemoveAll
builder.Services.AddTarsOutboxBrokerDelivery("events");
builder.Services.AddTarsOutboxRelay(DataKeys.Default);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "shortening" }));

app.MapPost("/api/urls", async (
    CreateUrlRequest request,
    KeyProvider keys,
    IUnitOfWorkFactory uow,
    IIntegrationEventBus bus,
    IConfiguration cfg,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.LongUrl)
        || !Uri.TryCreate(request.LongUrl, UriKind.Absolute, out _))
    {
        return Results.BadRequest(new { error = "longUrl must be an absolute URL." });
    }

    string code;
    if (request.CustomAlias is { Length: > 0 } alias)
    {
        if (!Regex.IsMatch(alias, "^[0-9A-Za-z]{3,16}$"))
            return Results.BadRequest(new { error = "customAlias must be 3-16 chars of [0-9A-Za-z]." });
        code = alias;
    }
    else
    {
        code = Base62.Encode(await keys.NextAsync(ct));
    }

    var created = await uow.ExecuteAsync(async (ctx, token) =>
    {
        var repo = ctx.AcquireRepository<IShortLinkRepository>();

        // A generated code never collides (the KGS counter only moves forward); a custom alias can.
        if (await repo.ExistsKeyAsync(code, token))
            return false;

        await repo.AddAsync(new ShortLink
        {
            Code = code,
            LongUrl = request.LongUrl,
            CreatedAt = DateTimeOffset.UtcNow,
        }, token);

        // Written into THIS transaction as an outbox row; the relay delivers it to Kafka after commit.
        await bus.PublishAsync(new UrlCreated(Guid.NewGuid(), DateTimeOffset.UtcNow, code, request.LongUrl), token);

        return true;
    }, cancellationToken: ct);

    if (!created)
        return Results.Conflict(new { error = $"Alias '{code}' is already taken." });

    var publicBaseUrl = cfg["PublicBaseUrl"] ?? "http://localhost:8080";
    return Results.Created($"/api/urls/{code}", new { code, shortUrl = $"{publicBaseUrl}/{code}" });
});

app.Run();

internal sealed record CreateUrlRequest(string LongUrl, string? CustomAlias = null);
