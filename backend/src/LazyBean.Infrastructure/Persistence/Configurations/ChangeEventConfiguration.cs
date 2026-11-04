using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class ChangeEventConfiguration : IEntityTypeConfiguration<ChangeEvent>
{
    public void Configure(EntityTypeBuilder<ChangeEvent> builder)
    {
        builder.ToTable("change_events");

        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.OldValue).HasMaxLength(500);
        builder.Property(c => c.NewValue).HasMaxLength(500);

        builder.HasIndex(c => c.RunId);
        builder.HasIndex(c => c.VariantId);
        builder.HasIndex(c => c.AdId);
    }
}
