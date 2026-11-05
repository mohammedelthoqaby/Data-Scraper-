using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AiSettingConfiguration : IEntityTypeConfiguration<AiSetting>
{
    public void Configure(EntityTypeBuilder<AiSetting> builder)
    {
        builder.ToTable("ai_settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ModelName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.KeyLastFour)
            .HasMaxLength(10);

        builder.Property(s => s.LastTestMessage)
            .HasMaxLength(500);

        builder.Property(s => s.LastCallStatus)
            .HasMaxLength(100);
    }
}
