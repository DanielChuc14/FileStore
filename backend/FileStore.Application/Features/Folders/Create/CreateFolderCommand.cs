using FileStore.Application.Abstractions;
using FileStore.Application.Common;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Folders.Common;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Folders.Create;

public record CreateFolderCommand(string Name, Guid? ParentId) : IRequest<FolderDto>;

public class CreateFolderCommandValidator : AbstractValidator<CreateFolderCommand>
{
    public CreateFolderCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty()
            .MaximumLength(FileNameRules.MaxLength)
            .Must(FileNameRules.IsValid)
            .WithMessage(c => FileNameRules.Validate(c.Name));
    }
}

public class CreateFolderCommandHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<CreateFolderCommand, FolderDto>
{
    public async Task<FolderDto> Handle(
        CreateFolderCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();
        var name = request.Name.Trim();

        var parentPath = "";

        if (request.ParentId.HasValue)
        {
            // El padre se busca filtrando por cliente: no se puede colgar una
            // carpeta del arbol de otro.
            var parent = await context.Folders
                .Where(f => f.Id == request.ParentId && f.ClientId == clientId)
                .Select(f => new { f.Path })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException(nameof(Folder), request.ParentId);

            parentPath = parent.Path;
        }

        var duplicate = await context.Folders.AnyAsync(
            f => f.ClientId == clientId
                && f.ParentFolderId == request.ParentId
                && f.Name == name,
            cancellationToken);

        if (duplicate)
        {
            throw new ConflictException($"Ya existe una carpeta llamada '{name}' en esa ubicacion.");
        }

        var folder = new Folder
        {
            Id = Guid.CreateVersion7(),
            ClientId = clientId,
            ParentFolderId = request.ParentId,
            Name = name,
            Path = $"{parentPath}/{name}",
            CreatedAt = DateTime.UtcNow
        };

        context.Folders.Add(folder);

        auditLogger.Record(
            AuditAction.CreateFolder,
            clientId: clientId,
            resourceType: nameof(Folder),
            resourceId: folder.Id,
            metadata: new { folder.Name, folder.Path });

        await context.SaveChangesAsync(cancellationToken);

        return new FolderDto(
            folder.Id,
            folder.ParentFolderId,
            folder.Name,
            folder.Path,
            folder.CreatedAt);
    }
}
