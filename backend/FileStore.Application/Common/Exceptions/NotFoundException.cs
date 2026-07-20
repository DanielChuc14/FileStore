namespace FileStore.Application.Common.Exceptions;

public class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;
}
