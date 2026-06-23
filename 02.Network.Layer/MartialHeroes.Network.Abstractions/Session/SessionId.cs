namespace MartialHeroes.Network.Abstractions.Session;

public readonly record struct SessionId(ulong Value)
{
    public static readonly SessionId None = new(0UL);

    public override string ToString()
    {
        return $"Session({Value})";
    }
}