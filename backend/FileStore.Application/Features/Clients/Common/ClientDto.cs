using FileStore.Domain.Entities;

namespace FileStore.Application.Features.Clients.Common;

public record ClientDto(
    Guid Id,
    string Email,
    string Name,
    long QuotaBytes,
    long UsedBytes,
    bool IsActive,
    int? TrashRetentionDays,
    long? MaxFileSizeBytes,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static ClientDto From(Client client) => new(
        client.Id,
        client.Email,
        client.Name,
        client.QuotaBytes,
        client.UsedBytes,
        client.IsActive,
        client.TrashRetentionDays,
        client.MaxFileSizeBytes,
        client.CreatedAt,
        client.UpdatedAt);
}

// CreateClientResult se elimino: la contraseña generada ya no vuelve en la
// respuesta HTTP. Va por correo directo al cliente, y asi ni el super-admin ni
// el log del panel llegan a verla. Si se pierde, se resetea.
