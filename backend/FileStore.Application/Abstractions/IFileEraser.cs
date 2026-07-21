namespace FileStore.Application.Abstractions;

/// <summary>
/// Borrado irreversible de un archivo con todas sus versiones. Lo usan tanto el
/// hard delete manual como el job de purga, para que no existan dos caminos de
/// borrado que puedan divergir.
///
/// NO valida autorizacion: el llamador debe haber verificado la propiedad. El
/// job de purga corre sin usuario autenticado.
/// </summary>
public interface IFileEraser
{
    /// <summary>Devuelve los bytes liberados de la cuota.</summary>
    Task<long> EraseAsync(Guid fileId, CancellationToken cancellationToken = default);
}
