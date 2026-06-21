namespace MartialHeroes.Network.Abstractions.Session;

/// <summary>
///     Strongly-typed, allocation-free identifier for a single transport-level connection session.
/// </summary>
/// <remarks>
///     Scoped to the lifetime of one connection attempt. If the session is torn down and a new TCP
///     connection is made, a fresh <see cref="SessionId" /> is issued. The value is opaque to all
///     layers above <c>Transport.Pipelines</c>; consumers must never interpret the underlying
///     <see cref="Value" />.
/// </remarks>
/// <param name="Value">The raw 64-bit identifier, assigned by the transport factory.</param>
public readonly record struct SessionId(ulong Value)
{
    /// <summary>Sentinel representing the absence of a session (value 0).</summary>
    public static readonly SessionId None = new(0UL);

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Session({Value})";
    }
}