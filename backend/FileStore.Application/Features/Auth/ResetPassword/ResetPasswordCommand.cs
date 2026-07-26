using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Auth.ResetPassword;

/// <summary>Canjea el token del correo por una contraseña nueva.</summary>
public record ResetPasswordCommand(string Token, string NewPassword) : IRequest;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(c => c.Token).NotEmpty();

        // Mismas reglas que el cambio desde el perfil: no tendria sentido que
        // recuperar la cuenta permitiera una contraseña mas debil.
        RuleFor(c => c.NewPassword)
            .NotEmpty()
            .MinimumLength(12).WithMessage("La contraseña debe tener al menos 12 caracteres.")
            .MaximumLength(256);
    }
}

public class ResetPasswordCommandHandler(
    IApplicationDbContext context,
    ISecureTokenGenerator tokenGenerator,
    IPasswordHasher passwordHasher,
    IAuditLogger auditLogger)
    : IRequestHandler<ResetPasswordCommand>
{
    public async Task Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var hash = tokenGenerator.Hash(request.Token);

        var reset = await context.PasswordResetTokens
            .Include(t => t.Client)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        // Token inexistente, ya canjeado o vencido: la misma respuesta para los
        // tres. Distinguirlos le diria a quien prueba tokens si acerto con uno
        // real pero caducado.
        if (reset is null || !reset.IsUsable)
        {
            throw new InvalidCredentialsException("El enlace no es valido o ya vencio.");
        }

        var client = reset.Client;

        if (client.IsDeleted || !client.IsActive)
        {
            throw new InvalidCredentialsException("El enlace no es valido o ya vencio.");
        }

        var now = DateTime.UtcNow;

        client.PasswordHash = passwordHasher.Hash(request.NewPassword);
        client.UpdatedAt = now;

        // Un solo uso. Se marca antes de guardar para que dos canjes simultaneos
        // del mismo token no puedan pasar ambos.
        reset.UsedAt = now;

        // Recuperar la contraseña suele responder a haber perdido el control de
        // la cuenta: se cierran TODAS las sesiones, sin excepciones. Aqui no hay
        // una sesion actual que conservar, a diferencia del cambio desde perfil.
        var sessions = await context.RefreshTokens
            .Where(t => t.UserId == client.Id
                && t.UserType == UserType.Client
                && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAt = now;
        }

        auditLogger.Record(
            AuditAction.ChangePassword,
            clientId: client.Id,
            resourceType: nameof(Domain.Entities.Client),
            resourceId: client.Id,
            metadata: new { SelfService = true, RevokedSessions = sessions.Count });

        await context.SaveChangesAsync(cancellationToken);
    }
}
