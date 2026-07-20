namespace FileStore.Application.Common.Exceptions;

/// <summary>
/// El contenido excede un limite: tamaño maximo por archivo o cuota disponible.
/// Ambos casos responden 413, como indica la seccion 6 del documento.
/// </summary>
public class PayloadTooLargeException(string message) : Exception(message);
