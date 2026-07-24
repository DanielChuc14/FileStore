using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileStore.Application.Abstractions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FileStore.Infrastructure.Persistence;

/// <summary>
/// Siembra datos de demostracion (clientes, API Keys, carpetas, archivos con
/// versiones, papelera y auditoria) para poblar el panel en un entorno de vitrina
/// o pruebas. NO es datos de referencia: por eso no va en HasData y esta detras de
/// un flag. Solo corre si Seed:Demo=true y la base aun no tiene clientes.
///
/// Inserta las entidades directamente en vez de pasar por los handlers de MediatR:
/// esos dependen de ICurrentUser, que se resuelve de un request HTTP inexistente en
/// el arranque. Aun asi respeta las invariantes reales: escribe binarios de verdad
/// con IStorageService, UsedBytes queda igual a la suma de versiones, y el
/// versionado es incremental.
/// </summary>
public class DemoDataSeeder(
    FileStoreDbContext context,
    IConfiguration configuration,
    IPasswordHasher passwordHasher,
    IApiKeyGenerator apiKeyGenerator,
    IStorageService storage,
    ILogger<DemoDataSeeder> logger)
{
    private const long Kb = 1024;
    private const long Mb = 1024 * Kb;

    // IP del rango de documentacion (RFC 5737): no es una direccion real.
    private const string DemoIp = "203.0.113.10";
    private const string DemoAgent = "DemoSeeder/1.0";

    // PNG valido de 1x1 pixel transparente. Se usa tal cual para que un logo.png
    // demo se pueda abrir de verdad, a diferencia de un binario inventado.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private Guid _superAdminId;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Seed:Demo"))
        {
            return;
        }

        if (await context.Clients.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Datos demo omitidos: la base ya tiene clientes.");
            return;
        }

        // Actor de las acciones administrativas (crear clientes). Si por alguna
        // razon no hay super-admin, queda Guid.Empty: el seed no se bloquea por eso.
        _superAdminId = await context.SuperAdmins
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var password = configuration["Seed:DemoPassword"] ?? "Demo1234!";
        var credentials = new List<(string Email, string Key)>();

        // Fecha base ~2 semanas atras: las acciones se reparten en el tiempo para
        // que las series del dashboard muestren una curva y no un solo pico.
        var t0 = DateTime.UtcNow.AddDays(-14);

        // --- Cliente 1: consumo alto (~82% de 10 MB), dispara el aviso del 80% ---
        var acme = await CreateClientAsync(
            "demo-acme@filestore.local", "Acme Design", 10 * Mb, password, t0, cancellationToken);
        var (acmeKey, acmeKeyValue) = await CreateApiKeyAsync(acme, "produccion", t0.AddHours(2), cancellationToken);
        credentials.Add((acme.Email, acmeKeyValue));

        var documentos = AddFolder(acme, "Documentos", null, t0.AddHours(3));
        var anio2026 = AddFolder(acme, "2026", documentos, t0.AddHours(4));
        var imagenes = AddFolder(acme, "Imagenes", null, t0.AddHours(5));
        await context.SaveChangesAsync(cancellationToken);

        // Archivo con 3 versiones: mismas carpeta y nombre, subido por API Key.
        await SeedFileAsync(acme, documentos.Id, "manual-usuario.txt", "txt", "text/plain",
            TextBlob("Manual de usuario - v1", 1500 * Kb), t0.AddDays(1), acmeKey, trashed: false, cancellationToken);
        await SeedFileAsync(acme, documentos.Id, "manual-usuario.txt", "txt", "text/plain",
            TextBlob("Manual de usuario - v2", 1600 * Kb), t0.AddDays(4), acmeKey, trashed: false, cancellationToken);
        await SeedFileAsync(acme, documentos.Id, "manual-usuario.txt", "txt", "text/plain",
            TextBlob("Manual de usuario - v3", 1700 * Kb), t0.AddDays(7), acmeKey, trashed: false, cancellationToken);

        await SeedFileAsync(acme, anio2026.Id, "reporte-anual.csv", "csv", "text/csv",
            TextBlob("periodo,ingresos,egresos", 2 * Mb), t0.AddDays(2), null, trashed: false, cancellationToken);
        await SeedFileAsync(acme, documentos.Id, "notas.txt", "txt", "text/plain",
            TextBlob("Notas internas", 512 * Kb), t0.AddDays(3), null, trashed: false, cancellationToken);
        await SeedFileAsync(acme, imagenes.Id, "logo.png", "png", "image/png",
            OnePixelPng, t0.AddDays(5), null, trashed: false, cancellationToken);

        // Archivo en papelera: sigue contando para la cuota (decision del proyecto).
        await SeedFileAsync(acme, null, "borrador.txt", "txt", "text/plain",
            TextBlob("Borrador descartado", 1 * Mb), t0.AddDays(6), null, trashed: true, cancellationToken);

        // --- Cliente 2: consumo medio (~35% de 20 MB) ---
        var beta = await CreateClientAsync(
            "demo-beta@filestore.local", "Beta Labs", 20 * Mb, password, t0.AddHours(6), cancellationToken);
        var (betaKey, betaKeyValue) = await CreateApiKeyAsync(beta, "integracion", t0.AddHours(7), cancellationToken);
        credentials.Add((beta.Email, betaKeyValue));

        var reportes = AddFolder(beta, "Reportes", null, t0.AddHours(8));
        await context.SaveChangesAsync(cancellationToken);

        await SeedFileAsync(beta, reportes.Id, "ventas-q1.csv", "csv", "text/csv",
            TextBlob("mes,unidades", 3 * Mb), t0.AddDays(3), betaKey, trashed: false, cancellationToken);
        await SeedFileAsync(beta, reportes.Id, "ventas-q2.csv", "csv", "text/csv",
            TextBlob("mes,unidades", 3 * Mb), t0.AddDays(8), betaKey, trashed: false, cancellationToken);
        await SeedFileAsync(beta, null, "contrato.txt", "txt", "text/plain",
            TextBlob("Contrato de servicio", 1 * Mb), t0.AddDays(9), null, trashed: false, cancellationToken);

        // --- Cliente 3: consumo bajo (~1% de 50 MB), contrasta en el ranking ---
        var gamma = await CreateClientAsync(
            "demo-gamma@filestore.local", "Gamma Studio", 50 * Mb, password, t0.AddHours(9), cancellationToken);
        var (_, gammaKeyValue) = await CreateApiKeyAsync(gamma, "default", t0.AddHours(10), cancellationToken);
        credentials.Add((gamma.Email, gammaKeyValue));

        await SeedFileAsync(gamma, null, "bienvenida.txt", "txt", "text/plain",
            TextBlob("Bienvenido a FileStore", 512 * Kb), t0.AddDays(10), null, trashed: false, cancellationToken);
        await SeedFileAsync(gamma, null, "logo.png", "png", "image/png",
            OnePixelPng, t0.AddDays(11), null, trashed: false, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        LogSummary(password, credentials);
    }

    private async Task<Client> CreateClientAsync(
        string email, string name, long quotaBytes, string password, DateTime at, CancellationToken ct)
    {
        var client = new Client
        {
            Id = Guid.CreateVersion7(),
            Email = email.ToLowerInvariant(),
            Name = name,
            PasswordHash = passwordHasher.Hash(password),
            QuotaBytes = quotaBytes,
            UsedBytes = 0,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = at,
            UpdatedAt = at
        };

        context.Clients.Add(client);
        await context.SaveChangesAsync(ct);

        Audit(client.Id, ActorType.SuperAdmin, _superAdminId, AuditAction.CreateClient,
            nameof(Client), client.Id, new { client.Email, client.QuotaBytes }, at);
        Audit(client.Id, ActorType.Client, client.Id, AuditAction.Login,
            null, null, null, at.AddHours(1));

        return client;
    }

    private async Task<(ApiKey Key, string Value)> CreateApiKeyAsync(
        Client client, string name, DateTime at, CancellationToken ct)
    {
        var generated = apiKeyGenerator.Generate();

        var key = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            ClientId = client.Id,
            Name = name,
            KeyHash = generated.Hash,
            Prefix = generated.Prefix,
            IsActive = true,
            CreatedAt = at
        };

        context.ApiKeys.Add(key);
        Audit(client.Id, ActorType.Client, client.Id, AuditAction.CreateApiKey,
            nameof(ApiKey), key.Id, new { key.Name, key.Prefix }, at);
        await context.SaveChangesAsync(ct);

        return (key, generated.Value);
    }

    private Folder AddFolder(Client client, string name, Folder? parent, DateTime at)
    {
        var folder = new Folder
        {
            Id = Guid.CreateVersion7(),
            ClientId = client.Id,
            ParentFolderId = parent?.Id,
            Name = name,
            Path = (parent?.Path ?? string.Empty) + "/" + name,
            CreatedAt = at
        };

        context.Folders.Add(folder);
        Audit(client.Id, ActorType.Client, client.Id, AuditAction.CreateFolder,
            nameof(Folder), folder.Id, new { folder.Name, folder.Path }, at);

        return folder;
    }

    /// <summary>
    /// Replica el camino real de un upload: escribe el binario, crea StoredFile +
    /// FileVersion (resolviendo la FK circular con dos SaveChanges), incrementa la
    /// cuota y registra la auditoria. Re-invocar con el mismo (folder, nombre) crea
    /// una version nueva, como en produccion.
    /// </summary>
    private async Task SeedFileAsync(
        Client client, Guid? folderId, string name, string extension, string mimeType,
        byte[] content, DateTime at, ApiKey? viaApiKey, bool trashed, CancellationToken ct)
    {
        var versionId = Guid.CreateVersion7();
        string storagePath;

        await using (var stream = new MemoryStream(content))
        {
            // La ruta la decide el storage; se toma de su retorno en vez de
            // reconstruirla, para no duplicar su formato interno.
            storagePath = await storage.SaveAsync(client.Id, versionId, stream, ct);
        }

        var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var file = await context.Files.FirstOrDefaultAsync(
            f => f.ClientId == client.Id && f.FolderId == folderId
                && f.OriginalName == name && !f.IsDeleted, ct);

        var isNew = file is null;
        var versionNumber = 1;

        if (isNew)
        {
            file = new StoredFile
            {
                Id = Guid.CreateVersion7(),
                ClientId = client.Id,
                FolderId = folderId,
                OriginalName = name,
                CurrentVersionId = null,
                SizeBytes = content.Length,
                MimeType = mimeType,
                Extension = extension,
                IsDeleted = false,
                CreatedAt = at,
                UpdatedAt = at
            };
            context.Files.Add(file);
        }
        else
        {
            versionNumber = (await context.FileVersions
                .Where(v => v.FileId == file!.Id)
                .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0) + 1;
        }

        var version = new FileVersion
        {
            Id = versionId,
            FileId = file!.Id,
            VersionNumber = versionNumber,
            StoragePath = storagePath,
            SizeBytes = content.Length,
            MimeType = mimeType,
            ChecksumSha256 = checksum,
            UploadedByApiKeyId = viaApiKey?.Id,
            CreatedAt = at
        };

        context.FileVersions.Add(version);
        await context.SaveChangesAsync(ct);

        // Segundo SaveChanges: recien ahora el puntero a la version vigente puede
        // apuntar a una fila que ya existe (la FK es circular con StoredFile).
        file.CurrentVersionId = version.Id;
        file.SizeBytes = content.Length;
        file.MimeType = mimeType;
        file.UpdatedAt = at;

        client.UsedBytes += content.Length;

        var actorType = viaApiKey is null ? ActorType.Client : ActorType.ApiKey;
        var actorId = viaApiKey?.Id ?? client.Id;
        Audit(client.Id, actorType, actorId, AuditAction.Upload,
            nameof(StoredFile), file.Id, new { file.OriginalName, version.VersionNumber, content.Length }, at);

        if (trashed)
        {
            var deletedAt = at.AddDays(1);
            file.IsDeleted = true;
            file.DeletedAt = deletedAt;
            Audit(client.Id, ActorType.Client, client.Id, AuditAction.Delete,
                nameof(StoredFile), file.Id, new { file.OriginalName }, deletedAt);
        }

        await context.SaveChangesAsync(ct);
    }

    private void Audit(
        Guid? clientId, ActorType actorType, Guid actorId, AuditAction action,
        string? resourceType, Guid? resourceId, object? metadata, DateTime at)
    {
        context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ClientId = clientId,
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata),
            IpAddress = DemoIp,
            UserAgent = DemoAgent,
            CreatedAt = at
        });
    }

    /// <summary>Genera un blob de texto de tamaño exacto en bytes (todo ASCII).</summary>
    private static byte[] TextBlob(string header, long sizeBytes)
    {
        var target = (int)sizeBytes;
        var sb = new StringBuilder(target + 64);
        sb.Append(header).Append('\n');

        const string filler = "Contenido de demostracion generado automaticamente por el seeder. ";
        while (sb.Length < target)
        {
            sb.Append(filler);
        }

        return Encoding.ASCII.GetBytes(sb.ToString(0, target));
    }

    private void LogSummary(string password, List<(string Email, string Key)> credentials)
    {
        logger.LogWarning("=== Datos demo sembrados (solo desarrollo/vitrina) ===");
        logger.LogWarning("Contrasena de todos los clientes demo: {Password}", password);

        foreach (var (email, key) in credentials)
        {
            // El valor completo de la API Key solo existe en este momento; se
            // registra a proposito para poder probar la API con curl en la demo.
            logger.LogWarning("Cliente {Email} -> API Key: {Key}", email, key);
        }
    }
}
