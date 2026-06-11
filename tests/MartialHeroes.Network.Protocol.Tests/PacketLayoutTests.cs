// Layout drift guard: each wire struct's runtime size must equal its spec `size:`.
// spec sources cited per assertion. CAPTURE-UNVERIFIED layouts (capture_verified: false).

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    [Fact] // spec: Docs/RE/packets/2-13_move_request.yaml (size: 16)
    public void MoveRequest_size_is_16()
    {
        Assert.Equal(16, Unsafe.SizeOf<CmsgMoveRequest>());
        Assert.Equal(16, CmsgMoveRequest.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/1-9_enter_game_request.yaml (size: 40)
    public void EnterGameRequest_size_is_40()
    {
        Assert.Equal(40, Unsafe.SizeOf<CmsgEnterGameRequest>());
        Assert.Equal(40, CmsgEnterGameRequest.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml (size: 32)
    public void ActorVitalsAndPairState_size_is_32()
    {
        Assert.Equal(32, Unsafe.SizeOf<SmsgActorVitalsAndPairState>());
        Assert.Equal(32, SmsgActorVitalsAndPairState.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/5-32_level_up.yaml (size: 48)
    public void LevelUp_size_is_48()
    {
        Assert.Equal(48, Unsafe.SizeOf<SmsgLevelUp>());
        Assert.Equal(48, SmsgLevelUp.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/4-29_stat_update.yaml (size: 36)
    public void StatUpdate_size_is_36()
    {
        Assert.Equal(36, Unsafe.SizeOf<SmsgStatUpdate>());
        Assert.Equal(36, SmsgStatUpdate.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (size: 912)
    public void ActorSpawnExtended_size_is_912()
    {
        Assert.Equal(912, Unsafe.SizeOf<SmsgActorSpawnExtended>());
        Assert.Equal(912, SmsgActorSpawnExtended.WireSize);
    }

    // --- variable-length packets: only the fixed header size is guaranteed ---

    [Fact] // spec: Docs/RE/packets/2-52_use_skill.yaml (24-byte header)
    public void UseSkillHeader_size_is_24()
    {
        Assert.Equal(24, Unsafe.SizeOf<CmsgUseSkillHeader>());
        Assert.Equal(24, CmsgUseSkillHeader.HeaderSize);
    }

    [Fact] // spec: Docs/RE/packets/2-7_whisper.yaml (19-byte header)
    public void WhisperHeader_size_is_19()
    {
        Assert.Equal(19, Unsafe.SizeOf<CmsgWhisperHeader>());
        Assert.Equal(19, CmsgWhisperHeader.HeaderSize);
    }

    [Fact] // spec: Docs/RE/packets/2-83_chat_contextual.yaml (24-byte header)
    public void ChatContextualHeader_size_is_24()
    {
        Assert.Equal(24, Unsafe.SizeOf<CmsgChatContextualHeader>());
        Assert.Equal(24, CmsgChatContextualHeader.HeaderSize);
    }

    [Fact] // spec: Docs/RE/packets/3-21_chat_channel.yaml (56-byte header)
    public void ChatChannelHeader_size_is_56()
    {
        Assert.Equal(56, Unsafe.SizeOf<CmsgChatChannelHeader>());
        Assert.Equal(56, CmsgChatChannelHeader.HeaderSize);
    }

    [Fact] // spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml — field round-trip read
    public void ActorVitalsAndPairState_decodes_known_bytes()
    {
        // Build a 32-byte LE frame body with distinctive per-field values, then reinterpret it as
        // the wire struct and assert each field lands at its specced offset. spec: 5-53.
        Span<byte> body = stackalloc byte[SmsgActorVitalsAndPairState.WireSize];
        body[0x00] = 2;                                            // Sort
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x04..], 0x11223344u); // ActorId
        body[0x08] = 0xAB;                                         // Byte08
        body[0x09] = 0xCD;                                         // Byte09
        body[0x0a] = 42;                                           // LevelOrState
        body[0x0b] = 7;                                            // StateByte
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x0c..], 0x55667788u); // PartnerId
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x10..], 1000u);       // CurrentHp
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x14..], 2000u);       // VitalB
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x18..], 3000u);       // Stamina
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x1c..], 4000u);       // VitalC

        ref readonly SmsgActorVitalsAndPairState p =
            ref MemoryMarshal.AsRef<SmsgActorVitalsAndPairState>(body);

        Assert.Equal(2, p.Sort);
        Assert.Equal(ActorSort.Mob, p.SortKind);
        Assert.Equal(0x11223344u, p.ActorId);
        Assert.Equal(0xAB, p.Byte08);
        Assert.Equal(0xCD, p.Byte09);
        Assert.Equal(42, p.LevelOrState);
        Assert.Equal(7, p.StateByte);
        Assert.Equal(0x55667788u, p.PartnerId);
        Assert.Equal(1000u, p.CurrentHp);
        Assert.Equal(2000u, p.VitalB);
        Assert.Equal(3000u, p.Stamina);
        Assert.Equal(4000u, p.VitalC);
    }
}
