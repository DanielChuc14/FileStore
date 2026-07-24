using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.Name).HasMaxLength(100).IsRequired();
        builder.Property(k => k.KeyHash).HasMaxLength(512).IsRequired();
        builder.Property(k => k.Prefix).HasMaxLength(32).IsRequired();

        // Indice critico (seccion 5): cada request de la API resuelve la key por
        // su prefijo antes de verificar el hash. Unico para que ese lookup no ambiguo.
        builder.HasIndex(k => k.Prefix).IsUnique();

        builder.Property(k => k.IsActive).IsRequired();

        builder
            .HasOne(k => k.Client)
            .WithMany(c => c.ApiKeys)
            .HasForeignKey(k => k.ClientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
