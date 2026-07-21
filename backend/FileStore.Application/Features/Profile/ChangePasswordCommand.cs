using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Profile;

/// <summary>
/// CurrentRefreshToken llega desde la cookie del request. Sirve para conservar
/// viva la sesion que hace el cambio y revocar todas las demas.
/// </summary>
public record ChangePasswordCommand(
    string CurrentPassword,
    string NewPassword,
    string? CurrentRefreshToken) : IRequest;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(c => c.CurrentPassword).NotEmpty();

        RuleFor(c => c.NewPassword)
            .NotEmpty()
            .MinimumLength(12).WithMessage("La contraseña debe tener al menos 12 caracteres.")
            .MaximumLength(256)
            .NotEqual(c => c.CurrentPassword)
            .WithMessage("La contraseña nueva debe ser distinta de la actual.");
    }
}

public class ChangePasswordCommandHandler(
    IApplicationDbContext context,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator tokenGenerator,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var client = await context.Clients
            .FirstOrDefaultAsync(
                c => c.Id == clientId && !c.IsDeleted && c.IsActive,
                cancellationToken)
            ?? throw new InvalidCredentialsException();

        // Se exige la contraseña actual: sin esto, una sesion secuestrada podria
        // cambiarla y dejar al dueño afuera sin conocer la original.
        var (isValid, _) = passwordHasher.Verify(client.PasswordHash, request.CurrentPassword);

        if (!isValid)
        {
            throw new InvalidCredentialsException("La contraseña actual es incorrecta.");
        }

        client.PasswordHash = passwordHasher.Hash(request.NewPassword);
        client.UpdatedAt = DateTime.UtcNow;

        // Se cierran las demas sesiones pero se conserva la actual: si el cambio
        // responde a una credencial comprometida, dejar vivas las otras sesiones
        // anularia el proposito. Expulsar tambien a quien lo hace seria molesto.
        var currentTokenHash = string.IsNullOrWhiteSpace(request.CurrentRefreshToken)
            ? null
            : tokenGenerator.HashRefreshToken(request.CurrentRefreshToken);

        var otherSessions = await context.RefreshTokens
            .Where(t => t.UserId == clientId
                && t.UserType == UserType.Client
                && t.RevokedAt == null
                && (currentTokenHash == null || t.TokenHash != currentTokenHash))
            .ToListAsync(cancellationToken);

        foreach (var session in otherSessions)
        {
            session.RevokedAt = DateTime.UtcNow;
        }

        auditLogger.Record(
            AuditAction.ChangePassword,
            clientId: clientId,
            resourceType: nameof(Client),
            resourceId: clientId,
            metadata: new { Reset = false, RevokedSessions = otherSessions.Count });

        await context.SaveChangesAsync(cancellationToken);
    }
}
