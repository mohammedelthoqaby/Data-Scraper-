using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(a => a.Reason)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(a => a.TitleAr)
            .HasMaxLength(300);

        builder.Property(a => a.ReasonAr)
            .HasMaxLength(1000);

        builder.Property(a => a.SuggestedAction)
            .HasMaxLength(500);

        builder.Property(a => a.SuggestedActionAr)
            .HasMaxLength(500);

        builder.Property(a => a.IdentityKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(a => a.IdentityKey);
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => a.Severity);
    }
}
