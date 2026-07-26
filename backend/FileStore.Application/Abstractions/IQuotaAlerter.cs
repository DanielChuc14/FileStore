namespace FileStore.Application.Abstractions;

/// <summary>
/// Revisa el consumo de todos los clientes y encola los avisos de cuota que
/// correspondan.
///
/// Separado del BackgroundService por el mismo motivo que <see cref="ITrashPurger"/>:
/// un hosted service solo se puede probar esperando a su temporizador. Con la
/// logica aqui, el test la invoca directamente.
/// </summary>
public interface IQuotaAlerter
{
    /// <summary>Devuelve cuantos avisos se encolaron.</summary>
    Task<int> CheckAsync(CancellationToken cancellationToken = default);
}
