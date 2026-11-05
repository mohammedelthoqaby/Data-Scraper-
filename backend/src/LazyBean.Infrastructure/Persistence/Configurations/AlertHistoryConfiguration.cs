using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AlertHistoryConfiguration : IEntityTypeConfiguration<AlertHistory>
{
    public void Configure(EntityTypeBuilder<AlertHistory> builder)
    {
        builder.ToTable("alert_histories");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Action)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(h => h.Note)
            .HasMaxLength(1000);

        builder.Property(h => h.PerformedBy)
            .HasMaxLength(100);

        builder.HasIndex(h => h.AlertId);
    }
}
