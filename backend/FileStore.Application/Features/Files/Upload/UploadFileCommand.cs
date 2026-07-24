using System.Security.Cryptography;
using FileStore.Application.Abstractions;
using FileStore.Application.Common;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Files.Common;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileStore.Application.Features.Files.Upload;

public record UploadFileCommand(
    string FileName,
    long SizeBytes,
    Stream Content,
    Guid? FolderId) : IRequest<FileDto>;

public class UploadFileCommandHandler(
    IApplicationDbContext context,
    IStorageService storage,
    IAppConfigReader appConfig,
    ICurrentUser currentUser,
    IAuditLogger auditLogger,
    ILogger<UploadFileCommandHandler> logger)
    : IRequestHandler<UploadFileCommand, FileDto>
{
    public async Task<FileDto> Handle(
        UploadFileCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var fileName = request.FileName.Trim();

        if (FileNameRules.Validate(fileName) is { } nameError)
        {
            throw new ValidationFailedException(nameof(request.FileName), nameError);
        }

        var client = await context.Clients
            .Where(c => c.Id == clientId && !c.IsDeleted && c.IsActive)
            .Select(c => new { c.MaxFileSizeBytes })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidCredentialsException();

        // --- Validacion de extension y MIME ---
        var extension = FileNameRules.GetExtension(fileName);
        var mimeType = await appConfig.GetMimeTypeForExtensionAsync(extension, cancellationToken);

        if (mimeType is null)
        {
            throw new ValidationFailedException(
                nameof(request.FileName),
                $"La extension '.{extension}' no esta permitida.");
        }

        // --- Validacion de tamaño ---
        var maxFileSize = client.MaxFileSizeBytes
            ?? await appConfig.GetMaxFileSizeBytesAsync(cancellationToken);

        if (request.SizeBytes > maxFileSize)
        {
            throw new PayloadTooLargeException(
                $"El archivo supera el maximo permitido de {maxFileSize} bytes.");
        }

        if (request.FolderId.HasValue)
        {
            var folderExists = await context.Folders.AnyAsync(
                f => f.Id == request.FolderId && f.ClientId == clientId,
                cancellationToken);

            if (!folderExists)
            {
                throw new NotFoundException(nameof(Folder), request.FolderId);
            }
        }

        // --- Reserva atomica de cuota ---
        // Un solo UPDATE condicional en vez de leer, validar y escribir: dos
        // subidas simultaneas que juntas exceden la cuota no pueden pasar ambas,
        // porque la condicion se evalua dentro de la misma sentencia.
        var reserved = await context.Clients
            .Where(c => c.Id == clientId && c.UsedBytes + request.SizeBytes <= c.QuotaBytes)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.UsedBytes, c => c.UsedBytes + request.SizeBytes),
                cancellationToken);

        if (reserved == 0)
        {
            throw new PayloadTooLargeException(
                "El archivo no entra en la cuota disponible.");
        }

        var fileVersionId = Guid.CreateVersion7();
        string? storagePath = null;

        try
        {
            storagePath = await storage.SaveAsync(
                clientId, fileVersionId, request.Content, cancellationToken);

            var checksum = await ComputeChecksumAsync(storagePath, cancellationToken);

            // StoredFile y FileVersion se apuntan mutuamente (FileId y
            // CurrentVersionId), asi que EF no puede ordenar los INSERT si se
            // guardan juntos. Se parte en dos SaveChanges dentro de una
            // transaccion: primero el archivo con CurrentVersionId en null y su
            // version, despues el puntero a la version vigente.
            await using var transaction = await context.BeginTransactionAsync(cancellationToken);

            var file = await UpsertFileAsync(
                clientId, fileName, extension, mimeType, request, cancellationToken);

            var versionNumber = await context.FileVersions
                .Where(v => v.FileId == file.Id)
                .Select(v => (int?)v.VersionNumber)
                .MaxAsync(cancellationToken) ?? 0;

            var version = new FileVersion
            {
                Id = fileVersionId,
                FileId = file.Id,
                VersionNumber = versionNumber + 1,
                StoragePath = storagePath,
                SizeBytes = request.SizeBytes,
                MimeType = mimeType,
                ChecksumSha256 = checksum,
                UploadedByApiKeyId = currentUser.ApiKeyId,
                CreatedAt = DateTime.UtcNow
            };

            context.FileVersions.Add(version);

            await context.SaveChangesAsync(cancellationToken);

            file.CurrentVersionId = version.Id;
            file.SizeBytes = request.SizeBytes;
            file.MimeType = mimeType;
            file.UpdatedAt = DateTime.UtcNow;

            auditLogger.Record(
                AuditAction.Upload,
                clientId: clientId,
                resourceType: nameof(StoredFile),
                resourceId: file.Id,
                metadata: new
                {
                    file.OriginalName,
                    version.VersionNumber,
                    request.SizeBytes
                });

            await context.SaveChangesAsync(cancellationToken);
            await context.CommitTransactionAsync(cancellationToken);

            return ToDto(file, version.VersionNumber);
        }
        catch (Exception ex)
        {
            // Disco y base no comparten transaccion. Si la base falla despues de
            // escribir el binario, ese archivo queda ocupando espacio que nadie
            // contabiliza. Se borra y se devuelve la cuota reservada.
            logger.LogError(
                ex,
                "Fallo la subida de {FileName} para el cliente {ClientId}. " +
                "Revirtiendo cuota y binario.",
                fileName,
                clientId);

            if (storagePath is not null)
            {
                try
                {
                    await storage.DeleteAsync(storagePath, CancellationToken.None);
                }
                catch (Exception cleanupError)
                {
                    logger.LogError(
                        cleanupError,
                        "No se pudo borrar el binario huerfano en {StoragePath}.",
                        storagePath);
                }
            }

            await ReleaseQuotaAsync(clientId, request.SizeBytes);

            throw;
        }
    }

    private async Task<StoredFile> UpsertFileAsync(
        Guid clientId,
        string fileName,
        string extension,
        string mimeType,
        UploadFileCommand request,
        CancellationToken cancellationToken)
    {
        // Subir con el mismo nombre en la misma carpeta genera una version nueva
        // del archivo existente, no un archivo duplicado (seccion 6).
        var existing = await context.Files.FirstOrDefaultAsync(
            f => f.ClientId == clientId
                && f.FolderId == request.FolderId
                && f.OriginalName == fileName
                && !f.IsDeleted,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var file = new StoredFile
        {
            Id = Guid.CreateVersion7(),
            ClientId = clientId,
            FolderId = request.FolderId,
            OriginalName = fileName,
            SizeBytes = request.SizeBytes,
            MimeType = mimeType,
            Extension = extension,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Files.Add(file);
        return file;
    }

    /// <summary>
    /// Se calcula releyendo el binario ya escrito y no desde el stream de
    /// entrada: no exige que ese stream sea reposicionable, y de paso verifica
    /// que lo que quedo en disco se puede leer.
    /// </summary>
    private async Task<string> ComputeChecksumAsync(
        string storagePath,
        CancellationToken cancellationToken)
    {
        await using var stream = await storage.OpenReadAsync(storagePath, cancellationToken);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task ReleaseQuotaAsync(Guid clientId, long sizeBytes)
    {
        try
        {
            await context.Clients
                .Where(c => c.Id == clientId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.UsedBytes, c => c.UsedBytes - sizeBytes),
                    CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Si esto falla, el cliente queda con cuota consumida de mas. Se
            // registra para poder corregirlo: es preferible a enmascararlo.
            logger.LogError(
                ex,
                "No se pudo liberar {SizeBytes} bytes de cuota del cliente {ClientId}.",
                sizeBytes,
                clientId);
        }
    }

    private static FileDto ToDto(StoredFile file, int versionCount) => new(
        file.Id,
        file.FolderId,
        file.OriginalName,
        file.SizeBytes,
        file.MimeType,
        file.Extension,
        versionCount,
        file.IsDeleted,
        file.DeletedAt,
        file.CreatedAt,
        file.UpdatedAt);
}
