using FileStore.Application.Abstractions;
using FileStore.Application.Common;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Config;

public record AppConfigDto(
    long MaxFileSizeBytes,
    int TrashRetentionDays,
    int RateLimitDefaultPerMinute);

public record AllowedTypeDto(Guid Id, string Extension, string MimeType, bool IsEnabled);

public record GetConfigQuery : IRequest<AppConfigDto>;

public record UpdateConfigCommand(
    long? MaxFileSizeBytes,
    int? TrashRetentionDays,
    int? RateLimitDefaultPerMinute) : IRequest<AppConfigDto>;

public record GetAllowedTypesQuery : IRequest<IReadOnlyList<AllowedTypeDto>>;

public record UpdateAllowedTypeCommand(Guid Id, bool IsEnabled) : IRequest<AllowedTypeDto>;

public class UpdateConfigCommandValidator : AbstractValidator<UpdateConfigCommand>
{
    public UpdateConfigCommandValidator()
    {
        RuleFor(c => c.MaxFileSizeBytes)
            .InclusiveBetween(1024, 5L * 1024 * 1024 * 1024)
            .When(c => c.MaxFileSizeBytes.HasValue);

        RuleFor(c => c.TrashRetentionDays)
            .InclusiveBetween(1, 365)
            .When(c => c.TrashRetentionDays.HasValue);

        RuleFor(c => c.RateLimitDefaultPerMinute)
            .InclusiveBetween(1, 10000)
            .When(c => c.RateLimitDefaultPerMinute.HasValue);
    }
}

public class GetConfigQueryHandler(IAppConfigReader appConfig)
    : IRequestHandler<GetConfigQuery, AppConfigDto>
{
    public async Task<AppConfigDto> Handle(GetConfigQuery request, CancellationToken cancellationToken) =>
        new(
            await appConfig.GetMaxFileSizeBytesAsync(cancellationToken),
            await appConfig.GetTrashRetentionDaysAsync(cancellationToken),
            await appConfig.GetRateLimitDefaultPerMinuteAsync(cancellationToken));
}

public class UpdateConfigCommandHandler(
    IApplicationDbContext context,
    IAppConfigReader appConfig,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateConfigCommand, AppConfigDto>
{
    public async Task<AppConfigDto> Handle(
        UpdateConfigCommand request,
        CancellationToken cancellationToken)
    {
        var changes = new Dictionary<string, object?>();

        await SetAsync(AppConfigKeys.MaxFileSizeBytes, request.MaxFileSizeBytes, changes, cancellationToken);
        await SetAsync(AppConfigKeys.TrashRetentionDays, request.TrashRetentionDays, changes, cancellationToken);
        await SetAsync(AppConfigKeys.RateLimitDefaultPerMinute, request.RateLimitDefaultPerMinute, changes, cancellationToken);

        if (changes.Count > 0)
        {
            auditLogger.Record(
                AuditAction.UpdateConfig,
                resourceType: nameof(AppConfig),
                metadata: changes);

            await context.SaveChangesAsync(cancellationToken);

            // Sin esto el cambio no surtiria efecto hasta que venza el cache de
            // cinco minutos, y el admin creeria que no se guardo.
            appConfig.Invalidate();
        }

        return new AppConfigDto(
            await appConfig.GetMaxFileSizeBytesAsync(cancellationToken),
            await appConfig.GetTrashRetentionDaysAsync(cancellationToken),
            await appConfig.GetRateLimitDefaultPerMinuteAsync(cancellationToken));
    }

    private async Task SetAsync<T>(
        string key,
        T? value,
        Dictionary<string, object?> changes,
        CancellationToken cancellationToken)
        where T : struct
    {
        if (!value.HasValue)
        {
            return;
        }

        var entry = await context.AppConfigs
            .FirstOrDefaultAsync(c => c.Key == key, cancellationToken);

        var raw = value.Value.ToString()!;

        if (entry is null)
        {
            context.AppConfigs.Add(new AppConfig
            {
                Key = key,
                Value = raw,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else if (entry.Value != raw)
        {
            entry.Value = raw;
            entry.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            return;
        }

        changes[key] = raw;
    }
}

public class GetAllowedTypesQueryHandler(IApplicationDbContext context)
    : IRequestHandler<GetAllowedTypesQuery, IReadOnlyList<AllowedTypeDto>>
{
    public async Task<IReadOnlyList<AllowedTypeDto>> Handle(
        GetAllowedTypesQuery request,
        CancellationToken cancellationToken) =>
        await context.AllowedFileTypes
            .AsNoTracking()
            .OrderBy(t => t.Extension)
            .Select(t => new AllowedTypeDto(t.Id, t.Extension, t.MimeType, t.IsEnabled))
            .ToListAsync(cancellationToken);
}

public class UpdateAllowedTypeCommandHandler(
    IApplicationDbContext context,
    IAppConfigReader appConfig,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateAllowedTypeCommand, AllowedTypeDto>
{
    public async Task<AllowedTypeDto> Handle(
        UpdateAllowedTypeCommand request,
        CancellationToken cancellationToken)
    {
        var type = await context.AllowedFileTypes
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(AllowedFileType), request.Id);

        if (type.IsEnabled != request.IsEnabled)
        {
            type.IsEnabled = request.IsEnabled;
            type.UpdatedByAdminId = currentUser.UserId;
            type.UpdatedAt = DateTime.UtcNow;

            auditLogger.Record(
                AuditAction.UpdateConfig,
                resourceType: nameof(AllowedFileType),
                resourceId: type.Id,
                metadata: new { type.Extension, type.IsEnabled });

            await context.SaveChangesAsync(cancellationToken);

            // La whitelist se consulta en cada subida: hay que refrescarla o
            // seguiria aceptando o rechazando con el valor viejo.
            appConfig.Invalidate();
        }

        return new AllowedTypeDto(type.Id, type.Extension, type.MimeType, type.IsEnabled);
    }
}
