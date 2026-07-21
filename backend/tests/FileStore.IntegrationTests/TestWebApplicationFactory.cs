using FileStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FileStore.IntegrationTests;

/// <summary>
/// Levanta la API real en memoria contra la base de prueba filestore_test. Es el
/// corazon de los tests de integracion: prueban el sistema entero (controllers,
/// autenticacion, EF, Postgres) como lo ve un cliente HTTP, no piezas aisladas.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    // Rol dedicado a los tests: su password es descartable y solo da acceso a
    // filestore_test, por eso es seguro que viva en el repositorio. Se puede
    // sobreescribir con la variable de entorno FILESTORE_TEST_CONNECTION (util
    // en CI o si el Postgres local escucha en otro puerto).
    private static readonly string TestConnectionString =
        Environment.GetEnvironmentVariable("FILESTORE_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=filestore_test;Username=filestore_test;Password=test_local_only";

    public const string AdminEmail = "admin-test@filestore.local";
    public const string AdminPassword = "AdminDePrueba2026";

    public readonly string StoragePath =
        Path.Combine(Path.GetTempPath(), "filestore-tests", Guid.NewGuid().ToString());

    public TestWebApplicationFactory()
    {
        // La configuracion se pasa por variables de entorno y no por
        // ConfigureAppConfiguration porque Program.cs lee la seccion Jwt y la
        // valida ANTES de builder.Build(); las fuentes que agrega la factory se
        // aplican recien en el build, demasiado tarde. Las variables de entorno,
        // en cambio, ya estan cuando CreateBuilder arma la configuracion.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", TestConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Secret", "clave-de-prueba-para-integracion-32-bytes-min!!");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "FileStore");
        Environment.SetEnvironmentVariable("Jwt__Audience", "FileStore");
        Environment.SetEnvironmentVariable("Storage__BasePath", StoragePath);
        // El job de purga se apaga: no debe interferir con los tests.
        Environment.SetEnvironmentVariable("TrashPurge__Enabled", "false");
        Environment.SetEnvironmentVariable("SuperAdmin__Email", AdminEmail);
        Environment.SetEnvironmentVariable("SuperAdmin__Password", AdminPassword);
        Environment.SetEnvironmentVariable("SuperAdmin__Name", "Admin Test");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost:4200");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    /// <summary>
    /// Migra la base ANTES de arrancar la app. El orden importa: al arrancar,
    /// Program corre el seeder del super-admin, que consulta la tabla SuperAdmins.
    /// Si se migrara accediendo a Services (que dispara el arranque), el seeder
    /// correria primero contra tablas inexistentes. Por eso se usa un contexto
    /// independiente para migrar, y recien despues se arranca la app.
    /// </summary>
    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<FileStoreDbContext>()
            .UseNpgsql(TestConnectionString)
            .Options;

        await using (var context = new FileStoreDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        // Ahora si se arranca la app (una peticion cualquiera lo dispara): el
        // seeder encuentra las tablas y crea el super-admin de prueba.
        using var client = CreateClient();
        await client.GetAsync("/health");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(StoragePath))
        {
            try { Directory.Delete(StoragePath, recursive: true); } catch { }
        }
    }
}
