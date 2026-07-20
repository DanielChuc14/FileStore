using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ResourceType).HasMaxLength(50);
        builder.Property(a => a.IpAddress).HasMaxLength(45); // IPv6 completa
        builder.Property(a => a.UserAgent).HasMaxLength(512);

        // jsonb y no text: permite consultar dentro del metadata con operadores
        // nativos de PostgreSQL si mas adelante hace falta.
        builder.Property(a => a.MetadataJson).HasColumnType("jsonb");

        // Indice critico (seccion 5): la vista de auditoria del cliente pagina
        // por fecha descendente dentro de su propio historial.
        builder.HasIndex(a => new { a.ClientId, a.CreatedAt });

        // Restrict y no Cascade: el audit log no debe desaparecer al borrar un
        // cliente. Es justamente el registro de que existio y que se hizo.
        builder
            .HasOne(a => a.Client)
            .WithMany()
            .HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
