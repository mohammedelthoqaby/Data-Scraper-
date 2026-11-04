using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    public void Configure(EntityTypeBuilder<Variant> builder)
    {
        builder.ToTable("variants");

        builder.Property(v => v.ExternalId).HasMaxLength(200).IsRequired();
        builder.Property(v => v.Title).HasMaxLength(500);
        builder.Property(v => v.Sku).HasMaxLength(100);
        builder.Property(v => v.LastObservedAt);
        builder.Property(v => v.LastSeenRunId);

        builder.HasIndex(v => new { v.ProductId, v.ExternalId }).IsUnique();
    }
}
