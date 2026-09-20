using Microsoft.EntityFrameworkCore;
using Pottmayer.Tars.Data.Abstractions.UnitOfWork;
using Pottmayer.Tars.Data.DI;
using Pottmayer.Tars.Data.Relational.DI;
using Pottmayer.UrlShortener.Kgs;

using Pottmayer.UrlShortener.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddUrlShortenerObservability();

// Tars data pipeline — single database (the kgs counter).
builder.Services.AddTarsDataContextAccessor();
builder.Services.AddTarsRelationalConfigurationConnectionResolver();
builder.Services.AddTarsDataContextFactory();
builder.Services.AddTarsUnitOfWorkFactory();
builder.Services.AddTarsRelationalData<KgsDbContext>((_, descriptor) =>
    new DbContextOptionsBuilder<KgsDbContext>()
        .UseNpgsql(descriptor.ConnectionString)
        .Options);
builder.Services.AddTarsDataRepositoriesFromAssemblies(typeof(Program).Assembly);

var app = builder.Build();

app.UseUrlShortenerObservability();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "kgs" }));

// Hands out a contiguous [rangeStart, rangeEnd] block of counter values. The caller (Shortening)
// buffers the block locally and base62-encodes each number into a short code.
app.MapPost("/api/keys/allocate", async (AllocateRequest request, IUnitOfWorkFactory uow, CancellationToken ct) =>
{
    var size = request.Size is > 0 and <= 100_000 ? request.Size : 1_000;

    var high = await uow.ExecuteAsync(
        async (ctx, token) => await ctx.AcquireRepository<IKeyRangeRepository>().AllocateAsync(size, token),
        cancellationToken: ct);

    return Results.Ok(new { rangeStart = high - size + 1, rangeEnd = high });
});

app.Run();

internal sealed record AllocateRequest(int Size);
