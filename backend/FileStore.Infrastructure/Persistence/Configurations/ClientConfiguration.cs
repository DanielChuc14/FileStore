using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("Clients");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Email).HasMaxLength(256).IsRequired();
        builder.Property(c => c.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();

        // El login resuelve por email, y ademas no puede haber dos cuentas con el mismo.
        builder.HasIndex(c => c.Email).IsUnique();

        builder.Property(c => c.QuotaBytes).IsRequired();
        builder.Property(c => c.UsedBytes).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.IsDeleted).IsRequired();

        // El listado del super-admin siempre excluye las bajas.
        builder.HasIndex(c => c.IsDeleted);
    }
}
