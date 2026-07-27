using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FileStore.IntegrationTests;

[Collection("Integration")]
public class FileOperationsTests(IntegrationTestFixture fixture)
{
    private record UploadedFile(Guid Id, string OriginalName, string Extension, int VersionCount);
    private record UsageResponse(long UsedBytes, long QuotaBytes, int FilesCount);

    private static MultipartFormDataContent FileForm(string name, string content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", name);
        return form;
    }

    [Fact]
    public async Task Upload_ArchivoValido_QuedaGuardado()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        using var form = FileForm("documento.txt", "contenido de prueba");
        var response = await client.PostAsync("/v1/files", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = await response.Content.ReadFromJsonAsync<UploadedFile>();
        Assert.Equal("documento.txt", file!.OriginalName);
        Assert.Equal("txt", file.Extension);
        Assert.Equal(1, file.VersionCount);
    }

    [Fact]
    public async Task Upload_ExtensionNoPermitida_400()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        using var form = FileForm("malicioso.exe", "contenido");
        var response = await client.PostAsync("/v1/files", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_MismoNombre_CreaVersionNueva()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        using var form1 = FileForm("informe.txt", "version 1");
        var first = await client.PostAsync("/v1/files", form1);
        var file1 = await first.Content.ReadFromJsonAsync<UploadedFile>();

        using var form2 = FileForm("informe.txt", "version 2 con mas texto");
        var second = await client.PostAsync("/v1/files", form2);
        var file2 = await second.Content.ReadFromJsonAsync<UploadedFile>();

        // Mismo archivo, no un duplicado; con dos versiones.
        Assert.Equal(file1!.Id, file2!.Id);
        Assert.Equal(2, file2.VersionCount);
    }

    [Fact]
    public async Task Upload_ExcedeLaCuota_413()
    {
        // Cuota chica para poder superarla con un archivo modesto.
        var (_, email, password) = await fixture.CreateClientAsync(quotaBytes: 100);
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        using var form = FileForm("grande.txt", new string('x', 500));
        var response = await client.PostAsync("/v1/files", form);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Concurrente_NuncaSuperaLaCuota()
    {
        // ESTE es el test que justifica usar una base real: la reserva atomica
        // de cuota depende del UPDATE condicional de Postgres. Con EF InMemory
        // este test pasaria aunque la reserva fuera insegura.
        //
        // Cuota para exactamente 2 archivos de 400 bytes. Se lanzan 6 subidas en
        // paralelo: si la reserva no fuera atomica, varias pasarian la validacion
        // a la vez y la suma superaria la cuota.
        var (clientId, email, password) = await fixture.CreateClientAsync(quotaBytes: 900);
        var token = await fixture.LoginAsync(email, password);

        var uploads = Enumerable.Range(0, 6).Select(async i =>
        {
            using var client = fixture.AuthenticatedClient(token);
            using var form = FileForm($"archivo-{i}.txt", new string('y', 400));
            var response = await client.PostAsync("/v1/files", form);
            return response.StatusCode;
        });

        var results = await Task.WhenAll(uploads);

        var aceptadas = results.Count(s => s == HttpStatusCode.Created);
        var rechazadas = results.Count(s => s == HttpStatusCode.RequestEntityTooLarge);

        // Entran 2 (800 <= 900), se rechazan las otras 4. Ninguna se pierde.
        Assert.Equal(2, aceptadas);
        Assert.Equal(4, rechazadas);

        // Y la cuota final nunca supero el limite.
        var adminToken = await fixture.LoginAsAdminAsync();
        using var adminClient = fixture.AuthenticatedClient(adminToken);
        var detail = await adminClient.GetFromJsonAsync<UsageResponse>($"/v1/admin/clients/{clientId}");
        Assert.True(detail!.UsedBytes <= 900,
            $"UsedBytes={detail.UsedBytes} supero la cuota de 900");
    }

    [Fact]
    public async Task Delete_ArchivoSigueOcupandoCuota()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        using var form = FileForm("borrable.txt", "contenido para borrar");
        var upload = await client.PostAsync("/v1/files", form);
        var file = await upload.Content.ReadFromJsonAsync<UploadedFile>();

        var beforeUsage = await client.GetFromJsonAsync<UsageResponse>("/v1/me/usage");

        var delete = await client.DeleteAsync($"/v1/files/{file!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // Borrado suave: el archivo va a la papelera pero SIGUE ocupando cuota.
        var afterUsage = await client.GetFromJsonAsync<UsageResponse>("/v1/me/usage");
        Assert.Equal(beforeUsage!.UsedBytes, afterUsage!.UsedBytes);
        Assert.Equal(0, afterUsage.FilesCount); // ya no en el listado activo
    }

    [Fact]
    public async Task Download_DevuelveElContenidoSubido()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var contenido = "contenido exacto que debe volver intacto";
        using var form = FileForm("prueba.txt", contenido);
        var upload = await client.PostAsync("/v1/files", form);
        var file = await upload.Content.ReadFromJsonAsync<UploadedFile>();

        var download = await client.GetAsync($"/v1/files/{file!.Id}");
        var bytes = await download.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(contenido, bytes);
        Assert.Contains("attachment", download.Content.Headers.ContentDisposition!.ToString());
    }
}
