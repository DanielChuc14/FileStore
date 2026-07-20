using FileStore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FileStore.Infrastructure.Persistence;

/// <summary>
/// Siembra el super-admin inicial. No va por HasData porque el hash de la
/// contraseña incluye un salt aleatorio: embebido en la migracion cambiaria en
/// cada regeneracion y ademas quedaria versionado en el repositorio.
/// </summary>
public class DatabaseSeeder(
    FileStoreDbContext context,
    IConfiguration configuration,
    ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await context.SuperAdmins.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Super-admin ya existe, se omite el seed.");
            return;
        }

        var email = configuration["SuperAdmin:Email"];
        var password = configuration["SuperAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No se creo el super-admin: faltan SuperAdmin:Email o SuperAdmin:Password " +
                "en la configuracion. Definilos con dotnet user-secrets o variables de entorno.");
            return;
        }

        var admin = new SuperAdmin
        {
            Id = Guid.CreateVersion7(),
            Email = email.Trim().ToLowerInvariant(),
            Name = configuration["SuperAdmin:Name"] ?? "Super Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        admin.PasswordHash = new PasswordHasher<SuperAdmin>().HashPassword(admin, password);

        context.SuperAdmins.Add(admin);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Super-admin creado: {Email}", admin.Email);
    }
}
