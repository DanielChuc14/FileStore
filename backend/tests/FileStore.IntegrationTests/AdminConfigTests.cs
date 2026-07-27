using System.Net;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre la configuracion global y las estadisticas del super-admin. El test
/// central prueba que la config NO es decorativa: bajar el tamaño maximo debe
/// rechazar un upload que antes pasaba.
///
/// La config es estado global compartido por toda la coleccion de tests; como
/// xUnit corre los tests de una coleccion en serie, se cambia y se restaura
/// dentro del mismo test (finally) para no contaminar a los demas.
/// </summary>
[Collection("Integration")]
public class AdminConfigTests(IntegrationTestFixture fixture)
{
    private record AppConfigDto(long MaxFileSizeBytes, int TrashRetentionDays, int RateLimitDefaultPerMinute);
    private record AdminStatsDto(int TotalClients, long TotalUsedBytes);

    private static MultipartFormDataContent FileForm(string name, string content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content)), "file", name);
        return form;
    }

    private static async Task<AppConfigDto> GetConfigAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<AppConfigDto>("/v1/admin/config"))!;

    private static async Task PatchMaxFileSizeAsync(HttpClient admin, long bytes)
    {
        var response = await admin.PatchAsJsonAsync("/v1/admin/config", new { maxFileSizeBytes = bytes });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task UpdateConfig_BajarMaxFileSize_RechazaUploadQueLoSupera()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);
        var original = await GetConfigAsync(admin);

        try
        {
            // 1024 es el minimo permitido por el validador.
            await PatchMaxFileSizeAsync(admin, 1024);

            var (_, email, password) = await fixture.CreateClientAsync();
            var token = await fixture.LoginAsync(email, password);
            using var client = fixture.AuthenticatedClient(token);

            // 2000 bytes contra un maximo de 1024: debe rechazarse con 413. Si la
            // config fuera decorativa, este upload pasaria.
            using var form = FileForm("grande.txt", new string('x', 2000));
            var response = await client.PostAsync("/v1/files", form);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }
        finally
        {
            await PatchMaxFileSizeAsync(admin, original.MaxFileSizeBytes);
        }
    }

    [Fact]
    public async Task UpdateConfig_PersisteYSeReflejaEnGetConfig()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);
        var original = await GetConfigAsync(admin);

        try
        {
            var patch = await admin.PatchAsJsonAsync("/v1/admin/config", new { trashRetentionDays = 99 });
            patch.EnsureSuccessStatusCode();

            var updated = await GetConfigAsync(admin);
            Assert.Equal(99, updated.TrashRetentionDays);
        }
        finally
        {
            await admin.PatchAsJsonAsync("/v1/admin/config",
                new { trashRetentionDays = original.TrashRetentionDays });
        }
    }

    [Fact]
    public async Task Stats_ReflejanLosBytesSubidos()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var before = await admin.GetFromJsonAsync<AdminStatsDto>("/v1/admin/stats");

        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);
        using var form = FileForm("stats.txt", new string('z', 500));
        var upload = await client.PostAsync("/v1/files", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var after = await admin.GetFromJsonAsync<AdminStatsDto>("/v1/admin/stats");

        // Las stats cuentan datos reales: hay un cliente mas y al menos 500 bytes mas.
        Assert.True(after!.TotalClients > before!.TotalClients);
        Assert.True(after.TotalUsedBytes >= before.TotalUsedBytes + 500);
    }

    [Fact]
    public async Task Config_ConTokenDeCliente_403()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var response = await client.GetAsync("/v1/admin/config");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
