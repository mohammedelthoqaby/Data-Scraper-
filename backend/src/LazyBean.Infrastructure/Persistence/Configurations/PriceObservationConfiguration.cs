using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class PriceObservationConfiguration : IEntityTypeConfiguration<PriceObservation>
{
    public void Configure(EntityTypeBuilder<PriceObservation> builder)
    {
        builder.ToTable("price_observations");

        builder.Property(p => p.Price).HasPrecision(12, 2);
        builder.Property(p => p.CompareAtPrice).HasPrecision(12, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();

        // Append-only history: every observation is a new row, never an UPDATE of a past price.
        builder.HasIndex(p => new { p.VariantId, p.ObservedAt });

        // One observation per variant per run - the orchestrator upserts within a run
        // rather than appending duplicates if a page is re-fetched after a retry.
        builder.HasIndex(p => new { p.VariantId, p.RunId }).IsUnique();
    }
}
