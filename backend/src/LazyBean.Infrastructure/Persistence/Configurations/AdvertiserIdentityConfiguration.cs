using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AdvertiserIdentityConfiguration : IEntityTypeConfiguration<AdvertiserIdentity>
{
    public void Configure(EntityTypeBuilder<AdvertiserIdentity> builder)
    {
        builder.ToTable("advertiser_identities");

        builder.Property(a => a.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.ExternalId).HasMaxLength(200);
        builder.Property(a => a.Name).HasMaxLength(300);
        builder.Property(a => a.LibraryUrl).HasMaxLength(1000);
        builder.Property(a => a.OwnershipEvidenceUrl).HasMaxLength(1000);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        builder.HasIndex(a => a.CompetitorId);
        builder.HasIndex(a => new { a.Platform, a.ExternalId }).IsUnique();
        builder.HasIndex(a => a.Status);
    }
}
