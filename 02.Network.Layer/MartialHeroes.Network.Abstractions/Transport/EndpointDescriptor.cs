using System.Net;

namespace MartialHeroes.Network.Abstractions.Transport;

public sealed record EndpointDescriptor(EndPoint EndPoint, string DisplayName = "")
{
    public override string ToString()
    {
        return DisplayName.Length > 0 ? $"{DisplayName} ({EndPoint})" : EndPoint.ToString()!;
    }
}