using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.ApiKeys.Common;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;

namespace FileStore.Application.Features.ApiKeys.Create;

public record CreateApiKeyCommand(string Name, int? RateLimitPerMinute)
    : IRequest<CreateApiKeyResult>;

public class CreateApiKeyCommandValidator : AbstractValidator<CreateApiKeyCommand>
{
    public CreateApiKeyCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.RateLimitPerMinute)
            .InclusiveBetween(1, 10000)
            .When(c => c.RateLimitPerMinute.HasValue);
    }
}

public class CreateApiKeyCommandHandler(
    IApplicationDbContext context,
    IApiKeyGenerator generator,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<CreateApiKeyCommand, CreateApiKeyResult>
{
    public async Task<CreateApiKeyResult> Handle(
        CreateApiKeyCommand request,
        CancellationToken cancellationToken)
    {
        // El ClientId sale del token, nunca del request: un cliente no puede
        // crear keys para otro aunque manipule el body.
        var clientId = currentUser.ClientId
            ?? throw new InvalidCredentialsException();

        var generated = generator.Generate();

        var apiKey = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            ClientId = clientId,
            Name = request.Name.Trim(),
            KeyHash = generated.Hash,
            Prefix = generated.Prefix,
            RateLimitPerMinute = request.RateLimitPerMinute,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        context.ApiKeys.Add(apiKey);

        auditLogger.Record(
            AuditAction.CreateApiKey,
            clientId: clientId,
            resourceType: nameof(ApiKey),
            resourceId: apiKey.Id,
            metadata: new { apiKey.Name, apiKey.Prefix });

        await context.SaveChangesAsync(cancellationToken);

        return new CreateApiKeyResult(ToDto(apiKey), generated.Value);
    }

    internal static ApiKeyDto ToDto(ApiKey key) => new(
        key.Id,
        key.Name,
        key.Prefix,
        key.RateLimitPerMinute,
        key.IsActive,
        key.LastUsedAt,
        key.CreatedAt,
        key.RevokedAt);
}
