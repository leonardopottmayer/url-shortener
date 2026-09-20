using Microsoft.EntityFrameworkCore;
using Pottmayer.Tars.Data.Abstractions.UnitOfWork;
using Pottmayer.Tars.Data.DI;
using Pottmayer.Tars.Data.Relational.DI;
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

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "redirect" }));

app.MapGet("/{code}", async (string code, IUnitOfWorkFactory uow, CancellationToken ct) =>
{
    var link = await uow.ExecuteAsync(
        async (ctx, token) => await ctx.AcquireRepository<IShortLinkRepository>().GetByIdAsync(code, token),
        options: new UnitOfWorkOptions { CommitOnSuccess = false },
        cancellationToken: ct);

    // 302 (not 301) on purpose: keeps the browser hitting us so analytics keeps counting (design.md §7).
    return link is null ? Results.NotFound() : Results.Redirect(link.LongUrl, permanent: false);
});

app.Run();
