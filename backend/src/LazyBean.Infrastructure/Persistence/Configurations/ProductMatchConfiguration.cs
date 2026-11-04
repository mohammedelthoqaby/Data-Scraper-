using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class ProductMatchConfiguration : IEntityTypeConfiguration<ProductMatch>
{
    public void Configure(EntityTypeBuilder<ProductMatch> builder)
    {
        builder.ToTable("product_matches");

        builder.Property(m => m.Score).HasPrecision(5, 4);
        builder.Property(m => m.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(m => new { m.CompetitorVariantId, m.ReferenceVariantId }).IsUnique();
    }
}
