namespace FileStore.Application.Features.Folders.Common;

public record FolderDto(
    Guid Id,
    Guid? ParentFolderId,
    string Name,
    string Path,
    DateTime CreatedAt);
