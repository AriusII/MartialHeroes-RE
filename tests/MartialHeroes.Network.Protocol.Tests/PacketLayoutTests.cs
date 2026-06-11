// Layout drift guard: each wire struct's runtime size must equal its spec `size:`.
// spec sources cited per assertion. CAPTURE-UNVERIFIED layouts (capture_verified: false).

using System.Runtime.CompilerServices;
using MartialHeroes.Network.Protocol.Packets;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class PacketLayoutTests
{
    [Fact] // spec: Docs/RE/packets/5-0_char_despawn.yaml (size: 12)
    public void CharDespawn_size_is_12()
    {
        Assert.Equal(12, Unsafe.SizeOf<SmsgCharDespawn>());
        Assert.Equal(12, SmsgCharDespawn.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/3-5_enter_game_response.yaml (size: 44)
    public void EnterGameAck_size_is_44()
    {
        Assert.Equal(44, Unsafe.SizeOf<SmsgEnterGameAck>());
        Assert.Equal(44, SmsgEnterGameAck.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/5-13_actor_movement_update.yaml (size: 40)
    public void ActorMovementUpdate_size_is_40()
    {
        Assert.Equal(40, Unsafe.SizeOf<SmsgActorMovementUpdate>());
        Assert.Equal(40, SmsgActorMovementUpdate.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/5-3_char_spawn.yaml (size: 908)
    public void CharSpawn_size_is_908()
    {
        Assert.Equal(908, Unsafe.SizeOf<SmsgCharSpawn>());
        Assert.Equal(908, SmsgCharSpawn.WireSize);
    }
}
