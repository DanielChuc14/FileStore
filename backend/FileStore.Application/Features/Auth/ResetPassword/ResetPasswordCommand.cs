using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
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
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        // Token inexistente, ya canjeado o vencido: la misma respuesta para los
        // tres. Distinguirlos le diria a quien prueba tokens si acerto con uno
        // real pero caducado.
        if (reset is null || !reset.IsUsable)
        {
            throw new InvalidCredentialsException("El enlace no es valido o ya vencio.");
        }

        var now = DateTime.UtcNow;
        var newHash = passwordHasher.Hash(request.NewPassword);

        var applied = reset.UserType == UserType.SuperAdmin
            ? await ApplyToSuperAdminAsync(reset.UserId, newHash, now, cancellationToken)
            : await ApplyToClientAsync(reset.UserId, newHash, now, cancellationToken);

        if (!applied)
        {
            // La cuenta se dio de baja o se bloqueo entre que se pidio el enlace
            // y se canjeo.
            throw new InvalidCredentialsException("El enlace no es valido o ya vencio.");
        }

        // Un solo uso. Se marca antes de guardar para que dos canjes simultaneos
        // del mismo token no puedan pasar ambos.
        reset.UsedAt = now;

        // Recuperar la contraseña suele responder a haber perdido el control de
        // la cuenta: se cierran TODAS las sesiones, sin excepciones. Aqui no hay
        // una sesion actual que conservar, a diferencia del cambio desde perfil.
        var sessions = await context.RefreshTokens
            .Where(t => t.UserId == reset.UserId
                && t.UserType == reset.UserType
                && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAt = now;
        }

        auditLogger.Record(
            AuditAction.ChangePassword,
            // El super-admin no es dueño de ningun contenido: su recuperacion no
            // pertenece al audit log de ningun cliente.
            clientId: reset.UserType == UserType.Client ? reset.UserId : null,
            resourceType: reset.UserType == UserType.Client
                ? nameof(Client)
                : nameof(SuperAdmin),
            resourceId: reset.UserId,
            metadata: new
            {
                SelfService = true,
                Account = reset.UserType.ToString(),
                RevokedSessions = sessions.Count
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ApplyToClientAsync(
        Guid clientId,
        string passwordHash,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var client = await context.Clients
            .FirstOrDefaultAsync(
                c => c.Id == clientId && !c.IsDeleted && c.IsActive,
                cancellationToken);

        if (client is null)
        {
            return false;
        }

        client.PasswordHash = passwordHash;
        client.UpdatedAt = now;
        return true;
    }

    private async Task<bool> ApplyToSuperAdminAsync(
        Guid adminId,
        string passwordHash,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var admin = await context.SuperAdmins
            .FirstOrDefaultAsync(a => a.Id == adminId && a.IsActive, cancellationToken);

        if (admin is null)
        {
            return false;
        }

        admin.PasswordHash = passwordHash;
        admin.UpdatedAt = now;
        return true;
    }
}
