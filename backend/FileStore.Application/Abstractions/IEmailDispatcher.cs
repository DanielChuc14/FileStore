namespace FileStore.Application.Abstractions;

/// <summary>
/// Entrega los correos pendientes de la tabla de salida.
///
/// Separado del BackgroundService por el mismo motivo que <see cref="ITrashPurger"/>
/// y <see cref="IQuotaAlerter"/>: un hosted service solo se puede probar esperando
/// a su temporizador. Aqui esta lo que importa (reintentos, borrado del cuerpo,
/// rendicion tras N intentos) y se puede invocar directamente desde un test.
/// </summary>
public interface IEmailDispatcher
{
    /// <summary>Devuelve cuantos correos se entregaron con exito.</summary>
    Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default);
}
