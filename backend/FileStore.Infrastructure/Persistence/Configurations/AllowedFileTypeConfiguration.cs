using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class AllowedFileTypeConfiguration : IEntityTypeConfiguration<AllowedFileType>
{
    public void Configure(EntityTypeBuilder<AllowedFileType> builder)
    {
        builder.ToTable("AllowedFileTypes");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Extension).HasMaxLength(20).IsRequired();
        builder.Property(t => t.MimeType).HasMaxLength(255).IsRequired();
        builder.Property(t => t.IsEnabled).IsRequired();

        builder.HasIndex(t => t.Extension).IsUnique();

        builder
            .HasOne(t => t.UpdatedByAdmin)
            .WithMany()
            .HasForeignKey(t => t.UpdatedByAdminId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasData(SeedData.AllowedFileTypes);
    }
}
