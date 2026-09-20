using Microsoft.EntityFrameworkCore;
using Pottmayer.Tars.Data.Relational;

namespace Pottmayer.UrlShortener.Kgs;

internal sealed class KgsDbContext(DbContextOptions<KgsDbContext> options)
    : RelationalDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KgsDbContext).Assembly);
    }
}
