using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class FileVersionConfiguration : IEntityTypeConfiguration<FileVersion>
{
    public void Configure(EntityTypeBuilder<FileVersion> builder)
    {
        builder.ToTable("FileVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(v => v.MimeType).HasMaxLength(255).IsRequired();

        // SHA-256 en hexadecimal son siempre 64 caracteres.
        builder.Property(v => v.ChecksumSha256).HasMaxLength(64).IsRequired();

        // Listar versiones de un archivo, y garantizar que no se repita el
        // correlativo ante dos subidas concurrentes del mismo archivo.
        builder.HasIndex(v => new { v.FileId, v.VersionNumber }).IsUnique();

        builder
            .HasOne(v => v.File)
            .WithMany(f => f.Versions)
            .HasForeignKey(v => v.FileId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: revocar o borrar una API Key no debe arrastrar las versiones
        // que subio. La trazabilidad de quien subio que tiene que sobrevivir.
        builder
            .HasOne(v => v.UploadedByApiKey)
            .WithMany()
            .HasForeignKey(v => v.UploadedByApiKeyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
