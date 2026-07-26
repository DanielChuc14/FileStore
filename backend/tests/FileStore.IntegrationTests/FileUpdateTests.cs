using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre renombrar y mover archivos.
///
/// Lo delicado no es el cambio de nombre en si, sino lo que arrastra: la
/// extension determina el MIME y la whitelist de tipos permitidos, asi que
/// renombrar es otra puerta de entrada a esa validacion. Si no se comprobara,
/// bastaria con subir "informe.pdf" y renombrarlo a "script.exe" para meter en
/// el sistema un tipo que la subida rechaza.
/// </summary>
[Collection("Integration")]
public class FileUpdateTests(IntegrationTestFixture fixture)
{
    private record FileDto(
        Guid Id, Guid? FolderId, string OriginalName, long SizeBytes,
        string MimeType, string Extension, int VersionCount);
    private record FolderDto(Guid Id, Guid? ParentFolderId, string Name, string Path);

    private static async Task<FileDto> UploadAsync(HttpClient client, string name, Guid? folderId = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("contenido")), "file", name);

        var url = folderId is null ? "/files" : $"/files?folderId={folderId}";
        var response = await client.PostAsync(url, form);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<FileDto>())!;
    }

    private static async Task<FolderDto> CreateFolderAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/folders", new { name, parentId = (Guid?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FolderDto>())!;
    }

    private async Task<HttpClient> NewClientAsync()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        return fixture.AuthenticatedClient(token);
    }

    [Fact]
    public async Task Renombrar_CambiaElNombreYRecalculaElTipo()
    {
        using var client = await NewClientAsync();
        var file = await UploadAsync(client, "informe.txt");

        var response = await client.PatchAsJsonAsync($"/files/{file.Id}", new { name = "informe-final.csv" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<FileDto>())!;

        Assert.Equal("informe-final.csv", updated.OriginalName);

        // La extension y el MIME se recalculan: son datos derivados del nombre y
        // quedarian mintiendo si solo cambiara la cadena.
        Assert.Equal("csv", updated.Extension);
        Assert.Equal("text/csv", updated.MimeType);
    }

    [Fact]
    public async Task Renombrar_AUnaExtensionNoPermitida_SeRechaza()
    {
        using var client = await NewClientAsync();
        var file = await UploadAsync(client, "informe.txt");

        // Es el agujero evidente si nadie lo comprueba: la subida valida la
        // whitelist, y renombrar seria la puerta de atras para saltarsela.
        var response = await client.PatchAsJsonAsync($"/files/{file.Id}", new { name = "malicioso.exe" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var sinCambios = await client.GetFromJsonAsync<FileDto>($"/files/{file.Id}/metadata");
        Assert.Equal("informe.txt", sinCambios!.OriginalName);
    }

    [Fact]
    public async Task Renombrar_ConNombreQueIntentaSalirDeLaRuta_SeRechaza()
    {
        using var client = await NewClientAsync();
        var file = await UploadAsync(client, "informe.txt");

        var response = await client.PatchAsJsonAsync($"/files/{file.Id}", new { name = "../../etc/passwd" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Renombrar_ChocandoConOtroDelMismoDestino_Devuelve409()
    {
        using var client = await NewClientAsync();
        await UploadAsync(client, "ocupado.txt");
        var file = await UploadAsync(client, "libre.txt");

        // Dos archivos con el mismo nombre en la misma carpeta romperian la
        // regla que hace que re-subir genere una version en vez de un duplicado.
        var response = await client.PatchAsJsonAsync($"/files/{file.Id}", new { name = "ocupado.txt" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Mover_AOtraCarpeta_CambiaLaUbicacion()
    {
        using var client = await NewClientAsync();
        var destino = await CreateFolderAsync(client, "destino");
        var file = await UploadAsync(client, "viajero.txt");

        Assert.Null(file.FolderId);

        var response = await client.PatchAsJsonAsync($"/files/{file.Id}", new { folderId = destino.Id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<FileDto>())!;
        Assert.Equal(destino.Id, updated.FolderId);
    }

    [Fact]
    public async Task Mover_ALaRaiz_NecesitaElFlagExplicito()
    {
        using var client = await NewClientAsync();
        var carpeta = await CreateFolderAsync(client, "origen");
        var file = await UploadAsync(client, "viajero.txt", carpeta.Id);

        // folderId null sin el flag significa "no mover", no "mover a la raiz":
        // con un solo campo nullable los dos casos serian indistinguibles.
        var sinFlag = await client.PatchAsJsonAsync($"/files/{file.Id}", new { name = "renombrado.txt" });
        sinFlag.EnsureSuccessStatusCode();
        Assert.Equal(carpeta.Id, (await sinFlag.Content.ReadFromJsonAsync<FileDto>())!.FolderId);

        var conFlag = await client.PatchAsJsonAsync($"/files/{file.Id}", new { moveToRoot = true });
        conFlag.EnsureSuccessStatusCode();
        Assert.Null((await conFlag.Content.ReadFromJsonAsync<FileDto>())!.FolderId);
    }

    [Fact]
    public async Task Mover_AUnaCarpetaDeOtroCliente_Devuelve404()
    {
        var (_, emailA, passwordA) = await fixture.CreateClientAsync();
        var tokenA = await fixture.LoginAsync(emailA, passwordA);
        using var clientA = fixture.AuthenticatedClient(tokenA);
        var carpetaDeA = await CreateFolderAsync(clientA, "privada");

        using var clientB = await NewClientAsync();
        var fileDeB = await UploadAsync(clientB, "mio.txt");

        // Mover a una carpeta ajena colocaria contenido de B dentro del arbol de
        // A. Se responde 404 y no 403: confirmar que existe ya seria filtrar.
        var response = await clientB.PatchAsJsonAsync($"/files/{fileDeB.Id}", new { folderId = carpetaDeA.Id });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Renombrar_ArchivoDeOtroCliente_Devuelve404()
    {
        var (_, emailA, passwordA) = await fixture.CreateClientAsync();
        var tokenA = await fixture.LoginAsync(emailA, passwordA);
        using var clientA = fixture.AuthenticatedClient(tokenA);
        var fileDeA = await UploadAsync(clientA, "de-a.txt");

        using var clientB = await NewClientAsync();

        var response = await clientB.PatchAsJsonAsync($"/files/{fileDeA.Id}", new { name = "secuestrado.txt" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Y el archivo de A conserva su nombre.
        var sinCambios = await clientA.GetFromJsonAsync<FileDto>($"/files/{fileDeA.Id}/metadata");
        Assert.Equal("de-a.txt", sinCambios!.OriginalName);
    }

    [Fact]
    public async Task Renombrar_UnArchivoEnLaPapelera_Devuelve404()
    {
        using var client = await NewClientAsync();
        var file = await UploadAsync(client, "borrado.txt");

        await client.DeleteAsync($"/files/{file.Id}");

        // Lo que esta en la papelera no se edita: primero se restaura.
        var response = await client.PatchAsJsonAsync($"/files/{file.Id}", new { name = "otro.txt" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
