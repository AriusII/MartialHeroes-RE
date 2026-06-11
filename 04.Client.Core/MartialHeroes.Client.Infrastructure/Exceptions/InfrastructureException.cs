namespace MartialHeroes.Client.Infrastructure.Exceptions;

/// <summary>
/// Base exception for all infrastructure layer failures.
/// Wraps raw SQLite / IO exceptions so the Application layer is not coupled
/// to <c>Microsoft.Data.Sqlite</c> failure modes.
/// </summary>
public class InfrastructureException : Exception
{
    /// <inheritdoc/>
    public InfrastructureException(string message) : base(message)
    {
    }

    /// <inheritdoc/>
    public InfrastructureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when the local SQLite store fails to initialize, read, or write.
/// </summary>
public sealed class LocalStoreException : InfrastructureException
{
    /// <inheritdoc/>
    public LocalStoreException(string message) : base(message)
    {
    }

    /// <inheritdoc/>
    public LocalStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a macro file cannot be read from disk.
/// </summary>
public sealed class MacroFileException : InfrastructureException
{
    /// <inheritdoc/>
    public MacroFileException(string message) : base(message)
    {
    }

    /// <inheritdoc/>
    public MacroFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}