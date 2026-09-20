using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pottmayer.Tars.Data.Abstractions.UnitOfWork;
using Pottmayer.Tars.Data.DI;
using Pottmayer.Tars.Data.Relational.DI;
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

// Short codes now come from the KGS (base62 over a buffered counter range), not random generation.
builder.Services.AddHttpClient<IKgsClient, KgsClient>((sp, http) =>
    http.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Kgs:BaseUrl"] ?? "http://localhost:8084"));
builder.Services.AddSingleton<KeyProvider>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "shortening" }));

app.MapPost("/api/urls", async (
    CreateUrlRequest request,
    KeyProvider keys,
    IUnitOfWorkFactory uow,
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

        return true;
    }, cancellationToken: ct);

    if (!created)
        return Results.Conflict(new { error = $"Alias '{code}' is already taken." });

    var publicBaseUrl = cfg["PublicBaseUrl"] ?? "http://localhost:8080";
    return Results.Created($"/api/urls/{code}", new { code, shortUrl = $"{publicBaseUrl}/{code}" });
});

app.Run();

internal sealed record CreateUrlRequest(string LongUrl, string? CustomAlias = null);
