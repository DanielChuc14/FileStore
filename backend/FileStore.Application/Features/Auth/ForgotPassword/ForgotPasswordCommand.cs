using FileStore.Application.Abstractions;
using FileStore.Application.Common.Emails;
using FileStore.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileStore.Application.Features.Auth.ForgotPassword;

/// <summary>
/// Pide un enlace de recuperacion. No devuelve nada y NUNCA falla por email
/// desconocido: responder distinto segun exista o no la cuenta convertiria este
/// endpoint en un enumerador de clientes registrados.
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
        var email = request.Email.Trim().ToLowerInvariant();

        var client = await context.Clients
            .FirstOrDefaultAsync(
                c => c.Email == email && !c.IsDeleted && c.IsActive,
                cancellationToken);

        if (client is null)
        {
            // Se registra para poder investigar un patron de sondeo, pero la
            // respuesta al cliente HTTP es identica al caso exitoso.
            logger.LogInformation(
                "Recuperacion solicitada para un email sin cuenta activa. IP {Ip}.",
                request.IpAddress);
            return;
        }

        // Los pendientes se invalidan: pedir un enlace nuevo tiene que dejar sin
        // efecto los anteriores, o cada solicitud sumaria una llave viva mas.
        var pending = await context.PasswordResetTokens
            .Where(t => t.ClientId == client.Id && t.UsedAt == null)
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
            ClientId = client.Id,
            TokenHash = tokenHash,
            ExpiresAt = now.Add(Lifetime),
            CreatedAt = now,
            CreatedByIp = request.IpAddress
        });

        emailQueue.Enqueue(
            EmailTemplates.PasswordResetLink(
                client.Email,
                client.Name,
                urls.BuildPasswordResetUrl(token),
                (int)Lifetime.TotalMinutes),
            client.Id);

        await context.SaveChangesAsync(cancellationToken);
    }
}
