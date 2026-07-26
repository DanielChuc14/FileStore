using System.Net;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre los avisos automaticos: cuota al 80/95% y alertas de seguridad.
///
/// Son correos que nadie pide: si dejaran de salir, el sistema seguiria
/// funcionando y nadie lo notaria hasta que un cliente se quedara sin espacio de
/// golpe o no se enterara de que le robaron la sesion.
/// </summary>
[Collection("Integration")]
public class NotificationTests(IntegrationTestFixture fixture)
{
    private const long Quota = 1000;

    private async Task<(Guid Id, string Email, string Password)> ClientWithQuotaAsync() =>
        await fixture.CreateClientAsync(quotaBytes: Quota);

    [Fact]
    public async Task Cuota_AlCruzarEl80_EncolaUnAviso()
    {
        var (id, email, _) = await ClientWithQuotaAsync();

        await fixture.Factory.SetUsedBytesAsync(id, 850);
        await fixture.Factory.RunQuotaCheckAsync();

        var queued = await fixture.Factory.GetQueuedEmailsAsync(email);
        Assert.Contains(queued, m => m.Subject.Contains("80%"));
    }

    [Fact]
    public async Task Cuota_PorDebajoDel80_NoAvisa()
    {
        var (id, email, _) = await ClientWithQuotaAsync();

        await fixture.Factory.SetUsedBytesAsync(id, 700);
        await fixture.Factory.RunQuotaCheckAsync();

        // Solo el correo de bienvenida.
        Assert.Single(await fixture.Factory.GetQueuedEmailsAsync(email));
    }

    [Fact]
    public async Task Cuota_NoRepiteElAvisoEnCadaRevision()
    {
        var (id, email, _) = await ClientWithQuotaAsync();

        await fixture.Factory.SetUsedBytesAsync(id, 850);

        await fixture.Factory.RunQuotaCheckAsync();
        await fixture.Factory.RunQuotaCheckAsync();
        await fixture.Factory.RunQuotaCheckAsync();

        // Esta es la razon de existir de la marca: el job corre cada hora y sin
        // ella el cliente recibiria un correo por hora hasta liberar espacio.
        var avisos = (await fixture.Factory.GetQueuedEmailsAsync(email))
            .Count(m => m.Subject.Contains("80%"));

        Assert.Equal(1, avisos);
    }

    [Fact]
    public async Task Cuota_AlSubirDel80Al95_AvisaOtraVez()
    {
        var (id, email, _) = await ClientWithQuotaAsync();

        await fixture.Factory.SetUsedBytesAsync(id, 850);
        await fixture.Factory.RunQuotaCheckAsync();

        await fixture.Factory.SetUsedBytesAsync(id, 970);
        await fixture.Factory.RunQuotaCheckAsync();

        var queued = await fixture.Factory.GetQueuedEmailsAsync(email);

        // Empeorar si merece un aviso nuevo: el 95% es una situacion distinta.
        Assert.Contains(queued, m => m.Subject.Contains("80%"));
        Assert.Contains(queued, m => m.Subject.Contains("95%"));
    }

    [Fact]
    public async Task Cuota_LiberarEspacioReactivaElAviso()
    {
        var (id, email, _) = await ClientWithQuotaAsync();

        await fixture.Factory.SetUsedBytesAsync(id, 850);
        await fixture.Factory.RunQuotaCheckAsync();

        // Baja del umbral: se limpia la marca.
        await fixture.Factory.SetUsedBytesAsync(id, 100);
        await fixture.Factory.RunQuotaCheckAsync();

        // Y al volver a llenarse, avisa de nuevo.
        await fixture.Factory.SetUsedBytesAsync(id, 850);
        await fixture.Factory.RunQuotaCheckAsync();

        var avisos = (await fixture.Factory.GetQueuedEmailsAsync(email))
            .Count(m => m.Subject.Contains("80%"));

        Assert.Equal(2, avisos);
    }

    [Fact]
    public async Task ReusoDeRefreshToken_AvisaAlCliente()
    {
        var (_, email, password) = await fixture.CreateClientAsync();

        using var client = fixture.Factory.CreateClient();

        var login = await client.PostAsJsonAsync("/auth/login", new { email, password });
        var cookie = login.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("fs_refresh="));
        var original = cookie.Split(';')[0];

        // Se rota: el token original queda revocado.
        using var withCookie = fixture.Factory.CreateClient();
        withCookie.DefaultRequestHeaders.Add("Cookie", original);
        var rotation = await withCookie.PostAsync("/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);

        // Y ahora se reusa el viejo, que es la señal de sesion robada.
        using var replay = fixture.Factory.CreateClient();
        replay.DefaultRequestHeaders.Add("Cookie", original);
        var reused = await replay.PostAsync("/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

        // Antes esto se detectaba y se cortaba en silencio: el cliente veia morir
        // su sesion sin saber por que, y el indicio quedaba solo en el log.
        var queued = await fixture.Factory.GetQueuedEmailsAsync(email);
        Assert.Contains(queued, m => m.Subject.Contains("seguridad"));
    }

    [Fact]
    public async Task CrearApiKey_AvisaAlCliente()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        using var panel = fixture.AuthenticatedClient(jwt);
        var created = await panel.PostAsJsonAsync("/me/api-keys", new { name = "integracion-nueva" });
        created.EnsureSuccessStatusCode();

        var queued = await fixture.Factory.GetQueuedEmailsAsync(email);
        Assert.Contains(queued, m => m.Subject.Contains("API Key"));
    }

    [Fact]
    public async Task RotarApiKey_TambienAvisa()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        using var panel = fixture.AuthenticatedClient(jwt);

        var created = await panel.PostAsJsonAsync("/me/api-keys", new { name = "a-rotar" });
        var body = await created.Content.ReadFromJsonAsync<CreateKeyResponse>();

        await panel.PostAsync($"/me/api-keys/{body!.ApiKey.Id}/rotate", null);

        // Dos avisos: uno por la creacion y otro por la rotacion.
        var avisos = (await fixture.Factory.GetQueuedEmailsAsync(email))
            .Count(m => m.Subject.Contains("API Key"));

        Assert.Equal(2, avisos);
    }

    private record ApiKeyPayload(Guid Id, string Name, string Prefix);
    private record CreateKeyResponse(ApiKeyPayload ApiKey, string Value);
}
