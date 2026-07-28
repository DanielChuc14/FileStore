using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Limits;

public record AllowedTypeInfo(string Extension, string MimeType);

/// <summary>
/// Todo lo que una integracion necesita saber antes de subir algo.
/// </summary>
public record LimitsDto(
    long MaxFileSizeBytes,
    long QuotaBytes,
    long UsedBytes,
    long AvailableBytes,
    int TrashRetentionDays,
    int RateLimitPerMinute,
    IReadOnlyList<AllowedTypeInfo> AllowedTypes);

/// <summary>
/// Hace la API autodescriptiva.
///
/// Sin esto, quien integra no puede averiguar por si mismo que extensiones se
/// aceptan ni cual es el tamaño maximo: la lista solo la expone un endpoint de
/// super-admin, y el tamaño no lo expone nadie. Se descubren a base de recibir
/// 400 y 413, o preguntando por correo y anotandolos a mano — y como son
/// configurables por despliegue, esa nota se queda obsoleta en silencio el dia
/// que el administrador cambie algo.
///
/// Ademas resuelve un hueco del canal de API Key: `/me/usage` exige JWT, asi que
/// una integracion no tenia forma de consultar su cuota. Se enteraba de que
/// estaba llena al recibir un 413 a mitad de una carga.
/// </summary>
public record GetLimitsQuery : IRequest<LimitsDto>;

public class GetLimitsQueryHandler(
    IApplicationDbContext context,
    IAppConfigReader appConfig,
    ICurrentUser currentUser)
    : IRequestHandler<GetLimitsQuery, LimitsDto>
{
    public async Task<LimitsDto> Handle(GetLimitsQuery request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var client = await context.Clients
            .AsNoTracking()
            .Where(c => c.Id == clientId && !c.IsDeleted)
            .Select(c => new
            {
                c.QuotaBytes,
                c.UsedBytes,
                c.MaxFileSizeBytes,
                c.TrashRetentionDays
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidCredentialsException();

        // Los valores que se devuelven son los EFECTIVOS para este cliente: su
        // override si lo tiene, y si no el default global. Devolver el global a
        // secas seria peor que no decir nada, porque quien integra lo daria por
        // bueno y seguiria chocando con un limite distinto.
        var maxFileSize = client.MaxFileSizeBytes
            ?? await appConfig.GetMaxFileSizeBytesAsync(cancellationToken);

        var retentionDays = client.TrashRetentionDays
            ?? await appConfig.GetTrashRetentionDaysAsync(cancellationToken);

        var rateLimit = await ResolveRateLimitAsync(cancellationToken);

        var allowedTypes = await context.AllowedFileTypes
            .AsNoTracking()
            .Where(t => t.IsEnabled)
            .OrderBy(t => t.Extension)
            .Select(t => new AllowedTypeInfo(t.Extension, t.MimeType))
            .ToListAsync(cancellationToken);

        // Puede ser negativo si el administrador recorto la cuota por debajo de
        // lo ya consumido, que es una operacion permitida. Se acota en cero: al
        // que integra le interesa "cuanto me cabe", y la respuesta es ninguno.
        var available = Math.Max(0, client.QuotaBytes - client.UsedBytes);

        return new LimitsDto(
            maxFileSize,
            client.QuotaBytes,
            client.UsedBytes,
            available,
            retentionDays,
            rateLimit,
            allowedTypes);
    }

    /// <summary>
    /// Con API Key se informa el limite de ESA key, que es el que se le va a
    /// aplicar. Por JWT no hay limite por credencial, asi que se informa el
    /// default global como referencia de lo que tendria una key nueva.
    /// </summary>
    private async Task<int> ResolveRateLimitAsync(CancellationToken cancellationToken)
    {
        var globalDefault = await appConfig.GetRateLimitDefaultPerMinuteAsync(cancellationToken);

        if (currentUser.ApiKeyId is not { } apiKeyId)
        {
            return globalDefault;
        }

        var keyLimit = await context.ApiKeys
            .AsNoTracking()
            .Where(k => k.Id == apiKeyId)
            .Select(k => k.RateLimitPerMinute)
            .FirstOrDefaultAsync(cancellationToken);

        return keyLimit ?? globalDefault;
    }
}
