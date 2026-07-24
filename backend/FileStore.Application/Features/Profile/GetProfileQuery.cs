using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Profile;

public record ProfileDto(
    Guid Id,
    string Email,
    string Name,
    long QuotaBytes,
    long UsedBytes,
    int? TrashRetentionDays,
    long? MaxFileSizeBytes,
    DateTime CreatedAt);

public record GetProfileQuery : IRequest<ProfileDto>;

public class GetProfileQueryHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser)
    : IRequestHandler<GetProfileQuery, ProfileDto>
{
    public async Task<ProfileDto> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        return await context.Clients
            .AsNoTracking()
            .Where(c => c.Id == clientId && !c.IsDeleted)
            .Select(c => new ProfileDto(
                c.Id,
                c.Email,
                c.Name,
                c.QuotaBytes,
                c.UsedBytes,
                c.TrashRetentionDays,
                c.MaxFileSizeBytes,
                c.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidCredentialsException();
    }
}
