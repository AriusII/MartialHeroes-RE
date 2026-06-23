using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Abstractions.Lobby;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct LobbyServerRecordWire
{
    public const int WireSize = 8;

    public readonly short ServerId;

    public readonly short StatusCode;

    public readonly short Load;

    public readonly short OpenTime;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LobbyServerRecord ToRecord()
    {
        return new LobbyServerRecord(ServerId, StatusCode, Load, OpenTime);
    }
}