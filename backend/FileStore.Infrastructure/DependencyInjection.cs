using FileStore.Application.Abstractions;
using FileStore.Infrastructure.Authentication;
using FileStore.Infrastructure.BackgroundJobs;
using FileStore.Infrastructure.Persistence;
using FileStore.Infrastructure.Services;
using FileStore.Infrastructure.Storage;
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
        services.AddSingleton<IPasswordGenerator, PasswordGenerator>();
        services.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();

        services.AddOptions<StorageSettings>()
            .Bind(configuration.GetSection(StorageSettings.SectionName))
            .Validate(
                s => !string.IsNullOrWhiteSpace(s.BasePath),
                "Storage:BasePath debe estar configurado.")
            .ValidateOnStart();

        services.AddSingleton<IStorageService, LocalFileStorageService>();

        // Scoped: depende de ICurrentUser y del DbContext, que viven por request.
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAppConfigReader, AppConfigReader>();
        services.AddScoped<IFileEraser, FileEraser>();

        services.Configure<TrashPurgeSettings>(
            configuration.GetSection(TrashPurgeSettings.SectionName));

        services.AddHostedService<TrashPurgeService>();

        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<DemoDataSeeder>();

        return services;
    }
}
