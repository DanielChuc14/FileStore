namespace FileStore.Application.Common.Exceptions;

/// <summary>
/// Validacion que no puede expresarse en un validador de FluentValidation
/// porque depende de datos (whitelist de extensiones, existencia del recurso).
/// Se mapea a 400 con el mismo formato que los errores de validacion.
/// </summary>
public class ValidationFailedException(string property, string error)
    : Exception(error)
{
    public string Property { get; } = property;
    public string Error { get; } = error;
}
