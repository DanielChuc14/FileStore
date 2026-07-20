using FileStore.Application.Abstractions;
using FileStore.Application.Common;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Folders.Common;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Folders.Update;

/// <summary>
/// Renombrar y mover en una sola operacion. MoveToRoot distingue "no mover" de
/// "mover a la raiz", que con solo un ParentId nullable serian indistinguibles.
/// </summary>
public record UpdateFolderCommand(
    Guid Id,
    string? Name,
    Guid? ParentId,
    bool MoveToRoot = false) : IRequest<FolderDto>;

public class UpdateFolderCommandValidator : AbstractValidator<UpdateFolderCommand>
{
    public UpdateFolderCommandValidator()
    {
        RuleFor(c => c.Name)
            .Must(FileNameRules.IsValid)
            .WithMessage(c => FileNameRules.Validate(c.Name))
            .When(c => c.Name is not null);
    }
}

public class UpdateFolderCommandHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateFolderCommand, FolderDto>
{
    public async Task<FolderDto> Handle(
        UpdateFolderCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var folder = await context.Folders
            .FirstOrDefaultAsync(f => f.Id == request.Id && f.ClientId == clientId, cancellationToken)
            ?? throw new NotFoundException(nameof(Folder), request.Id);

        var oldPath = folder.Path;
        var newName = request.Name?.Trim() ?? folder.Name;
        var newParentId = request.MoveToRoot ? null : request.ParentId ?? folder.ParentFolderId;

        var isMoving = newParentId != folder.ParentFolderId;
        var isRenaming = newName != folder.Name;

        if (!isMoving && !isRenaming)
        {
            return ToDto(folder);
        }

        var newParentPath = "";

        if (newParentId.HasValue)
        {
            var parent = await context.Folders
                .Where(f => f.Id == newParentId && f.ClientId == clientId)
                .Select(f => new { f.Id, f.Path })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException(nameof(Folder), newParentId);

            // Mover una carpeta dentro de si misma o de un descendiente suyo
            // desconectaria ese subarbol del resto: quedaria inalcanzable.
            if (parent.Id == folder.Id || parent.Path.StartsWith($"{oldPath}/", StringComparison.Ordinal))
            {
                throw new ConflictException(
                    "No se puede mover una carpeta dentro de si misma o de una subcarpeta suya.");
            }

            newParentPath = parent.Path;
        }

        var duplicate = await context.Folders.AnyAsync(
            f => f.ClientId == clientId
                && f.ParentFolderId == newParentId
                && f.Name == newName
                && f.Id != folder.Id,
            cancellationToken);

        if (duplicate)
        {
            throw new ConflictException($"Ya existe una carpeta llamada '{newName}' en el destino.");
        }

        var newPath = $"{newParentPath}/{newName}";

        folder.Name = newName;
        folder.ParentFolderId = newParentId;
        folder.Path = newPath;

        // El Path de los descendientes es un denormalizado: hay que recalcularlo
        // o quedaria apuntando a una ruta que ya no existe. Se filtra por
        // "oldPath/" y no por "oldPath" para no arrastrar carpetas hermanas que
        // solo comparten prefijo (/docs no debe tocar /documentos).
        var descendants = await context.Folders
            .Where(f => f.ClientId == clientId && f.Path.StartsWith(oldPath + "/"))
            .ToListAsync(cancellationToken);

        foreach (var descendant in descendants)
        {
            descendant.Path = string.Concat(newPath, descendant.Path.AsSpan(oldPath.Length));
        }

        auditLogger.Record(
            isMoving ? AuditAction.MoveFolder : AuditAction.RenameFolder,
            clientId: clientId,
            resourceType: nameof(Folder),
            resourceId: folder.Id,
            metadata: new
            {
                OldPath = oldPath,
                NewPath = newPath,
                UpdatedDescendants = descendants.Count
            });

        await context.SaveChangesAsync(cancellationToken);

        return ToDto(folder);
    }

    private static FolderDto ToDto(Folder folder) => new(
        folder.Id,
        folder.ParentFolderId,
        folder.Name,
        folder.Path,
        folder.CreatedAt);
}
