using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre la papelera: listar, restaurar y borrado definitivo. El punto delicado
/// es la cuota: restaurar la conserva, y solo el hard delete la libera. Un error
/// aqui deja bytes fantasma ocupando cuota que nadie puede recuperar.
/// </summary>
[Collection("Integration")]
public class TrashTests(IntegrationTestFixture fixture)
{
    private record UploadedFile(Guid Id, string OriginalName, string Extension, int VersionCount);
    private record TrashItemDto(
        Guid Id, string OriginalName, long SizeBytes, string Extension,
        DateTime DeletedAt, DateTime PurgeAt, int DaysUntilPurge);
    private record UsageResponse(long UsedBytes, long QuotaBytes, int FilesCount);
    private record FileListItem(Guid Id, string OriginalName);
    private record PagedFiles(List<FileListItem> Items);

    private static MultipartFormDataContent FileForm(string name, string content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", name);
        return form;
    }

    /// <summary>Sube un archivo y lo manda a la papelera; devuelve su id.</summary>
    private static async Task<Guid> UploadAndDeleteAsync(HttpClient client, string name, string content)
    {
        using var form = FileForm(name, content);
        var upload = await client.PostAsync("/files", form);
        var file = await upload.Content.ReadFromJsonAsync<UploadedFile>();

        var delete = await client.DeleteAsync($"/files/{file!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        return file.Id;
    }

    [Fact]
    public async Task GetTrash_ListaElArchivoBorradoConFechaDePurga()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var fileId = await UploadAndDeleteAsync(client, "borrado.txt", "contenido");

        var trash = await client.GetFromJsonAsync<List<TrashItemDto>>("/trash");

        var item = Assert.Single(trash!);
        Assert.Equal(fileId, item.Id);
        Assert.Equal("borrado.txt", item.OriginalName);
        // La fecha de purga es posterior al borrado (retencion por defecto > 0).
        Assert.True(item.PurgeAt > item.DeletedAt);
    }

    [Fact]
    public async Task Restore_DevuelveElArchivoAlListadoActivoYConservaLaCuota()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var fileId = await UploadAndDeleteAsync(client, "recuperable.txt", "contenido a recuperar");
        var usageEnPapelera = await client.GetFromJsonAsync<UsageResponse>("/me/usage");

        var restore = await client.PostAsync($"/trash/{fileId}/restore", null);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        // Vuelve al listado activo y desaparece de la papelera.
        var files = await client.GetFromJsonAsync<PagedFiles>("/files");
        Assert.Contains(files!.Items, f => f.Id == fileId);
        var trash = await client.GetFromJsonAsync<List<TrashItemDto>>("/trash");
        Assert.DoesNotContain(trash!, t => t.Id == fileId);

        // La cuota no cambia: el archivo nunca dejo de ocupar espacio.
        var usageRestaurado = await client.GetFromJsonAsync<UsageResponse>("/me/usage");
        Assert.Equal(usageEnPapelera!.UsedBytes, usageRestaurado!.UsedBytes);
    }

    [Fact]
    public async Task HardDelete_EliminaDefinitivamenteYLiberaLaCuota()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var fileId = await UploadAndDeleteAsync(client, "definitivo.txt", "contenido a eliminar");
        var usageAntes = await client.GetFromJsonAsync<UsageResponse>("/me/usage");
        Assert.True(usageAntes!.UsedBytes > 0);

        var hardDelete = await client.DeleteAsync($"/trash/{fileId}");
        Assert.Equal(HttpStatusCode.NoContent, hardDelete.StatusCode);

        // Ya no esta en la papelera y la cuota se libero por completo.
        var trash = await client.GetFromJsonAsync<List<TrashItemDto>>("/trash");
        Assert.DoesNotContain(trash!, t => t.Id == fileId);
        var usageDespues = await client.GetFromJsonAsync<UsageResponse>("/me/usage");
        Assert.Equal(0, usageDespues!.UsedBytes);
    }
}
