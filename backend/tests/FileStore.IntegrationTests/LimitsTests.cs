using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre el endpoint que hace la API autodescriptiva.
///
/// Existe para que quien integra no tenga que descubrir los limites a base de
/// recibir 400 y 413, ni anotarlos a mano tras preguntarlos por correo: son
/// configurables por despliegue, asi que una nota se queda obsoleta en silencio.
///
/// Lo que mas importa aqui es que los valores sean los EFECTIVOS de la
/// credencial que llama, no los globales. Devolver el default global cuando el
/// cliente tiene un override seria peor que no responder nada, porque quien
/// integra lo daria por bueno y seguiria chocando con un limite distinto.
/// </summary>
[Collection("Integration")]
public class LimitsTests(IntegrationTestFixture fixture)
{
    private record AllowedTypeInfo(string Extension, string MimeType);
    private record LimitsDto(
        long MaxFileSizeBytes,
        long QuotaBytes,
        long UsedBytes,
        long AvailableBytes,
        int TrashRetentionDays,
        int RateLimitPerMinute,
        List<AllowedTypeInfo> AllowedTypes);
    private record ApiKeyPayload(Guid Id, string Name, string Prefix);
    private record CreateKeyResult(ApiKeyPayload ApiKey, string Value);

    private async Task<(Guid Id, HttpClient Panel)> ClientWithPanelAsync(long quotaBytes = 10_000)
    {
        var (id, email, password) = await fixture.CreateClientAsync(quotaBytes);
        var jwt = await fixture.LoginAsync(email, password);
        return (id, fixture.AuthenticatedClient(jwt));
    }

    private async Task PatchClientAsync(Guid clientId, object cambios)
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var response = await admin.PatchAsJsonAsync($"/v1/admin/clients/{clientId}", cambios);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DevuelveLasExtensionesPermitidasConSuMime()
    {
        var (_, panel) = await ClientWithPanelAsync();
        using var _p = panel;

        var limits = await panel.GetFromJsonAsync<LimitsDto>("/v1/limits");

        Assert.NotEmpty(limits!.AllowedTypes);

        // Es la lista que la subida valida de verdad. Sin este endpoint solo la
        // expone /v1/admin/allowed-types, que un cliente no puede consultar.
        var txt = Assert.Single(limits.AllowedTypes, t => t.Extension == "txt");
        Assert.Equal("text/plain", txt.MimeType);

        // Sin punto y en minusculas, tal como las compara el servidor.
        Assert.All(limits.AllowedTypes, t =>
        {
            Assert.DoesNotContain(".", t.Extension);
            Assert.Equal(t.Extension.ToLowerInvariant(), t.Extension);
        });
    }

    [Fact]
    public async Task LoQueDeclaraCoincideConLoQueLaSubidaAcepta()
    {
        var (_, panel) = await ClientWithPanelAsync();
        using var _p = panel;

        var limits = await panel.GetFromJsonAsync<LimitsDto>("/v1/limits");

        // El contrato en una frase: si el endpoint la declara, la subida la
        // acepta. Si alguna vez dejan de coincidir, este endpoint pasa de ayudar
        // a estorbar, porque el integrador validaria mal en su lado.
        var permitida = limits!.AllowedTypes.First().Extension;

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("x")), "file", $"prueba.{permitida}");

        var upload = await panel.PostAsync("/v1/files", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
    }

    [Fact]
    public async Task ElTamanoMaximoReflejaElOverrideDelCliente()
    {
        var (clientId, panel) = await ClientWithPanelAsync();
        using var _p = panel;

        var global = (await panel.GetFromJsonAsync<LimitsDto>("/v1/limits"))!.MaxFileSizeBytes;

        await PatchClientAsync(clientId, new { maxFileSizeBytes = 4096 });

        var propio = (await panel.GetFromJsonAsync<LimitsDto>("/v1/limits"))!.MaxFileSizeBytes;

        Assert.NotEqual(global, propio);
        Assert.Equal(4096, propio);
    }

    [Fact]
    public async Task LaRetencionDePapeleraReflejaElOverrideDelCliente()
    {
        var (clientId, panel) = await ClientWithPanelAsync();
        using var _p = panel;

        await PatchClientAsync(clientId, new { trashRetentionDays = 7 });

        var limits = await panel.GetFromJsonAsync<LimitsDto>("/v1/limits");
        Assert.Equal(7, limits!.TrashRetentionDays);
    }

    [Fact]
    public async Task ElEspacioDisponibleBajaAlSubir()
    {
        var (_, panel) = await ClientWithPanelAsync(quotaBytes: 10_000);
        using var _p = panel;

        var antes = await panel.GetFromJsonAsync<LimitsDto>("/v1/limits");
        Assert.Equal(10_000, antes!.AvailableBytes);
        Assert.Equal(0, antes.UsedBytes);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(new string('x', 500))), "file", "medio.txt");
        (await panel.PostAsync("/v1/files", form)).EnsureSuccessStatusCode();

        var despues = await panel.GetFromJsonAsync<LimitsDto>("/v1/limits");
        Assert.Equal(500, despues!.UsedBytes);
        Assert.Equal(9_500, despues.AvailableBytes);
    }

    [Fact]
    public async Task ConLaCuotaRecortadaElDisponibleEsCeroYNoNegativo()
    {
        var (clientId, panel) = await ClientWithPanelAsync(quotaBytes: 10_000);
        using var _p = panel;

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(new string('x', 500))), "file", "ocupa.txt");
        (await panel.PostAsync("/v1/files", form)).EnsureSuccessStatusCode();

        // Recortar la cuota por debajo del uso esta permitido, asi que la resta
        // puede dar negativo. Al integrador le interesa "cuanto me cabe", y la
        // respuesta es ninguno, no un numero negativo que tendria que interpretar.
        await PatchClientAsync(clientId, new { quotaBytes = 100 });

        var limits = await panel.GetFromJsonAsync<LimitsDto>("/v1/limits");
        Assert.Equal(0, limits!.AvailableBytes);
        Assert.True(limits.UsedBytes > limits.QuotaBytes);
    }

    [Fact]
    public async Task FuncionaConApiKey_QueEsElCasoQueMotivaElEndpoint()
    {
        var (_, panel) = await ClientWithPanelAsync(quotaBytes: 7_777);
        using var _p = panel;

        var created = await panel.PostAsJsonAsync("/v1/me/api-keys",
            new { name = "integracion", rateLimitPerMinute = 42 });
        var key = await created.Content.ReadFromJsonAsync<CreateKeyResult>();

        using var apiClient = fixture.ApiKeyClient(key!.Value);
        var limits = await apiClient.GetFromJsonAsync<LimitsDto>("/v1/limits");

        // El canal de API Key no puede consultar /v1/me/usage, que exige JWT: sin
        // este endpoint, una integracion no tenia forma de conocer su cuota y se
        // enteraba de que estaba llena al recibir un 413 a mitad de una carga.
        Assert.Equal(7_777, limits!.QuotaBytes);

        // Y el rate limit informado es el de ESA key, que es el que se le aplica.
        Assert.Equal(42, limits.RateLimitPerMinute);
    }

    [Fact]
    public async Task SinCredencial_Devuelve401()
    {
        using var anonymous = fixture.Factory.CreateClient();

        var response = await anonymous.GetAsync("/v1/limits");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ElSuperAdmin_NoTieneLimitesPropios()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        // No es dueño de contenido, asi que la pregunta no le aplica. Queda
        // fuera por la misma politica que le cierra /v1/files.
        var response = await admin.GetAsync("/v1/limits");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
