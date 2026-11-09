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

        // Defaults let existing rows migrate cleanly: the primary is Groq and the triage cap is 200.
        builder.Property(s => s.PrimaryProvider)
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue("groq");

        builder.Property(s => s.TriageRunCap)
            .HasDefaultValue(200);
    }
}

public class AiProviderSettingConfiguration : IEntityTypeConfiguration<AiProviderSetting>
{
    public void Configure(EntityTypeBuilder<AiProviderSetting> builder)
    {
        builder.ToTable("ai_provider_settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Provider)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(s => s.Provider).IsUnique();

        builder.Property(s => s.EncryptedApiKey).HasMaxLength(2048);
        builder.Property(s => s.KeyPrefix).HasMaxLength(8);
        builder.Property(s => s.KeyLastFour).HasMaxLength(8);
        builder.Property(s => s.ModelName).HasMaxLength(100);
        builder.Property(s => s.LastTestStatus).HasMaxLength(40);
        builder.Property(s => s.LastTestMessage).HasMaxLength(500);
        builder.Property(s => s.LastError).HasMaxLength(500);
    }
}

public class AiProviderUsageConfiguration : IEntityTypeConfiguration<AiProviderUsage>
{
    public void Configure(EntityTypeBuilder<AiProviderUsage> builder)
    {
        builder.ToTable("ai_provider_usages");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Provider)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(u => new { u.Provider, u.UsageDate }).IsUnique();
    }
}
