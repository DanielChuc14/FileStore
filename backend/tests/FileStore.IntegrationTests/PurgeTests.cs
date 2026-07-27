using System.Net.Http.Json;
using System.Text;
using FileStore.Application.Abstractions;
using FileStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre el barrido de la papelera (ITrashPurger). El job real corre por
/// temporizador y esta apagado en los tests; por eso la logica se extrajo a un
/// servicio invocable, que aqui se ejecuta a demanda. Se antedata el DeletedAt
/// accediendo al DbContext para simular el paso del tiempo sin esperar dias.
///
/// Es la regla que, si falla en silencio, deja bytes fantasma ocupando cuota que
/// nadie puede recuperar, o —peor— borra archivos que aun no vencieron.
/// </summary>
[Collection("Integration")]
public class PurgeTests(IntegrationTestFixture fixture)
{
    private static MultipartFormDataContent FileForm(string name, string content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", name);
        return form;
    }

    private record UploadedFile(Guid Id);

    private static async Task<Guid> UploadAndDeleteAsync(HttpClient client, string name, string content)
    {
        using var form = FileForm(name, content);
        var upload = await client.PostAsync("/v1/files", form);
        var file = await upload.Content.ReadFromJsonAsync<UploadedFile>();
        await client.DeleteAsync($"/v1/files/{file!.Id}");
        return file.Id;
    }

    private async Task<T> InScopeAsync<T>(Func<FileStoreDbContext, Task<T>> action)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FileStoreDbContext>();
        return await action(db);
    }

    /// <summary>Antedata el borrado y devuelve la ruta del binario de su version.</summary>
    private Task<string> BackdateAndGetPathAsync(Guid fileId, int daysAgo) =>
        InScopeAsync(async db =>
        {
            var file = await db.Files.FirstAsync(f => f.Id == fileId);
            file.DeletedAt = DateTime.UtcNow.AddDays(-daysAgo);
            await db.SaveChangesAsync();
            return await db.FileVersions
                .Where(v => v.FileId == fileId)
                .Select(v => v.StoragePath)
                .FirstAsync();
        });

    private async Task<TrashPurgeResult> RunPurgeAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var purger = scope.ServiceProvider.GetRequiredService<ITrashPurger>();
        return await purger.PurgeExpiredAsync(batchSize: 100);
    }

    private Task<bool> FileExistsAsync(Guid fileId) =>
        InScopeAsync(db => db.Files.AnyAsync(f => f.Id == fileId));

    private Task<long> UsedBytesAsync(Guid clientId) =>
        InScopeAsync(db => db.Clients.Where(c => c.Id == clientId).Select(c => c.UsedBytes).FirstAsync());

    private async Task<bool> BinaryExistsAsync(string storagePath)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
        return await storage.ExistsAsync(storagePath);
    }

    [Fact]
    public async Task Purga_ArchivoVencido_BorraFilaBinarioYLiberaCuota()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var fileId = await UploadAndDeleteAsync(client, "vencido.txt", "contenido a purgar");
        // Vencido: 60 dias en papelera contra la retencion global de 30.
        var path = await BackdateAndGetPathAsync(fileId, daysAgo: 60);

        Assert.True(await BinaryExistsAsync(path)); // precondicion: el binario existe

        var result = await RunPurgeAsync();

        Assert.True(result.PurgedCount >= 1);
        Assert.False(await FileExistsAsync(fileId));      // fila borrada
        Assert.False(await BinaryExistsAsync(path));      // binario borrado
        Assert.Equal(0, await UsedBytesAsync(clientId));  // cuota liberada
    }

    [Fact]
    public async Task Purga_ArchivoDentroDeRetencion_NoLoToca()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        // Recien borrado: dentro de la retencion de 30 dias, NO debe purgarse.
        var fileId = await UploadAndDeleteAsync(client, "reciente.txt", "contenido vigente");
        var usedAntes = await UsedBytesAsync(clientId);

        await RunPurgeAsync();

        Assert.True(await FileExistsAsync(fileId));            // sigue en la base
        Assert.Equal(usedAntes, await UsedBytesAsync(clientId)); // cuota intacta
    }

    [Fact]
    public async Task Purga_RespetaElOverrideDeRetencionPorCliente()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        // Override del cliente a 1 dia (la global es 30).
        await InScopeAsync<object?>(async db =>
        {
            var c = await db.Clients.FirstAsync(x => x.Id == clientId);
            c.TrashRetentionDays = 1;
            await db.SaveChangesAsync();
            return null;
        });

        var fileId = await UploadAndDeleteAsync(client, "override.txt", "contenido");
        // 2 dias: no venceria con la global (30), pero si con el override (1).
        await BackdateAndGetPathAsync(fileId, daysAgo: 2);

        await RunPurgeAsync();

        Assert.False(await FileExistsAsync(fileId));
    }
}
