using FileStore.Application.Abstractions;
using FileStore.Application.Common.Emails;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileStore.Application.Features.Auth.ForgotPassword;

/// <summary>
/// Pide un enlace de recuperacion. No devuelve nada y NUNCA falla por email
/// desconocido: responder distinto segun exista o no la cuenta convertiria este
/// endpoint en un enumerador de cuentas registradas.
///
/// Sirve a clientes y al super-admin. Para este ultimo es la unica via de
/// recuperacion que no pasa por tocar la base a mano.
/// </summary>
public record ForgotPasswordCommand(string Email, string? IpAddress) : IRequest;

public class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        // Se valida el formato, no la existencia: un email mal formado es un
        // error del cliente HTTP, no una pista sobre quien esta registrado.
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}

public class ForgotPasswordCommandHandler(
    IApplicationDbContext context,
    ISecureTokenGenerator tokenGenerator,
    IEmailQueue emailQueue,
    IAppUrlProvider urls,
    ILogger<ForgotPasswordCommandHandler> logger)
    : IRequestHandler<ForgotPasswordCommand>
{
    /// <summary>
    /// Ventana de canje. Corta a proposito: el enlace da acceso total a la
    /// cuenta, y un correo reenviado o filtrado meses despues no deberia servir.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public async Task Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        // Va primero, antes incluso de mirar si la cuenta existe: la respuesta
        // depende solo de la configuracion, igual para cualquier email, asi que
        // no abre ninguna via para enumerar cuentas. Sin esto el usuario se
        // quedaria esperando un correo que nunca iba a salir.
        if (!emailQueue.IsDeliveryConfigured)
        {
            throw new EmailNotConfiguredException("enviar el enlace de recuperacion");
        }

        var email = request.Email.Trim().ToLowerInvariant();

        var account = await ResolveAccountAsync(email, cancellationToken);

        if (account is null)
        {
            // Se registra para poder investigar un patron de sondeo, pero la
            // respuesta al cliente HTTP es identica al caso exitoso.
            logger.LogInformation(
                "Recuperacion solicitada para un email sin cuenta activa. IP {Ip}.",
                request.IpAddress);
            return;
        }

        var (userId, userType, name) = account.Value;

        // Los pendientes se invalidan: pedir un enlace nuevo tiene que dejar sin
        // efecto los anteriores, o cada solicitud sumaria una llave viva mas.
        var pending = await context.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UserType == userType && t.UsedAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var old in pending)
        {
            old.UsedAt = now;
        }

        var (token, tokenHash) = tokenGenerator.Generate();

        context.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            UserType = userType,
            TokenHash = tokenHash,
            ExpiresAt = now.Add(Lifetime),
            CreatedAt = now,
            CreatedByIp = request.IpAddress
        });

        emailQueue.Enqueue(
            EmailTemplates.PasswordResetLink(
                email,
                name,
                urls.BuildPasswordResetUrl(token),
                (int)Lifetime.TotalMinutes),
            // Solo se asocia a un cliente cuando lo es: el super-admin no tiene
            // ClientId, y esa columna del outbox es de trazabilidad, no una FK.
            userType == UserType.Client ? userId : null);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Resuelve el email contra las dos tablas de cuentas. El orden no puede
    /// producir ambiguedad: el alta de clientes rechaza un email que ya exista
    /// como super-admin, justamente para que el login sepa contra que tabla
    /// resolver.
    /// </summary>
    private async Task<(Guid Id, UserType Type, string Name)?> ResolveAccountAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var admin = await context.SuperAdmins
            .Where(a => a.Email == email && a.IsActive)
            .Select(a => new { a.Id, a.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (admin is not null)
        {
            return (admin.Id, UserType.SuperAdmin, admin.Name);
        }

        var client = await context.Clients
            .Where(c => c.Email == email && !c.IsDeleted && c.IsActive)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken);

        return client is null ? null : (client.Id, UserType.Client, client.Name);
    }
}
