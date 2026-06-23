namespace MartialHeroes.Client.Infrastructure.Exceptions;

public class InfrastructureException : Exception
{
    public InfrastructureException(string message) : base(message)
    {
    }

    public InfrastructureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LocalStoreException : InfrastructureException
{
    public LocalStoreException(string message) : base(message)
    {
    }

    public LocalStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class MacroFileException : InfrastructureException
{
    public MacroFileException(string message) : base(message)
    {
    }

    public MacroFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LuaConfigException : InfrastructureException
{
    public LuaConfigException(string message) : base(message)
    {
    }

    public LuaConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}