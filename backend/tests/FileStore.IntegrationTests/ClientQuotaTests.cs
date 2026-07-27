using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre que el super-admin pueda bajar la cuota de un cliente por debajo de lo
/// que ya tiene consumido.
///
/// Es una decision explicita del handler y no un descuido: recortar la cuota
/// impide subir mas, pero no borra nada. La alternativa (rechazar el cambio)
/// dejaria al administrador sin forma de frenar a un cliente que ya se paso.
///
/// Lo que se fija aqui es la cadena de consecuencias, que no es obvia: el cambio
/// se acepta, las subidas siguientes se rechazan, el contenido existente sigue
/// intacto y accesible, y liberar espacio de verdad vuelve a habilitar la subida.
/// </summary>
[Collection("Integration")]
public class ClientQuotaTests(IntegrationTestFixture fixture)
{
    private record UploadedFile(Guid Id, string OriginalName, long SizeBytes);
    private record UsageResponse(long UsedBytes, long QuotaBytes, int FilesCount);
    private record ClientDto(Guid Id, string Email, string Name, long QuotaBytes, long UsedBytes);
    private record FileListItem(Guid Id, string OriginalName);
    private record PagedFiles(List<FileListItem> Items);

    private const int FileSize = 400;

    private static MultipartFormDataContent FileForm(string name, int size)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(new string('x', size))), "file", name);
        return form;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string name, int size = FileSize)
    {
        using var form = FileForm(name, size);
        return await client.PostAsync("/v1/files", form);
    }

    private async Task<ClientDto> SetQuotaAsync(Guid clientId, long quotaBytes)
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var response = await admin.PatchAsJsonAsync($"/v1/admin/clients/{clientId}", new { quotaBytes });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ClientDto>())!;
    }

    [Fact]
    public async Task BajarLaCuotaPorDebajoDelUso_SeAcepta()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync(quotaBytes: 2000);
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        (await UploadAsync(client, "ocupa.txt")).EnsureSuccessStatusCode();

        // 400 bytes usados, se recorta la cuota a 100. Un rechazo aqui dejaria al
        // administrador sin forma de frenar a un cliente que ya se paso.
        var updated = await SetQuotaAsync(clientId, 100);

        Assert.Equal(100, updated.QuotaBytes);
        Assert.True(updated.UsedBytes > updated.QuotaBytes,
            "El consumo deberia quedar por encima de la cuota nueva.");
    }

    [Fact]
    public async Task ConLaCuotaRecortada_NoSePuedeSubirNadaMas()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync(quotaBytes: 2000);
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        (await UploadAsync(client, "primero.txt")).EnsureSuccessStatusCode();

        await SetQuotaAsync(clientId, 100);

        // Es el efecto buscado: la reserva atomica compara contra la cuota nueva.
        var blocked = await UploadAsync(client, "segundo.txt");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, blocked.StatusCode);
    }

    [Fact]
    public async Task ElRecorte_NoDestruyeNiEsconde_LoQueYaEstaba()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync(quotaBytes: 2000);
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var upload = await UploadAsync(client, "importante.txt");
        var file = await upload.Content.ReadFromJsonAsync<UploadedFile>();

        await SetQuotaAsync(clientId, 100);

        // Recortar la cuota no es una via para borrar datos ajenos: el archivo
        // sigue en el listado y se sigue pudiendo descargar.
        var listado = await client.GetFromJsonAsync<PagedFiles>("/v1/files");
        Assert.Contains(listado!.Items, f => f.Id == file!.Id);

        var download = await client.GetAsync($"/v1/files/{file!.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task ElConsumoReportado_PuedeSuperarLaCuota()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync(quotaBytes: 2000);
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        (await UploadAsync(client, "ocupa.txt")).EnsureSuccessStatusCode();
        await SetQuotaAsync(clientId, 100);

        // El endpoint informa el estado real, sin recortar el numero para que
        // cuadre. Quien pinte la barra en el panel tiene que contar con que el
        // porcentaje pase del 100%.
        var usage = await client.GetFromJsonAsync<UsageResponse>("/v1/me/usage");

        Assert.True(usage!.UsedBytes > usage.QuotaBytes);
        Assert.Equal(100, usage.QuotaBytes);
    }

    [Fact]
    public async Task LiberarEspacioDeVerdad_VuelveAHabilitarLaSubida()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync(quotaBytes: 2000);
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var upload = await UploadAsync(client, "estorba.txt");
        var file = await upload.Content.ReadFromJsonAsync<UploadedFile>();

        // Cuota justo por encima de un archivo: entra uno, no dos.
        await SetQuotaAsync(clientId, 600);
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            (await UploadAsync(client, "no-entra.txt")).StatusCode);

        // Mandarlo a la papelera NO libera cuota: sigue ocupando hasta purgarse.
        var delete = await client.DeleteAsync($"/v1/files/{file!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            (await UploadAsync(client, "sigue-sin-entrar.txt")).StatusCode);

        // El borrado definitivo si la libera, y recien ahi vuelve a caber algo.
        var hardDelete = await client.DeleteAsync($"/v1/trash/{file.Id}");
        Assert.Equal(HttpStatusCode.NoContent, hardDelete.StatusCode);

        var ahoraSi = await UploadAsync(client, "ahora-si.txt");
        Assert.Equal(HttpStatusCode.Created, ahoraSi.StatusCode);
    }

    [Fact]
    public async Task SubirLaCuota_TieneEfectoInmediato()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync(quotaBytes: 500);
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        (await UploadAsync(client, "primero.txt")).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            (await UploadAsync(client, "segundo.txt")).StatusCode);

        // Ampliar la cuota es el remedio del administrador y no puede exigir que
        // el cliente vuelva a iniciar sesion: la reserva lee la cuota vigente en
        // cada subida, no un valor cacheado en el token.
        await SetQuotaAsync(clientId, 5000);

        var ahora = await UploadAsync(client, "segundo.txt");
        Assert.Equal(HttpStatusCode.Created, ahora.StatusCode);
    }
}
