
using MartialHeroes.Network.Protocol.Packets.Login.Packets;
using MartialHeroes.Network.Protocol.Packets.World.Packets;


namespace MartialHeroes.Network.Protocol.Routing.Routing;

public interface IPacketHandler
{

    void Handle(in SmsgKeyExchange packet);


    void Handle(in SmsgGameStateTick packet);

    void
        Handle(in SmsgLocalPlayerStateSync packet);


    void Handle(in SmsgCharDespawn packet);

    void Handle(in SmsgCharDeath packet);

    void Handle(in SmsgEnterGameAck packet);

    void Handle(in SmsgActorMovementUpdate packet);

    void Handle(in SmsgCharSpawn packet);

    void Handle(in SmsgActorVitalsAndPairState packet);

    void Handle(in SmsgActorSpawnExtended packet);

    void Handle(in SmsgStatUpdate packet);

    void Handle(in SmsgLevelUp packet);

    void Handle(in SmsgEquipItemResult packet);

    void Handle(in SmsgItemSlotStateAck packet);

    void Handle(in SmsgNpcBuyOrAcquireAck packet);

    void Handle(in SmsgSkillHotbarSlotSet packet);

    void Handle(in SmsgSkillHotbarAssignResult packet);

    void Handle(in SmsgSkillWindowStateUpdate packet);

    void Handle(in SmsgCharSpawnResult packet);

    void Handle(in SmsgCharManageResult packet);

    void Handle(in SmsgRenameCharResult packet);

    void Handle(in SmsgCharStatusBytesByName packet);

    void Handle(in SmsgCharActionResult packet);

    void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload);
}