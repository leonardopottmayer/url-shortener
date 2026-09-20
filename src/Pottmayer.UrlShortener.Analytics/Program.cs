using Pottmayer.Tars.Data.Abstractions.UnitOfWork;
using Pottmayer.Tars.Data.DI;
using Pottmayer.Tars.Data.Document.MongoDB.DI;
using Pottmayer.Tars.Messaging.MassTransit.Kafka.DI;
using Pottmayer.UrlShortener.Analytics;
using Pottmayer.UrlShortener.Contracts;

using Pottmayer.UrlShortener.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddUrlShortenerObservability();

// Tars data pipeline — MongoDB (single database → keyless "default").
builder.Services.AddTarsDataContextAccessor();
builder.Services.AddTarsDataContextFactory();
builder.Services.AddTarsUnitOfWorkFactory();
builder.Services.AddTarsMongoConfigurationConnectionResolver();
builder.Services.AddTarsMongoData();
builder.Services.AddTarsDataRepositoriesFromAssemblies(typeof(Program).Assembly);

// Kafka consumer — subscribes to UrlAccessed and dispatches to UrlAccessedHandler.
builder.AddTarsMassTransitKafka(configure: o =>
{
    o.Messaging.EndpointName = "analytics";
    o.Messaging.RegisterEventsFromAssembly(typeof(UrlAccessed).Assembly);
    o.Messaging.RegisterHandlersFromAssembly(typeof(UrlAccessedHandler).Assembly);
    o.Messaging.Subscribe<UrlAccessed>();
});

var app = builder.Build();

app.UseUrlShortenerObservability();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "analytics" }));

app.MapGet("/api/stats/{code}", async (string code, IUnitOfWorkFactory uow, CancellationToken ct) =>
{
    var totalClicks = await uow.ExecuteAsync(
        async (ctx, token) => await ctx.AcquireRepository<IAccessEventRepository>().CountAsync(e => e.Code == code, token),
        options: new UnitOfWorkOptions { CommitOnSuccess = false },
        cancellationToken: ct);

    return Results.Ok(new { code, totalClicks });
});

app.Run();
