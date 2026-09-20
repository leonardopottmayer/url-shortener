using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pottmayer.UrlShortener.Kgs;

internal sealed class KeyCounterConfiguration : IEntityTypeConfiguration<KeyCounter>
{
    public void Configure(EntityTypeBuilder<KeyCounter> builder)
    {
        builder.ToTable("key_counter");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Value).HasColumnName("value");
    }
}
