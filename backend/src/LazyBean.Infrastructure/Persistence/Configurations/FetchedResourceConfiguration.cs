using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class FetchedResourceConfiguration : IEntityTypeConfiguration<FetchedResource>
{
    public void Configure(EntityTypeBuilder<FetchedResource> builder)
    {
        builder.ToTable("fetched_resources");

        builder.Property(f => f.Url).HasMaxLength(2000).IsRequired();
        builder.Property(f => f.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(f => f.StoragePath).HasMaxLength(1000).IsRequired();

        builder.HasIndex(f => f.RunId);
        builder.HasIndex(f => f.CompetitorId);
    }
}
