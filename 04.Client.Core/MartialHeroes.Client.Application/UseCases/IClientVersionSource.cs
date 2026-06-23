namespace MartialHeroes.Client.Application.UseCases;

public interface IClientVersionSource
{
    uint VersionField { get; }
}

public static class ClientVersionToken
{
    public const uint SampledVersionField = 2114;

    public const uint SampledToken = 21149;

    public static uint Derive(uint versionField)
    {
        return 10u * versionField + 9u;
    }
}

public sealed class DefaultClientVersionSource : IClientVersionSource
{
    public static readonly DefaultClientVersionSource Instance = new();

    private DefaultClientVersionSource()
    {
    }

    public uint VersionField => ClientVersionToken.SampledVersionField;
}