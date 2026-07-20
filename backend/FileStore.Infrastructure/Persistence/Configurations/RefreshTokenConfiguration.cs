using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);

        // SHA-256 en hexadecimal: 64 caracteres.
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.CreatedByIp).HasMaxLength(45);

        // Cada refresh resuelve el token por su hash. Unico: dos sesiones no
        // pueden compartir el mismo valor.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Logout global y limpieza de tokens vencidos por usuario.
        builder.HasIndex(t => new { t.UserId, t.UserType });

        // IsActive es calculada en memoria, no tiene columna.
        builder.Ignore(t => t.IsActive);
    }
}
