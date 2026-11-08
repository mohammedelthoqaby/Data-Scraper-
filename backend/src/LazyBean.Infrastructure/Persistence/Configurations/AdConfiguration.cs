using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AdConfiguration : IEntityTypeConfiguration<Ad>
{
    public void Configure(EntityTypeBuilder<Ad> builder)
    {
        builder.ToTable("ads");

        builder.Property(a => a.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.ExternalAdId).HasMaxLength(200);
        builder.Property(a => a.Title).HasMaxLength(500);
        builder.Property(a => a.Body).HasMaxLength(4000);
        builder.Property(a => a.LinkUrl).HasMaxLength(2000);
        builder.Property(a => a.SnapshotUrl).HasMaxLength(2000);
        builder.Property(a => a.CreativeHash).HasMaxLength(100);
        builder.Property(a => a.PreviewUrl).HasMaxLength(2000);
        builder.Property(a => a.CreativeFormat).HasMaxLength(20);
        builder.Property(a => a.AdvertisedPrice).HasPrecision(12, 2);
        builder.Property(a => a.LinkMethod).HasMaxLength(20);
        builder.Property(a => a.LinkVersion).HasDefaultValue(0);
        builder.Property(a => a.LinkReason).HasMaxLength(40);
        builder.Property(a => a.Placements).HasMaxLength(200);
        builder.Property(a => a.Headline).HasMaxLength(500);
        builder.Property(a => a.Description).HasMaxLength(4000);
        builder.Property(a => a.PreviewVariationsJson);
        builder.Property(a => a.AiReadName).HasMaxLength(300);
        builder.Property(a => a.AiReadPrice).HasPrecision(12, 2);
        builder.Property(a => a.RawDataJson).HasColumnType("jsonb");
        builder.Property(a => a.MissingCount).HasDefaultValue(0);
        builder.Property(a => a.ProductId).IsRequired(false);

        builder.HasIndex(a => a.CompetitorId);
        builder.HasIndex(a => a.IdentityId);
        builder.HasIndex(a => a.CampaignId);
        builder.HasIndex(a => a.ProductId);
        // A creative is keyed by its advertiser account and its creative ID. The same creative shown under two
        // accounts is two rows, one per account, so neither account loses it.
        builder.HasIndex(a => new { a.Platform, a.IdentityId, a.ExternalAdId }).IsUnique();
        builder.HasIndex(a => a.Status);
    }
}
