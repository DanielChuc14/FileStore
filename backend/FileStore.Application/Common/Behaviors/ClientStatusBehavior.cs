using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Common.Behaviors;

/// <summary>
/// Corta el request si el cliente dueño de los datos esta desactivado o dado de
/// baja.
///
/// El esquema de API Key ya hace esta comprobacion al autenticar, pero el JWT no
/// consulta la base: valida la firma y confia en los claims. Sin esto, un access
/// token emitido antes de la baja sigue operando hasta que expira (15 minutos),
/// y en esa ventana el cliente puede listar, descargar y borrar. Revocar los
/// refresh tokens no lo impide: eso solo bloquea la renovacion, no invalida el
/// access token ya emitido.
///
/// Va en el pipeline y no en cada handler a proposito: repartido por handler se
/// olvida en el siguiente que se escriba. De hecho asi estaba, y solo Upload,
/// GetProfile y ChangePassword lo verificaban.
/// </summary>
public class ClientStatusBehavior<TRequest, TResponse>(
    IApplicationDbContext context,
    ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId;

        // Sin claim client_id no hay dueño que validar: es el super-admin, o un
        // request anonimo como el login.
        if (clientId is null)
        {
            return await next();
        }

        // Las peticiones con API Key ya pasaron por ApiKeyAuthenticationHandler,
        // que comprueba exactamente esto al autenticar. Repetirlo seria una
        // consulta de mas en cada llamada de la API.
        if (currentUser.ApiKeyId is not null)
        {
            return await next();
        }

        var isUsable = await context.Clients
            .AnyAsync(
                c => c.Id == clientId && c.IsActive && !c.IsDeleted,
                cancellationToken);

        if (!isUsable)
        {
            throw new InvalidCredentialsException("Client account is not active.");
        }

        return await next();
    }
}
