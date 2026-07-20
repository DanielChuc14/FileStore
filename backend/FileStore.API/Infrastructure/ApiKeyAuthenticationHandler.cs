using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using FileStore.Application.Abstractions;
using FileStore.Application.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FileStore.API.Infrastructure;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApplicationDbContext context,
    IApiKeyGenerator generator,
    IMemoryCache cache)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    /// <summary>
    /// Cada cuanto se persiste LastUsedAt. Escribirlo en cada request convertiria
    /// cada lectura de la API en una escritura a la base.
    /// </summary>
    private static readonly TimeSpan LastUsedThrottle = TimeSpan.FromMinutes(5);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AuthSchemes.ApiKeyHeader, out var header))
        {
            // NoResult y no Fail: sin el header, este esquema simplemente no
            // aplica. Fail marcaria el request como intento fallido de auth.
            return AuthenticateResult.NoResult();
        }

        var presented = header.ToString();
        var prefix = generator.ExtractPrefix(presented);

        if (prefix is null)
        {
            return AuthenticateResult.Fail("Malformed API key.");
        }

        // Se busca por prefijo (indice unico) y no hasheando toda la tabla.
        var record = await context.ApiKeys
            .Where(k => k.Prefix == prefix)
            .Select(k => new
            {
                k.Id,
                k.ClientId,
                k.KeyHash,
                k.IsActive,
                k.RevokedAt,
                k.LastUsedAt,
                ClientIsActive = k.Client.IsActive,
                ClientIsDeleted = k.Client.IsDeleted,
                ClientName = k.Client.Name
            })
            .FirstOrDefaultAsync();

        if (record is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        // Comparacion en tiempo constante: un equals normal corta en el primer
        // byte distinto, y medir esa diferencia permite reconstruir el hash.
        var presentedHash = generator.Hash(presented);
        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presentedHash),
            Encoding.UTF8.GetBytes(record.KeyHash));

        if (!matches)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        if (!record.IsActive || record.RevokedAt is not null)
        {
            return AuthenticateResult.Fail("API key is revoked.");
        }

        // Un cliente bloqueado o dado de baja no puede operar por ningun canal,
        // aunque su key siga tecnicamente activa.
        if (!record.ClientIsActive || record.ClientIsDeleted)
        {
            return AuthenticateResult.Fail("Client account is not active.");
        }

        await TouchLastUsedAsync(record.Id, record.LastUsedAt);

        var identity = new ClaimsIdentity(
            [
                new Claim(AuthClaims.UserId, record.ClientId.ToString()),
                new Claim(AuthClaims.ClientId, record.ClientId.ToString()),
                new Claim(AuthClaims.ApiKeyId, record.Id.ToString()),
                new Claim(ClaimTypes.Name, record.ClientName)
            ],
            AuthSchemes.ApiKey);

        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, AuthSchemes.ApiKey));
    }

    private async Task TouchLastUsedAsync(Guid apiKeyId, DateTime? lastUsedAt)
    {
        var cacheKey = $"apikey-touched-{apiKeyId}";

        if (cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (lastUsedAt is null || now - lastUsedAt.Value >= LastUsedThrottle)
        {
            // ExecuteUpdateAsync emite un UPDATE directo, sin pasar por el
            // change tracker del DbContext que esta usando el request.
            await context.ApiKeys
                .Where(k => k.Id == apiKeyId)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now));
        }

        cache.Set(cacheKey, true, LastUsedThrottle);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = AuthSchemes.ApiKeyHeader;
        return Task.CompletedTask;
    }
}
