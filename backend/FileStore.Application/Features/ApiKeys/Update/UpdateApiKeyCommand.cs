using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.ApiKeys.Common;
using FileStore.Application.Features.ApiKeys.Create;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.ApiKeys.Update;

public record UpdateApiKeyCommand(
    Guid Id,
    string? Name,
    int? RateLimitPerMinute,
    bool ClearRateLimitOverride = false) : IRequest<ApiKeyDto>;

public class UpdateApiKeyCommandValidator : AbstractValidator<UpdateApiKeyCommand>
{
    public UpdateApiKeyCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100).When(c => c.Name is not null);
        RuleFor(c => c.RateLimitPerMinute)
            .InclusiveBetween(1, 10000)
            .When(c => c.RateLimitPerMinute.HasValue);
    }
}

public class UpdateApiKeyCommandHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateApiKeyCommand, ApiKeyDto>
{
    public async Task<ApiKeyDto> Handle(
        UpdateApiKeyCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.UserId ?? throw new InvalidCredentialsException();

        // El ClientId va en el WHERE y no en un chequeo posterior: pedir la key
        // de otro cliente devuelve 404, sin confirmar siquiera que existe.
        var apiKey = await context.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == request.Id && k.ClientId == clientId, cancellationToken)
            ?? throw new NotFoundException(nameof(ApiKey), request.Id);

        var changes = new Dictionary<string, object?>();

        if (request.Name is not null && request.Name != apiKey.Name)
        {
            changes[nameof(apiKey.Name)] = request.Name;
            apiKey.Name = request.Name.Trim();
        }

        if (request.ClearRateLimitOverride)
        {
            changes[nameof(apiKey.RateLimitPerMinute)] = null;
            apiKey.RateLimitPerMinute = null;
        }
        else if (request.RateLimitPerMinute.HasValue)
        {
            changes[nameof(apiKey.RateLimitPerMinute)] = request.RateLimitPerMinute;
            apiKey.RateLimitPerMinute = request.RateLimitPerMinute;
        }

        if (changes.Count > 0)
        {
            auditLogger.Record(
                AuditAction.UpdateApiKey,
                clientId: clientId,
                resourceType: nameof(ApiKey),
                resourceId: apiKey.Id,
                metadata: changes);

            await context.SaveChangesAsync(cancellationToken);
        }

        return CreateApiKeyCommandHandler.ToDto(apiKey);
    }
}
