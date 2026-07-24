namespace FileStore.Domain.Enums;

public enum AuditAction
{
    // Archivos
    Upload = 1,
    Download = 2,
    Delete = 3,
    Move = 4,
    Rename = 5,
    Restore = 6,
    HardDelete = 7,
    RestoreVersion = 8,

    // Carpetas
    CreateFolder = 20,
    RenameFolder = 21,
    MoveFolder = 22,
    DeleteFolder = 23,

    // API Keys
    CreateApiKey = 40,
    RotateApiKey = 41,
    RevokeApiKey = 42,
    UpdateApiKey = 43,

    // Cuenta y administracion
    Login = 60,
    ChangePassword = 61,
    CreateClient = 62,
    UpdateClient = 63,
    BlockClient = 64,
    DeleteClient = 65,
    UpdateConfig = 66
}
