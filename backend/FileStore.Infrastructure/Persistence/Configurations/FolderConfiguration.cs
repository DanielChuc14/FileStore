using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("Folders");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Name).HasMaxLength(255).IsRequired();
        builder.Property(f => f.Path).HasMaxLength(4000).IsRequired();

        // Navegacion del explorador: listar el contenido de una carpeta.
        builder.HasIndex(f => new { f.ClientId, f.ParentFolderId });

        // No puede haber dos carpetas con el mismo nombre bajo el mismo padre.
        builder.HasIndex(f => new { f.ClientId, f.ParentFolderId, f.Name }).IsUnique();

        builder
            .HasOne(f => f.Client)
            .WithMany(c => c.Folders)
            .HasForeignKey(f => f.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict y no Cascade: borrar una carpeta con subcarpetas debe ser una
        // decision explicita de la aplicacion (?recursive=true), no un efecto
        // silencioso de la BD.
        builder
            .HasOne(f => f.ParentFolder)
            .WithMany(f => f.ChildFolders)
            .HasForeignKey(f => f.ParentFolderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
