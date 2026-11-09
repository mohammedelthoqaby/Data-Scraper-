using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class CollectorJobConfiguration : IEntityTypeConfiguration<CollectorJob>
{
    public void Configure(EntityTypeBuilder<CollectorJob> builder)
    {
        builder.ToTable("collector_jobs");

        builder.Property(j => j.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(j => j.Source).HasMaxLength(16).IsRequired();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.CompetitorSlug).HasMaxLength(100);
        builder.Property(j => j.Summary).HasMaxLength(1000);

        // Concurrent devices claim jobs: Postgres xmin makes a stale claim fail to save.
        builder.Property(j => j.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Work queue lookups: the next queued job in a group, and a device's active claim.
        builder.HasIndex(j => new { j.Status, j.GroupId, j.Sequence });
        builder.HasIndex(j => new { j.ClaimedByDeviceId, j.Status });
        builder.HasIndex(j => new { j.RunId, j.Kind, j.CompetitorId });
    }
}
