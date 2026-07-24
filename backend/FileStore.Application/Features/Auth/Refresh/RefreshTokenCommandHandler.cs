using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Auth.Common;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileStore.Application.Features.Auth.Refresh;

public class RefreshTokenCommandHandler(
    IApplicationDbContext context,
    IJwtTokenGenerator tokenGenerator,
    ILogger<RefreshTokenCommandHandler> logger)
    : IRequestHandler<RefreshTokenCommand, AuthenticationResult>
{
    public async Task<AuthenticationResult> Handle(
        RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        var hash = tokenGenerator.HashRefreshToken(request.RefreshToken);

        var stored = await context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            throw new InvalidCredentialsException();
        }

        // Un token ya revocado que vuelve a usarse solo tiene dos explicaciones:
        // o fue robado y el atacante lo esta reusando, o el legitimo se lo
        // robaron y ya rotamos. En ambos casos no se puede distinguir quien es
        // quien, asi que se cierran todas las sesiones del usuario y que vuelva
        // a autenticarse.
        if (stored.RevokedAt is not null)
        {
            logger.LogWarning(
                "Reutilizacion de refresh token detectada para {UserType} {UserId}. " +
                "Se revocan todas sus sesiones.",
                stored.UserType, stored.UserId);

            await RevokeAllSessionsAsync(stored.UserId, stored.UserType, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            throw new InvalidCredentialsException();
        }

        if (DateTime.UtcNow >= stored.ExpiresAt)
        {
            throw new InvalidCredentialsException();
        }

        var (email, isActive) = await ResolveUserAsync(
            stored.UserId, stored.UserType, cancellationToken);

        if (email is null || !isActive)
        {
            throw new InvalidCredentialsException();
        }

        var (accessToken, accessExpiresAt) =
            tokenGenerator.GenerateAccessToken(stored.UserId, email, stored.UserType);

        var (newToken, newHash, newExpiresAt) = tokenGenerator.GenerateRefreshToken();

        var replacement = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = stored.UserId,
            UserType = stored.UserType,
            TokenHash = newHash,
            ExpiresAt = newExpiresAt,
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = request.IpAddress
        };

        // Rotacion: el token usado queda invalidado en el mismo momento en que
        // se emite su reemplazo. Sin esto, un token robado serviria hasta vencer.
        stored.RevokedAt = DateTime.UtcNow;
        stored.ReplacedByTokenId = replacement.Id;

        context.RefreshTokens.Add(replacement);
        await context.SaveChangesAsync(cancellationToken);

        return new AuthenticationResult(
            accessToken,
            accessExpiresAt,
            newToken,
            newExpiresAt,
            stored.UserId,
            email,
            stored.UserType.ToString());
    }

    private async Task RevokeAllSessionsAsync(
        Guid userId,
        UserType userType,
        CancellationToken cancellationToken)
    {
        var active = await context.RefreshTokens
            .Where(t => t.UserId == userId && t.UserType == userType && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.RevokedAt = DateTime.UtcNow;
        }
    }

    private async Task<(string? Email, bool IsActive)> ResolveUserAsync(
        Guid userId,
        UserType userType,
        CancellationToken cancellationToken)
    {
        if (userType == UserType.SuperAdmin)
        {
            var admin = await context.SuperAdmins
                .Where(a => a.Id == userId)
                .Select(a => new { a.Email, a.IsActive })
                .FirstOrDefaultAsync(cancellationToken);

            return (admin?.Email, admin?.IsActive ?? false);
        }

        var client = await context.Clients
            .Where(c => c.Id == userId)
            .Select(c => new { c.Email, c.IsActive })
            .FirstOrDefaultAsync(cancellationToken);

        return (client?.Email, client?.IsActive ?? false);
    }
}
