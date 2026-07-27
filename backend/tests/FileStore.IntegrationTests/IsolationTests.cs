using System.Net;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

/// <summary>
/// El aislamiento entre clientes es la premisa del producto: el cliente A no
/// debe poder ver ni tocar nada de B. Estos tests lo verifican de forma
/// automatica, algo imposible de sostener a mano a medida que crece el sistema.
///
/// Se espera 404 y no 403 al pedir un recurso ajeno: un 403 confirmaria que el
/// recurso existe. Para el otro cliente, simplemente no existe.
/// </summary>
[Collection("Integration")]
public class IsolationTests(IntegrationTestFixture fixture)
{
    private record UploadedFile(Guid Id, string OriginalName);
    private record FolderResponse(Guid Id, string Path);
    private record ApiKeyCreated(ApiKeyPayload ApiKey, string Value);
    private record ApiKeyPayload(Guid Id, string Prefix);

    /// <summary>
    /// Prepara dos clientes: A con una carpeta, un archivo y una API Key; B vacio.
    /// Devuelve los clientes autenticados y los recursos de A.
    /// </summary>
    private async Task<(HttpClient A, HttpClient B, Guid FileA, Guid FolderA, Guid ApiKeyAId)> SetupAsync()
    {
        var (_, emailA, passwordA) = await fixture.CreateClientAsync();
        var (_, emailB, passwordB) = await fixture.CreateClientAsync();
        var tokenA = await fixture.LoginAsync(emailA, passwordA);
        var tokenB = await fixture.LoginAsync(emailB, passwordB);

        var clientA = fixture.AuthenticatedClient(tokenA);
        var clientB = fixture.AuthenticatedClient(tokenB);

        var folderResponse = await clientA.PostAsJsonAsync("/v1/folders", new { name = "privado-a" });
        var folder = await folderResponse.Content.ReadFromJsonAsync<FolderResponse>();

        var fileId = await UploadAsync(clientA, "secreto-de-a.txt", "contenido confidencial", folder!.Id);

        var keyResponse = await clientA.PostAsJsonAsync("/v1/me/api-keys", new { name = "key-de-a" });
        var key = await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreated>();

        return (clientA, clientB, fileId, folder.Id, key!.ApiKey.Id);
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string name, string content, Guid? folderId)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        form.Add(fileContent, "file", name);

        var url = folderId.HasValue ? $"/v1/files?folderId={folderId}" : "/v1/files";
        var response = await client.PostAsync(url, form);
        response.EnsureSuccessStatusCode();

        var uploaded = await response.Content.ReadFromJsonAsync<UploadedFile>();
        return uploaded!.Id;
    }

    [Fact]
    public async Task B_NoVeLosArchivosDeA()
    {
        var (_, b, _, _, _) = await SetupAsync();

        var response = await b.GetAsync("/v1/files");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"totalCount\":0", json.Replace(" ", ""));
    }

    [Fact]
    public async Task B_NoDescargaElArchivoDeA()
    {
        var (_, b, fileA, _, _) = await SetupAsync();

        var response = await b.GetAsync($"/v1/files/{fileA}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task B_NoBorraElArchivoDeA()
    {
        var (a, b, fileA, _, _) = await SetupAsync();

        var deleteResponse = await b.DeleteAsync($"/v1/files/{fileA}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        // Y el archivo de A sigue ahi tras el intento.
        var stillThere = await a.GetAsync($"/v1/files/{fileA}/metadata");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task B_NoVeLasVersionesDeA()
    {
        var (_, b, fileA, _, _) = await SetupAsync();

        var response = await b.GetAsync($"/v1/files/{fileA}/versions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task B_NoVeLasCarpetasDeA()
    {
        var (_, b, _, _, _) = await SetupAsync();

        var response = await b.GetAsync("/v1/folders?all=true");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", json.Replace(" ", ""));
    }

    [Fact]
    public async Task B_NoSubeArchivosEnLaCarpetaDeA()
    {
        var (_, b, _, folderA, _) = await SetupAsync();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("intruso"u8.ToArray()), "file", "intruso.txt");

        var response = await b.PostAsync($"/v1/files?folderId={folderA}", form);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task B_NoModificaLaApiKeyDeA()
    {
        var (_, b, _, _, apiKeyAId) = await SetupAsync();

        var patch = await b.PatchAsJsonAsync($"/v1/me/api-keys/{apiKeyAId}", new { name = "secuestrada" });
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);

        var revoke = await b.PostAsync($"/v1/me/api-keys/{apiKeyAId}/revoke", null);
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
    }

    [Fact]
    public async Task ConApiKeyDeB_TampocoSeAccedeAlArchivoDeA()
    {
        // El aislamiento debe valer por los dos canales, no solo por JWT.
        var (_, emailB, passwordB) = await fixture.CreateClientAsync();
        var tokenB = await fixture.LoginAsync(emailB, passwordB);
        using var jwtB = fixture.AuthenticatedClient(tokenB);
        var keyResponse = await jwtB.PostAsJsonAsync("/v1/me/api-keys", new { name = "prod" });
        var keyB = await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreated>();

        var (_, _, fileA, _, _) = await SetupAsync();

        using var apiClientB = fixture.ApiKeyClient(keyB!.Value);
        var response = await apiClientB.GetAsync($"/v1/files/{fileA}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
