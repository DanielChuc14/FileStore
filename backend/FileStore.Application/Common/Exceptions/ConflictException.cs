namespace FileStore.Application.Common.Exceptions;

/// <summary>Choque con el estado actual del recurso: por ejemplo, un email ya registrado.</summary>
public class ConflictException(string message) : Exception(message);
