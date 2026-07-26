using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("PasswordResetTokens");
        builder.HasKey(t => t.Id);

        // SHA-256 en hexadecimal: 64 caracteres.
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.CreatedByIp).HasMaxLength(45);

        // El canje resuelve el token por su hash, y tiene que ser unico.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Al emitir uno nuevo se invalidan los pendientes del mismo usuario.
        // Sin FK: UserId apunta a Clients o a SuperAdmins segun UserType, igual
        // que en RefreshToken.
        builder.HasIndex(t => new { t.UserId, t.UserType });

        builder.Ignore(t => t.IsUsable);
    }
}
