using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AdIngestBatchConfiguration : IEntityTypeConfiguration<AdIngestBatch>
{
    public void Configure(EntityTypeBuilder<AdIngestBatch> builder)
    {
        builder.ToTable("ad_ingest_batches");

        builder.Property(b => b.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.Status).HasMaxLength(20);
        builder.Property(b => b.Error).HasMaxLength(500);

        builder.HasIndex(b => new { b.IdentityId, b.ReceivedAt });
        builder.HasIndex(b => b.CompetitorId);
    }
}
