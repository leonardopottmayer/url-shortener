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

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "shortening" }));

app.MapPost("/api/urls", async (CreateUrlRequest request, IUnitOfWorkFactory uow, IConfiguration cfg, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.LongUrl)
        || !Uri.TryCreate(request.LongUrl, UriKind.Absolute, out _))
    {
        return Results.BadRequest(new { error = "longUrl must be an absolute URL." });
    }

    var code = await uow.ExecuteAsync(async (ctx, token) =>
    {
        var repo = ctx.AcquireRepository<IShortLinkRepository>();

        string candidate;
        do { candidate = ShortCode.NewCode(); }
        while (await repo.ExistsKeyAsync(candidate, token));

        await repo.AddAsync(new ShortLink
        {
            Code = candidate,
            LongUrl = request.LongUrl,
            CreatedAt = DateTimeOffset.UtcNow,
        }, token);

        return candidate;
    }, cancellationToken: ct);

    var publicBaseUrl = cfg["PublicBaseUrl"] ?? "http://localhost:8080";
    return Results.Created($"/api/urls/{code}", new { code, shortUrl = $"{publicBaseUrl}/{code}" });
});

app.Run();

internal sealed record CreateUrlRequest(string LongUrl);
