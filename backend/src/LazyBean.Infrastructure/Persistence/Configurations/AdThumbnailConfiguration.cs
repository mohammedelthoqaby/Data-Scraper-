using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LazyBean.Infrastructure.Persistence.Configurations;

public class AdThumbnailConfiguration : IEntityTypeConfiguration<AdThumbnail>
{
    public void Configure(EntityTypeBuilder<AdThumbnail> builder)
    {
        builder.ToTable("ad_thumbnails");

        builder.HasKey(t => t.Id);

        // One image per position per ad: a new image replaces the old one in place.
        builder.HasIndex(t => new { t.AdId, t.Position }).IsUnique();

        builder.Property(t => t.Data).IsRequired();
        builder.Property(t => t.ContentType).HasMaxLength(40).IsRequired();
        builder.Property(t => t.Sha256).HasMaxLength(64).IsRequired();

        // Removing an ad removes its images.
        builder.HasOne<Ad>()
            .WithMany()
            .HasForeignKey(t => t.AdId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
