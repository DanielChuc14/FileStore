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

/// <summary>
/// La contraseña generada viaja solo en esta respuesta y nunca mas: en la base
/// queda unicamente su hash. Si se pierde, hay que resetearla.
/// </summary>
public record CreateClientResult(ClientDto Client, string GeneratedPassword);
