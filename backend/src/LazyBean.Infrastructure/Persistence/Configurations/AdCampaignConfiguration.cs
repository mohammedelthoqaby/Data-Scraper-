using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AdCampaignConfiguration : IEntityTypeConfiguration<AdCampaign>
{
    public void Configure(EntityTypeBuilder<AdCampaign> builder)
    {
        builder.ToTable("ad_campaigns");

        builder.Property(c => c.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.CreativeHash).HasMaxLength(100);

        builder.HasIndex(c => c.CompetitorId);
        builder.HasIndex(c => c.IdentityId);
        builder.HasIndex(c => new { c.IdentityId, c.CreativeHash });
    }
}
