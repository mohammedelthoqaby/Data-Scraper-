using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class CompetitorRunConfiguration : IEntityTypeConfiguration<CompetitorRun>
{
    public void Configure(EntityTypeBuilder<CompetitorRun> builder)
    {
        builder.ToTable("competitor_runs");

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Coverage).HasConversion<string>().HasMaxLength(20);

        // One row per competitor per collection run.
        builder.HasIndex(r => new { r.RunId, r.CompetitorId }).IsUnique();
    }
}
