using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pottmayer.UrlShortener.Shortening;

internal sealed class ShortLinkConfiguration : IEntityTypeConfiguration<ShortLink>
{
    public void Configure(EntityTypeBuilder<ShortLink> builder)
    {
        builder.ToTable("short_link");
        builder.HasKey(x => x.Code);

        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(16);
        builder.Property(x => x.LongUrl).HasColumnName("long_url").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}
