using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre el versionado: listar versiones, descargar una especifica y restaurar
/// una antigua como vigente. Son reglas que fallan en silencio (devolver la
/// version equivocada no lanza ningun error), asi que conviene blindarlas.
/// </summary>
[Collection("Integration")]
public class VersioningTests(IntegrationTestFixture fixture)
{
    private record UploadedFile(Guid Id, string OriginalName, string Extension, int VersionCount);
    private record FileVersionDto(
        Guid Id, int VersionNumber, long SizeBytes, string MimeType,
        string ChecksumSha256, bool IsCurrent);

    private static MultipartFormDataContent FileForm(string name, string content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", name);
        return form;
    }

    /// <summary>Sube dos veces el mismo nombre y devuelve el id del archivo.</summary>
    private static async Task<Guid> UploadTwoVersionsAsync(HttpClient client, string name)
    {
        using var form1 = FileForm(name, "contenido de la version 1");
        var first = await client.PostAsync("/files", form1);
        var file = await first.Content.ReadFromJsonAsync<UploadedFile>();

        using var form2 = FileForm(name, "contenido de la version 2, distinto");
        await client.PostAsync("/files", form2);

        return file!.Id;
    }

    [Fact]
    public async Task GetVersions_DevuelveTodasConUnaSolaVigente()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var fileId = await UploadTwoVersionsAsync(client, "manual.txt");

        var versions = await client.GetFromJsonAsync<List<FileVersionDto>>($"/files/{fileId}/versions");

        Assert.Equal(2, versions!.Count);
        // Exactamente una es la vigente, y es la ultima subida (la 2).
        Assert.Single(versions, v => v.IsCurrent);
        Assert.True(versions.Single(v => v.IsCurrent).VersionNumber == 2);
    }

    [Fact]
    public async Task Download_VersionEspecifica_DevuelveEseContenido()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var fileId = await UploadTwoVersionsAsync(client, "informe.txt");

        var v1 = await client.GetStringAsync($"/files/{fileId}?version=1");
        var v2 = await client.GetStringAsync($"/files/{fileId}?version=2");
        var current = await client.GetStringAsync($"/files/{fileId}");

        Assert.Equal("contenido de la version 1", v1);
        Assert.Equal("contenido de la version 2, distinto", v2);
        // Sin parametro se devuelve la vigente, que es la ultima.
        Assert.Equal(v2, current);
    }

    [Fact]
    public async Task RestoreVersion_ReapuntaLaVigenteSinCrearVersionNueva()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var fileId = await UploadTwoVersionsAsync(client, "documento.txt");

        // Restaurar la version 1 como vigente.
        var restore = await client.PostAsync($"/files/{fileId}/versions/1/restore", null);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        var versions = await client.GetFromJsonAsync<List<FileVersionDto>>($"/files/{fileId}/versions");
        // No se crea una version nueva: siguen siendo 2.
        Assert.Equal(2, versions!.Count);
        // Ahora la vigente es la 1.
        Assert.Equal(1, versions.Single(v => v.IsCurrent).VersionNumber);

        // Y la descarga sin parametro devuelve el contenido de la version 1.
        var current = await client.GetStringAsync($"/files/{fileId}");
        Assert.Equal("contenido de la version 1", current);
    }
}
