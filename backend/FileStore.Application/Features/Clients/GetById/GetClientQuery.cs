using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Clients.Common;
using FileStore.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Clients.GetById;

public record GetClientQuery(Guid Id) : IRequest<ClientDto>;

public class GetClientQueryHandler(IApplicationDbContext context)
    : IRequestHandler<GetClientQuery, ClientDto>
{
    public async Task<ClientDto> Handle(GetClientQuery request, CancellationToken cancellationToken)
    {
        var client = await context.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Client), request.Id);

        return ClientDto.From(client);
    }
}
