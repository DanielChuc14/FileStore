namespace FileStore.API.Contracts;

public record CreateFolderRequest(string Name, Guid? ParentId);

public record UpdateFolderRequest(string? Name, Guid? ParentId, bool MoveToRoot = false);

public record UpdateFileRequest(string? Name, Guid? FolderId, bool MoveToRoot = false);
