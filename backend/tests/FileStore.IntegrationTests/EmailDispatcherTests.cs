using FileStore.Application.Abstractions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using FileStore.Infrastructure.Email;
using FileStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre la entrega de la cola de correo.
///
/// Es la mitad del sistema que no probaba nadie: los demas tests verifican que
/// los correos se ENCOLEN, pero el despachador esta apagado en la factory, asi
/// que marcar como enviado, borrar el cuerpo, reintentar con espera creciente y
/// rendirse tras N intentos nunca se habian ejecutado. Un fallo ahi es invisible
/// hasta el dia que se enciende el envio real y no llega nada.
///
/// Se construye el despachador a mano con un sender falso, en vez de resolverlo
/// del contenedor: hace falta poder decidir cuando falla el envio.
/// </summary>
[Collection("Integration")]
public class EmailDispatcherTests(IntegrationTestFixture fixture)
{
    /// <summary>Sender controlable: cuenta envios y falla cuando se le pide.</summary>
    private sealed class FakeEmailSender : IEmailSender
    {
        public bool IsConfigured => true;
        public bool ShouldFail { get; set; }
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new EmailSendException("Fallo simulado del proveedor.");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed record Harness(
        IServiceScope Scope,
        FileStoreDbContext Context,
        FakeEmailSender Sender,
        EmailDispatcher Dispatcher) : IDisposable
    {
        public void Dispose() => Scope.Dispose();
    }

    private async Task<Harness> CreateHarnessAsync(int maxAttempts = 5, int batchSize = 20)
    {
        var scope = fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FileStoreDbContext>();
        var sender = new FakeEmailSender();

        // La base de tests acumula correos pendientes de los demas tests, porque
        // el despachador esta apagado en la factory. Sin apartarlos, el lote se
        // llena con ellos y no llega a los de este test. Se posponen en vez de
        // borrarlos: son datos que otros casos pueden estar mirando.
        await context.EmailOutbox
            .Where(m => m.Status == EmailStatus.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(
                m => m.NextAttemptAt, DateTime.UtcNow.AddDays(1)));

        var settings = Options.Create(new EmailDispatchSettings
        {
            MaxAttempts = maxAttempts,
            BatchSize = batchSize
        });

        return new Harness(
            scope,
            context,
            sender,
            new EmailDispatcher(context, sender, settings, NullLogger<EmailDispatcher>.Instance));
    }

