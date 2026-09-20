using Microsoft.EntityFrameworkCore;
using Pottmayer.Tars.Data.Relational;
using Pottmayer.Tars.Messaging.EntityFrameworkCore.Outbox;

namespace Pottmayer.UrlShortener.Shortening;

internal sealed class ShorteningDbContext(DbContextOptions<ShorteningDbContext> options)
    : RelationalDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShorteningDbContext).Assembly);

        // The transactional outbox lives in this context so UrlCreated rows join the link's own
        // transaction (public schema).
        modelBuilder.AddTarsOutbox();
    }
}
