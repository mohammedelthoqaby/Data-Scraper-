using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.Property(p => p.ExternalId).HasMaxLength(200).IsRequired();
        builder.Property(p => p.CanonicalUrl).HasMaxLength(1000).IsRequired();
        builder.Property(p => p.ImageUrl).HasMaxLength(1000);
        builder.Property(p => p.Title).HasMaxLength(500).IsRequired();
        builder.Property(p => p.NormalizedTitle).HasMaxLength(500).IsRequired();
        builder.Property(p => p.Category).HasMaxLength(100);
        builder.Property(p => p.ProductType).HasMaxLength(200);
        builder.Property(p => p.VendorId).HasMaxLength(100);
        builder.Property(p => p.Vendor).HasMaxLength(200);
        builder.Property(p => p.Brand).HasMaxLength(200);
        builder.Property(p => p.Tags).HasMaxLength(2000);
        builder.Property(p => p.CanonicalCategory).HasConversion<string>().HasMaxLength(40);
        builder.Property(p => p.ClassificationConfidence).HasColumnType("decimal(4,2)");
        builder.Property(p => p.MatchedRule).HasMaxLength(200);
        builder.Property(p => p.Roast).HasMaxLength(100);
        builder.Property(p => p.Origin).HasMaxLength(100);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Handle).HasMaxLength(500);

        // One product per (competitor, external id) - the adapter's own product identity.
        builder.HasIndex(p => new { p.CompetitorId, p.ExternalId }).IsUnique();
        builder.HasIndex(p => new { p.CompetitorId, p.Handle });
    }
}
