using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AlertSettingConfiguration : IEntityTypeConfiguration<AlertSetting>
{
    public void Configure(EntityTypeBuilder<AlertSetting> builder)
    {
        builder.ToTable("alert_settings");

        builder.HasKey(s => s.Id);
    }
}