    /// <summary>
    /// Encola un correo directo en la base. Se usa un destinatario unico por test
    /// para que la coleccion compartida no mezcle correos de otros casos.
    /// </summary>
    private static async Task<Guid> QueueAsync(FileStoreDbContext context, string recipient)
    {
        var id = Guid.CreateVersion7();

        context.EmailOutbox.Add(new EmailOutboxMessage
        {
            Id = id,
            Recipient = recipient,
            Subject = "Asunto de prueba",
            HtmlBody = "<p>Contrasena: secreta</p>",
            TextBody = "Contrasena: secreta",
            Status = EmailStatus.Pending,
            Attempts = 0,
            CreatedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
        return id;
    }

    private static string UniqueRecipient() => $"dispatch-{Guid.NewGuid():N}@example.com";

    private static async Task<EmailOutboxMessage> ReloadAsync(FileStoreDbContext context, Guid id)
    {
        var message = await context.EmailOutbox.FirstAsync(m => m.Id == id);
        await context.Entry(message).ReloadAsync();
        return message;
    }

    [Fact]
    public async Task EnvioCorrecto_MarcaEnviadoYBorraElCuerpo()
    {
        using var h = await CreateHarnessAsync();
        var id = await QueueAsync(h.Context, UniqueRecipient());

        var sent = await h.Dispatcher.DispatchPendingAsync();

        Assert.Equal(1, sent);

        var message = await ReloadAsync(h.Context, id);
        Assert.Equal(EmailStatus.Sent, message.Status);
        Assert.NotNull(message.SentAt);
        Assert.Equal(1, message.Attempts);
        Assert.Null(message.LastError);
        Assert.Null(message.NextAttemptAt);

        // Lo importante: el cuerpo llevaba una contraseña y no puede quedarse en
        // la base. Sin esto, EmailOutbox seria un archivo de credenciales en claro.
        Assert.Null(message.HtmlBody);
        Assert.Null(message.TextBody);
    }

    [Fact]
    public async Task ElCuerpoLlegaAlSenderAntesDeBorrarse()
    {
        using var h = await CreateHarnessAsync();
        var recipient = UniqueRecipient();
        await QueueAsync(h.Context, recipient);

        await h.Dispatcher.DispatchPendingAsync();

        // Borrar el cuerpo no puede significar enviarlo vacio.
        var delivered = Assert.Single(h.Sender.Sent, m => m.To == recipient);
        Assert.Contains("secreta", delivered.HtmlBody);
        Assert.Contains("secreta", delivered.TextBody!);
    }

    [Fact]
    public async Task Fallo_SigueePendienteYProgramaElReintento()
    {
        using var h = await CreateHarnessAsync();
        var id = await QueueAsync(h.Context, UniqueRecipient());
        h.Sender.ShouldFail = true;

        var sent = await h.Dispatcher.DispatchPendingAsync();

        Assert.Equal(0, sent);

        var message = await ReloadAsync(h.Context, id);
        Assert.Equal(EmailStatus.Pending, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.Null(message.SentAt);
        Assert.NotNull(message.LastError);
        Assert.Contains("Fallo simulado", message.LastError);

        // La espera creciente es lo que evita martillar a un proveedor caido.
        Assert.NotNull(message.NextAttemptAt);
        Assert.True(message.NextAttemptAt > DateTime.UtcNow.AddMinutes(1));

        // Y el cuerpo se conserva: sin el no habria nada que reintentar.
        Assert.NotNull(message.TextBody);
    }

    [Fact]
    public async Task ReintentoProgramado_NoSeTomaAntesDeTiempo()
    {
        using var h = await CreateHarnessAsync();
        var id = await QueueAsync(h.Context, UniqueRecipient());

        h.Sender.ShouldFail = true;
        await h.Dispatcher.DispatchPendingAsync();

        // Segunda pasada inmediata: el reintento esta programado a futuro, asi
        // que este ciclo no debe tocarlo.
        h.Sender.ShouldFail = false;
        await h.Dispatcher.DispatchPendingAsync();

        var message = await ReloadAsync(h.Context, id);
        Assert.Equal(1, message.Attempts);
        Assert.Equal(EmailStatus.Pending, message.Status);
    }

    [Fact]
    public async Task TrasAgotarLosIntentos_QuedaComoFallido()
    {
        using var h = await CreateHarnessAsync(maxAttempts: 2);
        var id = await QueueAsync(h.Context, UniqueRecipient());
        h.Sender.ShouldFail = true;

        await h.Dispatcher.DispatchPendingAsync();

        // Se adelanta el reintento para no esperar los minutos de la espera.
        var message = await ReloadAsync(h.Context, id);
        message.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
        await h.Context.SaveChangesAsync();

        await h.Dispatcher.DispatchPendingAsync();

        message = await ReloadAsync(h.Context, id);
        Assert.Equal(EmailStatus.Failed, message.Status);
        Assert.Equal(2, message.Attempts);

        // Sin fecha de reintento: no se vuelve a intentar solo. Queda el motivo
        // para poder diagnosticarlo sin bucear en el log.
        Assert.Null(message.NextAttemptAt);
        Assert.NotNull(message.LastError);
    }

    [Fact]
    public async Task UnCorreoYaEnviado_NoSeReenvia()
    {
        using var h = await CreateHarnessAsync();
        var recipient = UniqueRecipient();
        await QueueAsync(h.Context, recipient);

        await h.Dispatcher.DispatchPendingAsync();
        await h.Dispatcher.DispatchPendingAsync();
        await h.Dispatcher.DispatchPendingAsync();

        // Reenviar credenciales por un ciclo repetido seria tan malo como no
        // enviarlas.
        Assert.Single(h.Sender.Sent, m => m.To == recipient);
    }

    [Fact]
    public async Task UnFalloNoImpideEntregarElRestoDelLote()
    {
        using var h = await CreateHarnessAsync();

        var buenoA = UniqueRecipient();
        var buenoB = UniqueRecipient();
        await QueueAsync(h.Context, buenoA);
        await QueueAsync(h.Context, buenoB);

        await h.Dispatcher.DispatchPendingAsync();

        // Los dos salen en la misma tanda: el bucle no corta ante una excepcion.
        Assert.Contains(h.Sender.Sent, m => m.To == buenoA);
        Assert.Contains(h.Sender.Sent, m => m.To == buenoB);
    }

    [Fact]
    public async Task RespetaElTamanoDelLote()
    {
        using var h = await CreateHarnessAsync(batchSize: 1);

        var primero = UniqueRecipient();
        var segundo = UniqueRecipient();
        await QueueAsync(h.Context, primero);
        await QueueAsync(h.Context, segundo);

        var sent = await h.Dispatcher.DispatchPendingAsync();

        // El lote acota cuanto puede tardar un ciclo y cuantas llamadas seguidas
        // recibe la API del proveedor.
        Assert.Equal(1, sent);
    }
}
