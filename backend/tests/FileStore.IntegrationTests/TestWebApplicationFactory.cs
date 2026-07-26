using FileStore.Application.Abstractions;
using FileStore.Domain.Entities;
using FileStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        // Sin clave de Resend el contenedor resuelve LoggingEmailSender, asi que
        // la suite nunca sale a la red ni manda correo real. Se fija de forma
        // explicita para que no dependa de lo que haya en el entorno de quien
        // corra los tests.
        Environment.SetEnvironmentVariable("Resend__ApiKey", "");
        // El despachador tampoco corre: un test que verifique la cola quiere ver
        // el correo encolado, no que un job se lo lleve por debajo.
        Environment.SetEnvironmentVariable("EmailDispatch__Enabled", "false");
        // Los avisos de cuota se disparan a mano en su test, no por temporizador.
        Environment.SetEnvironmentVariable("QuotaAlerts__Enabled", "false");
        Environment.SetEnvironmentVariable("SuperAdmin__Email", AdminEmail);
        Environment.SetEnvironmentVariable("SuperAdmin__Password", AdminPassword);
        Environment.SetEnvironmentVariable("SuperAdmin__Name", "Admin Test");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost:4200");
    }

    /// <summary>
    /// Sender que usa la suite. Reemplaza al que registraria el contenedor
    /// (LoggingEmailSender, porque no hay clave de Resend) por dos motivos: no
    /// salir nunca a la red, y poder simular que el correo NO esta configurado
    /// para probar las guardas que lo comprueban.
    /// </summary>
    public sealed class ControllableEmailSender : IEmailSender
    {
        /// <summary>Por defecto configurado: es el estado normal de la suite.</summary>
        public bool IsConfigured { get; set; } = true;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    public ControllableEmailSender EmailSender { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }

    /// <summary>
    /// Corre una accion como si el envio de correo no estuviera configurado, y
    /// restaura el estado pase lo que pase. Los tests de una coleccion corren en
    /// serie, asi que alterar la bandera un momento no afecta a los demas.
    /// </summary>
    public async Task WithEmailDeliveryDisabledAsync(Func<Task> action)
    {
        EmailSender.IsConfigured = false;

        try
        {
            await action();
        }
        finally
        {
            EmailSender.IsConfigured = true;
        }
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

    /// <summary>
    /// Fija una contraseña conocida para un cliente, directo en la base.
    ///
    /// Hace falta desde que el alta dejo de devolver la contraseña generada: la
    /// unica copia se la lleva el correo. Un test podria extraerla del cuerpo
    /// encolado, pero eso ata cada test al texto de la plantilla. Escribir el
    /// hash es determinista y no toca codigo de produccion.
    /// </summary>
    public async Task SetClientPasswordAsync(Guid clientId, string password)
    {
        using var scope = Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<FileStoreDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var client = await context.Clients.FirstAsync(c => c.Id == clientId);
        client.PasswordHash = hasher.Hash(password);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Fija el consumo de un cliente sin subir archivos de verdad. Llenar una
    /// cuota real byte a byte haria el test lento y fragil.
    /// </summary>
    public async Task SetUsedBytesAsync(Guid clientId, long usedBytes)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FileStoreDbContext>();

        var client = await context.Clients.FirstAsync(c => c.Id == clientId);
        client.UsedBytes = usedBytes;

        await context.SaveChangesAsync();
    }

    /// <summary>Corre la revision de cuotas a mano, sin esperar al temporizador.</summary>
    public async Task<int> RunQuotaCheckAsync()
    {
        using var scope = Services.CreateScope();
        var alerter = scope.ServiceProvider.GetRequiredService<IQuotaAlerter>();

        return await alerter.CheckAsync();
    }

    /// <summary>Correos encolados para un destinatario, del mas nuevo al mas viejo.</summary>
    public async Task<List<EmailOutboxMessage>> GetQueuedEmailsAsync(string recipient)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FileStoreDbContext>();

        return await context.EmailOutbox
            .AsNoTracking()
            .Where(m => m.Recipient == recipient)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
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
