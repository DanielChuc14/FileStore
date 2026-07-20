using FileStore.Application.Abstractions;
using FileStore.Application.Common;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Files.Common;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Files.Update;

/// <summary>Renombrar y mover. MoveToRoot distingue "no mover" de "mover a la raiz".</summary>
public record UpdateFileCommand(
    Guid Id,
    string? Name,
    Guid? FolderId,
    bool MoveToRoot = false) : IRequest<FileDto>;

public class UpdateFileCommandValidator : AbstractValidator<UpdateFileCommand>
{
    public UpdateFileCommandValidator()
    {
        RuleFor(c => c.Name)
            .Must(FileNameRules.IsValid)
            .WithMessage(c => FileNameRules.Validate(c.Name))
            .When(c => c.Name is not null);
    }
}

public class UpdateFileCommandHandler(
    IApplicationDbContext context,
    IAppConfigReader appConfig,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateFileCommand, FileDto>
{
    public async Task<FileDto> Handle(
        UpdateFileCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var file = await context.Files
            .FirstOrDefaultAsync(
                f => f.Id == request.Id && f.ClientId == clientId && !f.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException(nameof(StoredFile), request.Id);

        var newName = request.Name?.Trim() ?? file.OriginalName;
        var newFolderId = request.MoveToRoot ? null : request.FolderId ?? file.FolderId;

        var isRenaming = newName != file.OriginalName;
        var isMoving = newFolderId != file.FolderId;

        if (!isRenaming && !isMoving)
        {
            return await ToDtoAsync(file, cancellationToken);
        }

        if (isRenaming)
        {
            // Renombrar no puede usarse para saltear la whitelist: cambiar
            // "informe.pdf" a "script.exe" tiene que fallar.
            var extension = FileNameRules.GetExtension(newName);
            var mimeType = await appConfig.GetMimeTypeForExtensionAsync(extension, cancellationToken);

            if (mimeType is null)
            {
                throw new ValidationFailedException(
                    nameof(request.Name),
                    $"La extension '.{extension}' no esta permitida.");
            }

            file.Extension = extension;
            file.MimeType = mimeType;
        }

        if (isMoving && newFolderId.HasValue)
        {
            var folderExists = await context.Folders.AnyAsync(
                f => f.Id == newFolderId && f.ClientId == clientId,
                cancellationToken);

            if (!folderExists)
            {
                throw new NotFoundException(nameof(Folder), newFolderId);
            }
        }

        var duplicate = await context.Files.AnyAsync(
            f => f.ClientId == clientId
                && f.FolderId == newFolderId
                && f.OriginalName == newName
                && f.Id != file.Id
                && !f.IsDeleted,
            cancellationToken);

        if (duplicate)
        {
            throw new ConflictException($"Ya existe un archivo llamado '{newName}' en el destino.");
        }

        var oldName = file.OriginalName;
        file.OriginalName = newName;
        file.FolderId = newFolderId;
        file.UpdatedAt = DateTime.UtcNow;

        auditLogger.Record(
            isMoving ? AuditAction.Move : AuditAction.Rename,
            clientId: clientId,
            resourceType: nameof(StoredFile),
            resourceId: file.Id,
            metadata: new { OldName = oldName, NewName = newName, NewFolderId = newFolderId });

        await context.SaveChangesAsync(cancellationToken);

        return await ToDtoAsync(file, cancellationToken);
    }

    private async Task<FileDto> ToDtoAsync(StoredFile file, CancellationToken cancellationToken)
    {
        var versionCount = await context.FileVersions
            .CountAsync(v => v.FileId == file.Id, cancellationToken);

        return new FileDto(
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
}
