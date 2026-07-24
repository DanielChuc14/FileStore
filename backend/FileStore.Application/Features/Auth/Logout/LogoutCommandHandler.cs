using FileStore.Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Auth.Logout;

public class LogoutCommandHandler(
    IApplicationDbContext context,
    IJwtTokenGenerator tokenGenerator)
    : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        // Logout es idempotente: sin cookie, o con un token ya invalido, se
        // responde igual. No tiene sentido devolver error a alguien que quiere
        // cerrar sesion.
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return;
        }

        var hash = tokenGenerator.HashRefreshToken(request.RefreshToken);

        var stored = await context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, cancellationToken);

        if (stored is null)
        {
            return;
        }

        stored.RevokedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }
}
