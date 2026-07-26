namespace FileStore.Infrastructure.Email;

public class EmailDispatchSettings
{
    public const string SectionName = "EmailDispatch";

    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuanto se revisa la cola.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Correos por ciclo, para no monopolizar la base ni la API del proveedor.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Intentos antes de darlo por perdido.</summary>
    public int MaxAttempts { get; set; } = 5;
}
