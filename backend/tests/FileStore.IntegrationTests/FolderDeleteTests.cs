using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre el borrado de carpetas, que es el handler con mas comportamiento
/// implicito del modulo: sin recursive rechaza una carpeta con contenido, con
/// recursive manda los archivos a la papelera en vez de destruirlos, conserva la
/// fecha de borrado original de los que ya estaban ahi (o se les reiniciaria el
/// plazo de purga) y desvincula FolderId antes de borrar la fila, o el DELETE
/// falla por la clave foranea.
/// </summary>
[Collection("Integration")]
public class FolderDeleteTests(IntegrationTestFixture fixture)
{
    private record FolderDto(Guid Id, Guid? ParentFolderId, string Name, string Path, DateTime CreatedAt);
    private record UploadedFile(Guid Id, string OriginalName);
    private record TrashItemDto(
        Guid Id, string OriginalName, long SizeBytes, string Extension,
        DateTime DeletedAt, DateTime PurgeAt, int DaysUntilPurge);
    private record UsageResponse(long UsedBytes, long QuotaBytes, int FilesCount);

    private static async Task<FolderDto> CreateFolderAsync(HttpClient client, string name, Guid? parentId)
    {
        var response = await client.PostAsJsonAsync("/folders", new { name, parentId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FolderDto>())!;
    }

    private static async Task<UploadedFile> UploadAsync(HttpClient client, string name, Guid? folderId)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("contenido de prueba")), "file", name);

        var url = folderId is null ? "/files" : $"/files?folderId={folderId}";
        var response = await client.PostAsync(url, form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadedFile>())!;
    }

    private static async Task<List<FolderDto>> ListFoldersAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<FolderDto>>("/folders?all=true"))!;

    private static async Task<List<TrashItemDto>> ListTrashAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TrashItemDto>>("/trash"))!;

    [Fact]
    public async Task Delete_CarpetaVacia_SinRecursive_Funciona()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var folder = await CreateFolderAsync(client, "vacia", null);

        var response = await client.DeleteAsync($"/folders/{folder.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var remaining = await ListFoldersAsync(client);
        Assert.DoesNotContain(remaining, f => f.Id == folder.Id);
    }

    [Fact]
    public async Task Delete_ConArchivosDentro_SinRecursive_Devuelve409()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var folder = await CreateFolderAsync(client, "con-archivos", null);
        await UploadAsync(client, "factura.txt", folder.Id);

        var response = await client.DeleteAsync($"/folders/{folder.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // La carpeta sigue existiendo: el 409 no debe dejar nada a medias.
        var remaining = await ListFoldersAsync(client);
        Assert.Contains(remaining, f => f.Id == folder.Id);
    }

    [Fact]
    public async Task Delete_ConSubcarpetas_SinRecursive_Devuelve409()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var padre = await CreateFolderAsync(client, "padre", null);
        await CreateFolderAsync(client, "hija", padre.Id);

        // Una subcarpeta vacia tambien cuenta como contenido.
        var response = await client.DeleteAsync($"/folders/{padre.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Recursive_BorraElSubarbolYMandaLosArchivosALaPapelera()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var docs = await CreateFolderAsync(client, "docs", null);
        var anio = await CreateFolderAsync(client, "2026", docs.Id);
        var otra = await CreateFolderAsync(client, "sin-relacion", null);

        var raiz = await UploadAsync(client, "en-docs.txt", docs.Id);
        var hondo = await UploadAsync(client, "en-2026.txt", anio.Id);

        var response = await client.DeleteAsync($"/folders/{docs.Id}?recursive=true");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Desaparecen la carpeta y su descendencia, pero no las ajenas.
        var remaining = await ListFoldersAsync(client);
        Assert.DoesNotContain(remaining, f => f.Id == docs.Id);
        Assert.DoesNotContain(remaining, f => f.Id == anio.Id);
        Assert.Contains(remaining, f => f.Id == otra.Id);

        // Los archivos no se destruyen: van a la papelera, a cualquier
        // profundidad del subarbol.
        var trash = await ListTrashAsync(client);
        Assert.Contains(trash, t => t.Id == raiz.Id);
        Assert.Contains(trash, t => t.Id == hondo.Id);

        // Y siguen siendo recuperables, que es el sentido de no borrarlos.
        var restore = await client.PostAsync($"/trash/{raiz.Id}/restore", null);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
    }

    [Fact]
    public async Task Delete_Recursive_LosArchivosSiguenOcupandoCuota()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var folder = await CreateFolderAsync(client, "docs", null);
        await UploadAsync(client, "ocupa.txt", folder.Id);

        var antes = await client.GetFromJsonAsync<UsageResponse>("/me/usage");

        await client.DeleteAsync($"/folders/{folder.Id}?recursive=true");

        // La papelera cuenta para la cuota (decision 1 del documento maestro):
        // borrar la carpeta no puede ser un atajo para liberar espacio.
        var despues = await client.GetFromJsonAsync<UsageResponse>("/me/usage");
        Assert.Equal(antes!.UsedBytes, despues!.UsedBytes);
    }

    [Fact]
    public async Task Delete_Recursive_ConservaLaFechaDeBorradoDeLosQueYaEstabanEnLaPapelera()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var folder = await CreateFolderAsync(client, "docs", null);
        var yaBorrado = await UploadAsync(client, "borrado-antes.txt", folder.Id);
        await UploadAsync(client, "borrado-con-la-carpeta.txt", folder.Id);

        // Este entra a la papelera primero, con su propia fecha.
        await client.DeleteAsync($"/files/{yaBorrado.Id}");
        var original = (await ListTrashAsync(client)).Single(t => t.Id == yaBorrado.Id);

        await client.DeleteAsync($"/folders/{folder.Id}?recursive=true");

        // Al borrar la carpeta no se le puede reiniciar el plazo: si se le
        // pisara DeletedAt, se quedaria en la papelera mas tiempo del debido y
        // ocupando cuota.
        var despues = (await ListTrashAsync(client)).Single(t => t.Id == yaBorrado.Id);
        Assert.Equal(original.DeletedAt, despues.DeletedAt);
        Assert.Equal(original.PurgeAt, despues.PurgeAt);
    }

    [Fact]
    public async Task Delete_CarpetaDeOtroCliente_Devuelve404()
    {
        var (_, emailA, passwordA) = await fixture.CreateClientAsync();
        var tokenA = await fixture.LoginAsync(emailA, passwordA);
        using var clientA = fixture.AuthenticatedClient(tokenA);
        var carpetaDeA = await CreateFolderAsync(clientA, "privada", null);

        var (_, emailB, passwordB) = await fixture.CreateClientAsync();
        var tokenB = await fixture.LoginAsync(emailB, passwordB);
        using var clientB = fixture.AuthenticatedClient(tokenB);

        // 404 y no 403: confirmar que existe ya seria filtrar informacion.
        var response = await clientB.DeleteAsync($"/folders/{carpetaDeA.Id}?recursive=true");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Y la carpeta de A sigue intacta.
        var deA = await ListFoldersAsync(clientA);
        Assert.Contains(deA, f => f.Id == carpetaDeA.Id);
    }
}
