using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.ApiKeys.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.ApiKeys.GetList;

public record GetApiKeysQuery : IRequest<IReadOnlyList<ApiKeyDto>>;

public class GetApiKeysQueryHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser)
    : IRequestHandler<GetApiKeysQuery, IReadOnlyList<ApiKeyDto>>
{
    public async Task<IReadOnlyList<ApiKeyDto>> Handle(
        GetApiKeysQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.UserId ?? throw new InvalidCredentialsException();

        // El filtro por ClientId es lo que garantiza el aislamiento: sin el, un
        // cliente veria las keys de todos.
        return await context.ApiKeys
            .AsNoTracking()
            .Where(k => k.ClientId == clientId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyDto(
                k.Id,
                k.Name,
                k.Prefix,
                k.RateLimitPerMinute,
                k.IsActive,
                k.LastUsedAt,
                k.CreatedAt,
                k.RevokedAt))
            .ToListAsync(cancellationToken);
    }
}
