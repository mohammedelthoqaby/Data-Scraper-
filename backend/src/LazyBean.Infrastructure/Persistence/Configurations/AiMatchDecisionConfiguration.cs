using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AiMatchDecisionConfiguration : IEntityTypeConfiguration<AiMatchDecision>
{
    public void Configure(EntityTypeBuilder<AiMatchDecision> builder)
    {
        builder.ToTable("ai_match_decisions");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.PairHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(d => d.PairHash);

        builder.HasIndex(d => new { d.CompetitorVariantId, d.ReferenceVariantId });

        builder.Property(d => d.Verdict).HasMaxLength(20).IsRequired();
        builder.Property(d => d.Confidence).HasPrecision(5, 4);
        builder.Property(d => d.Reason).HasMaxLength(500);
        builder.Property(d => d.GuardrailReason).HasMaxLength(300);
        builder.Property(d => d.Provider).HasMaxLength(20).IsRequired();
        builder.Property(d => d.ModelName).HasMaxLength(100);
    }
}
