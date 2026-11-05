using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AiReviewTriageCacheConfiguration : IEntityTypeConfiguration<AiReviewTriageCache>
{
    public void Configure(EntityTypeBuilder<AiReviewTriageCache> builder)
    {
        builder.ToTable("ai_review_triage_cache");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.PairHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(c => c.PairHash)
            .IsUnique();

        builder.Property(c => c.Verdict)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(c => c.Confidence)
            .HasPrecision(5, 4);

        builder.Property(c => c.Reason)
            .HasMaxLength(1000);

        builder.Property(c => c.ModelName)
            .HasMaxLength(100);
    }
}
