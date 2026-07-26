using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre el audit log, que hasta ahora no tenia ni un assert pese a ser una
/// caracteristica declarada del producto: cada accion mutante queda registrada
/// con actor y recurso. Un fallo aqui es silencioso por definicion (nadie
/// consulta la auditoria hasta que la necesita), asi que solo un test lo detecta.
///
/// No se verifica la IP: el TestServer no expone una direccion remota, igual que
/// en RateLimitTests. Esa parte se apoya en ForwardedHeaders y solo se puede
/// comprobar detras de un proxy real.
/// </summary>
[Collection("Integration")]
public class AuditTests(IntegrationTestFixture fixture)
{
    private record UploadedFile(Guid Id, string OriginalName);
    private record AuditEntryDto(
        Guid Id, string Action, string ActorType, Guid ActorId,
        string? ResourceType, Guid? ResourceId, string? MetadataJson,
        string? IpAddress, DateTime CreatedAt);
    private record PagedAudit(List<AuditEntryDto> Items, int Page, int PageSize, int TotalCount);
    private record CreateKeyResult(string Value);

    private static async Task<UploadedFile> UploadAsync(HttpClient client, string name)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("contenido auditado")), "file", name);
        var response = await client.PostAsync("/files", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadedFile>())!;
    }

    private static async Task<List<AuditEntryDto>> GetAuditAsync(HttpClient client, string? action = null)
    {
        var url = action is null ? "/me/audit-log" : $"/me/audit-log?action={action}";
        var page = await client.GetFromJsonAsync<PagedAudit>(url);
        return page!.Items;
    }

    [Fact]
    public async Task Upload_QuedaRegistradoConElRecursoYElActor()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var file = await UploadAsync(client, "auditado.txt");

        var entries = await GetAuditAsync(client, "Upload");
        var entry = Assert.Single(entries, e => e.ResourceId == file.Id);

        Assert.Equal("Upload", entry.Action);
        Assert.Equal("StoredFile", entry.ResourceType);
        Assert.Equal("Client", entry.ActorType);
        Assert.Equal(clientId, entry.ActorId);

        // El metadata lleva el detalle que hace util la entrada al investigarla.
        Assert.NotNull(entry.MetadataJson);
        Assert.Contains("auditado.txt", entry.MetadataJson);
    }

    [Fact]
    public async Task Download_YBorrado_QuedanRegistrados()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var file = await UploadAsync(client, "descargado.txt");

        var download = await client.GetAsync($"/files/{file.Id}");
        download.EnsureSuccessStatusCode();

        var delete = await client.DeleteAsync($"/files/{file.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // La descarga se audita aunque no mute nada: es el acceso al contenido
        // lo que interesa registrar.
        Assert.Contains(await GetAuditAsync(client, "Download"), e => e.ResourceId == file.Id);
        Assert.Contains(await GetAuditAsync(client, "Delete"), e => e.ResourceId == file.Id);
    }

    [Fact]
    public async Task AccionesConApiKey_SeRegistranComoActorApiKey()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var panel = fixture.AuthenticatedClient(jwt);

        var created = await panel.PostAsJsonAsync("/me/api-keys", new { name = "auditoria" });
        var key = await created.Content.ReadFromJsonAsync<CreateKeyResult>();

        using var apiClient = fixture.ApiKeyClient(key!.Value);
        var file = await UploadAsync(apiClient, "subido-por-api.txt");

        // Distinguir el canal es el punto: saber que subio una integracion y no
        // una persona desde el panel.
        var entries = await GetAuditAsync(panel, "Upload");
        var entry = Assert.Single(entries, e => e.ResourceId == file.Id);
        Assert.Equal("ApiKey", entry.ActorType);
    }

    [Fact]
    public async Task GestionDeApiKeys_QuedaRegistrada()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var created = await client.PostAsJsonAsync("/me/api-keys", new { name = "auditada" });
        created.EnsureSuccessStatusCode();

        // Crear credenciales es de lo mas sensible que puede hacer un cliente.
        Assert.NotEmpty(await GetAuditAsync(client, "CreateApiKey"));
    }

    [Fact]
    public async Task ElAuditLog_NoMuestraEventosDeOtroCliente()
    {
        var (_, emailA, passwordA) = await fixture.CreateClientAsync();
        var tokenA = await fixture.LoginAsync(emailA, passwordA);
        using var clientA = fixture.AuthenticatedClient(tokenA);
        var fileDeA = await UploadAsync(clientA, "de-a.txt");

        var (_, emailB, passwordB) = await fixture.CreateClientAsync();
        var tokenB = await fixture.LoginAsync(emailB, passwordB);
        using var clientB = fixture.AuthenticatedClient(tokenB);
        await UploadAsync(clientB, "de-b.txt");

        // El aislamiento aplica tambien a la auditoria: el log de B no puede
        // revelar que archivos maneja A.
        var deB = await GetAuditAsync(clientB);
        Assert.DoesNotContain(deB, e => e.ResourceId == fileDeA.Id);
        Assert.NotEmpty(deB);
    }
}
