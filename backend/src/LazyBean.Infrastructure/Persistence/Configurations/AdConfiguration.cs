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
        builder.Property(a => a.RawDataJson).HasColumnType("jsonb");
        builder.Property(a => a.MissingCount).HasDefaultValue(0);
        builder.Property(a => a.ProductId).IsRequired(false);

        builder.HasIndex(a => a.CompetitorId);
        builder.HasIndex(a => a.IdentityId);
        builder.HasIndex(a => a.CampaignId);
        builder.HasIndex(a => a.ProductId);
        builder.HasIndex(a => new { a.Platform, a.ExternalAdId }).IsUnique();
        builder.HasIndex(a => a.Status);
    }
}
