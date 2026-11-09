using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class CollectorDeviceConfiguration : IEntityTypeConfiguration<CollectorDevice>
{
    public void Configure(EntityTypeBuilder<CollectorDevice> builder)
    {
        builder.ToTable("collector_devices");

        builder.Property(d => d.Name).HasMaxLength(80).IsRequired();
        builder.Property(d => d.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Scopes).HasMaxLength(200).IsRequired();
        builder.Property(d => d.PairedByUsername).HasMaxLength(100).IsRequired();

        // Lookup on every device request: the hash is unique, so one row matches.
        builder.HasIndex(d => d.TokenHash).IsUnique();
        builder.HasIndex(d => d.RevokedAt);
    }
}
