using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class CompetitorConfiguration : IEntityTypeConfiguration<Competitor>
{
    public void Configure(EntityTypeBuilder<Competitor> builder)
    {
        builder.ToTable("competitors");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Key).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Key).IsUnique();

        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.BaseUrl).HasMaxLength(500).IsRequired();
        builder.Property(c => c.Platform).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.IsReference).HasDefaultValue(false);
        builder.Property(c => c.CollectionHost).HasMaxLength(32).HasDefaultValue("server");
    }
}
