using System;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class CollectionRunConfiguration : IEntityTypeConfiguration<CollectionRun>
{
    public void Configure(EntityTypeBuilder<CollectionRun> builder)
    {
        builder.ToTable("collection_runs");

        builder.Property(r => r.Trigger)
            .HasConversion(
                v => v.ToString(),
                v => ParseTrigger(v))
            .HasMaxLength(20);

        builder.Property(r => r.Status)
            .HasConversion(
                v => v.ToString(),
                v => ParseStatus(v))
            .HasMaxLength(20);

        builder.Property(r => r.TargetCompetitorIdsJson).HasMaxLength(2000);
        builder.Property(r => r.PhasesJson).HasColumnType("text");
        builder.Property(r => r.MatchesCount);
    }

    private static RunTrigger ParseTrigger(string v)
        => Enum.TryParse<RunTrigger>(v, true, out var parsed) ? parsed : RunTrigger.Manual;

    private static CollectionRunStatus ParseStatus(string v)
        => Enum.TryParse<CollectionRunStatus>(v, true, out var parsed) ? parsed : CollectionRunStatus.Failed;
}
