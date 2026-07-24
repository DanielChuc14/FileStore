using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Auth.Common;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Auth.Login;

public class LoginCommandHandler(
    IApplicationDbContext context,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator tokenGenerator)
    : IRequestHandler<LoginCommand, AuthenticationResult>
{
    /// <summary>
    /// Hash de descarte. Si el email no existe igual se verifica contra este para
    /// que la respuesta tarde lo mismo que con un email valido: sin esto, medir
    /// el tiempo de respuesta permite enumerar que cuentas existen.
    /// </summary>
    private static string? _dummyHash;

    public async Task<AuthenticationResult> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var admin = await context.SuperAdmins
            .FirstOrDefaultAsync(a => a.Email == email, cancellationToken);

        if (admin is not null)
        {
            return await AuthenticateAsync(
                admin.Id, admin.Email, admin.PasswordHash, admin.IsActive,
                UserType.SuperAdmin, request, cancellationToken);
        }

        var client = await context.Clients
            .FirstOrDefaultAsync(c => c.Email == email, cancellationToken);

        if (client is not null)
        {
            return await AuthenticateAsync(
                client.Id, client.Email, client.PasswordHash, client.IsActive,
                UserType.Client, request, cancellationToken);
        }

        // Email inexistente: se gasta el mismo tiempo que en una verificacion real.
        _dummyHash ??= passwordHasher.Hash("not-a-real-password");
        passwordHasher.Verify(_dummyHash, request.Password);

        throw new InvalidCredentialsException();
    }

    private async Task<AuthenticationResult> AuthenticateAsync(
        Guid userId,
        string email,
        string passwordHash,
        bool isActive,
        UserType userType,
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var (isValid, _) = passwordHasher.Verify(passwordHash, request.Password);

        // Cuenta bloqueada: se responde igual que con contraseña incorrecta para
        // no confirmar que la cuenta existe.
        if (!isValid || !isActive)
        {
            throw new InvalidCredentialsException();
        }

        var (accessToken, accessExpiresAt) =
            tokenGenerator.GenerateAccessToken(userId, email, userType);

        var (refreshToken, refreshHash, refreshExpiresAt) =
            tokenGenerator.GenerateRefreshToken();

        context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            UserType = userType,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiresAt,
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = request.IpAddress
        });

        await context.SaveChangesAsync(cancellationToken);

        return new AuthenticationResult(
            accessToken,
            accessExpiresAt,
            refreshToken,
            refreshExpiresAt,
            userId,
            email,
            userType.ToString());
    }
}
