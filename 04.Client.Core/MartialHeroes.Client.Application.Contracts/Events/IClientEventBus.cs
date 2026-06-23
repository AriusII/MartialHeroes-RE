using System.Threading.Channels;

namespace MartialHeroes.Client.Application.Contracts.Events;

public interface IClientEventBus
{
    ChannelReader<IClientEvent> Reader { get; }

    bool Publish(IClientEvent clientEvent);

    void Complete();
}