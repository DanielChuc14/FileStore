namespace FileStore.Application.Abstractions;

/// <summary>
/// Barrido de la papelera: encuentra los archivos cuya retencion vencio, los
/// borra y libera su cuota. Se separa del BackgroundService que lo agenda para
/// poder ejecutarlo (y testearlo) sin depender del temporizador.
/// </summary>
public interface ITrashPurger
{
    /// <param name="batchSize">Tope de archivos a purgar en esta corrida.</param>
    Task<TrashPurgeResult> PurgeExpiredAsync(int batchSize, CancellationToken cancellationToken = default);
}

/// <param name="PurgedCount">Archivos purgados.</param>
/// <param name="BytesFreed">Bytes de cuota liberados.</param>
public record TrashPurgeResult(int PurgedCount, long BytesFreed);
