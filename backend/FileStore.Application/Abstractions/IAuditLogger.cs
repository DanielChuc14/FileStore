using FileStore.Domain.Enums;

namespace FileStore.Application.Abstractions;

public interface IAuditLogger
{
    /// <summary>
    /// Encola una entrada de auditoria en el DbContext SIN guardar. El SaveChanges
    /// lo hace el handler, de modo que la entrada y la operacion auditada viajan
    /// en la misma transaccion: o quedan las dos, o no queda ninguna. Auditar en
    /// una transaccion aparte permitiria registrar algo que despues falla.
    /// </summary>
    void Record(
        AuditAction action,
        Guid? clientId = null,
        string? resourceType = null,
        Guid? resourceId = null,
        object? metadata = null);
}
