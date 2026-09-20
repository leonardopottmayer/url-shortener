using Microsoft.EntityFrameworkCore;
using Pottmayer.Tars.Data.Relational;

namespace Pottmayer.UrlShortener.Redirect;

internal sealed class RedirectDbContext(DbContextOptions<RedirectDbContext> options)
    : RelationalDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RedirectDbContext).Assembly);
    }
}
