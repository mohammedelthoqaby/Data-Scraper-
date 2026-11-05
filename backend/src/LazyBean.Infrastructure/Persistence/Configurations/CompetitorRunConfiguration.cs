using System;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class CompetitorRunConfiguration : IEntityTypeConfiguration<CompetitorRun>
{
    public void Configure(EntityTypeBuilder<CompetitorRun> builder)
    {
        builder.ToTable("competitor_runs");

        builder.Property(r => r.Status)
            .HasConversion(
                v => v.ToString(),
                v => ParseStatus(v))
            .HasMaxLength(20);

        builder.Property(r => r.Coverage)
            .HasConversion(
                v => v.ToString(),
                v => ParseCoverage(v))
            .HasMaxLength(20);

        // One row per competitor per collection run.
        builder.HasIndex(r => new { r.RunId, r.CompetitorId }).IsUnique();
    }

    private static CompetitorRunStatus ParseStatus(string v)
        => Enum.TryParse<CompetitorRunStatus>(v, true, out var parsed) ? parsed : CompetitorRunStatus.Failed;

    private static RunCoverage ParseCoverage(string v)
        => Enum.TryParse<RunCoverage>(v, true, out var parsed) ? parsed : RunCoverage.Complete;
}
