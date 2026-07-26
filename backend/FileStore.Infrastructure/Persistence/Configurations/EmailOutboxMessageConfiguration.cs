using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("EmailOutbox");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Recipient).HasMaxLength(320).IsRequired();
        builder.Property(m => m.Subject).HasMaxLength(300).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(1000);

        // El despachador busca pendientes cuya hora de reintento ya paso. Este
        // indice es el que sostiene esa consulta, que corre cada pocos minutos.
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });

        // Sin FK a Clients a proposito: un correo debe poder sobrevivir al
        // borrado del cliente, y hay correos que no corresponden a ninguno.
        builder.HasIndex(m => m.ClientId);
    }
}
