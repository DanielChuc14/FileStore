using FileStore.Application.Abstractions;
using FileStore.Infrastructure.Authentication;
using FileStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FileStore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is not configured.");

        services.AddDbContext<FileStoreDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<FileStoreDbContext>());

        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .Validate(
                s => !string.IsNullOrWhiteSpace(s.Secret) && s.Secret.Length >= 32,
                "Jwt:Secret debe estar configurado y tener al menos 32 caracteres.")
            .ValidateOnStart();

        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();

        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
