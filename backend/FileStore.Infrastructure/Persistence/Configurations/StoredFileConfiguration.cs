using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("Files");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.OriginalName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.MimeType).HasMaxLength(255).IsRequired();
        builder.Property(f => f.Extension).HasMaxLength(20).IsRequired();
        builder.Property(f => f.IsDeleted).IsRequired();

        // Indice critico (seccion 5): listado del explorador, que siempre filtra
        // por cliente, carpeta y excluye los borrados.
        builder.HasIndex(f => new { f.ClientId, f.FolderId, f.IsDeleted });

        // Indice critico (seccion 5): papelera y job de purga, que barren por
        // fecha de borrado dentro de un cliente.
        builder.HasIndex(f => new { f.ClientId, f.DeletedAt });

        builder
            .HasOne(f => f.Client)
            .WithMany(c => c.Files)
            .HasForeignKey(f => f.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(f => f.Folder)
            .WithMany(fo => fo.Files)
            .HasForeignKey(f => f.FolderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Puntero a la version vigente. Es una referencia circular con FileVersion
        // (Files -> FileVersions -> Files), asi que va con NoAction: si fuera
        // cascada, EF rechazaria el modelo por ciclo de borrado. La consecuencia
        // practica es que el hard delete (Fase 6) debe poner CurrentVersionId en
        // null antes de borrar las versiones.
        builder
            .HasOne(f => f.CurrentVersion)
            .WithMany()
            .HasForeignKey(f => f.CurrentVersionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
