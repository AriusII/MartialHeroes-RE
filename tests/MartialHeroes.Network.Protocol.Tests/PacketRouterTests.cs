// Router round-trip: build a frame from known bytes, route it, assert the correct typed handler
// fires and fields decode. spec: Docs/RE/opcodes.md + the per-packet yaml specs.
// Little-endian construction per Docs/RE/opcodes.md.

using System.Buffers.Binary;
using MartialHeroes.Network.Protocol;
using MartialHeroes.Network.Protocol.Opcodes;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Routing;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class PacketRouterTests
{
    // Recording handler: captures which typed method fired and the decoded values.
    private sealed class RecordingHandler : IPacketHandler
    {
        public uint? DespawnSort;
        public uint? DespawnActorId;
        public byte? DespawnFlags;

        public float? MoveYaw;
        public float? MovePosX;
        public float? MovePosZ;
        public byte? MoveSort;
        public uint? MoveActorId;

        public uint? EnterBillingState;
        public uint? EnterCharacterCount;

        public uint? SpawnSort;
        public uint? SpawnActorId;

        public uint? UnhandledOpcode;

        public void Handle(in SmsgCharDespawn p)
        {
            DespawnSort = p.Sort;
            DespawnActorId = p.ActorId;
            DespawnFlags = p.Flags;
        }

        public void Handle(in SmsgEnterGameAck p)
        {
            EnterBillingState = p.BillingState;
            EnterCharacterCount = p.CharacterCount;
        }

        public void Handle(in SmsgActorMovementUpdate p)
        {
            MoveSort = p.Sort;
            MoveActorId = p.ActorId;
            MoveYaw = p.Yaw;
            MovePosX = p.PosX;
            MovePosZ = p.PosZ;
        }

        public void Handle(in SmsgCharSpawn p)
        {
            SpawnSort = p.Sort;
            SpawnActorId = p.ActorId;
        }

        public void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload) =>
            UnhandledOpcode = packedOpcode;
    }

    private static byte[] BuildFrame(ushort major, ushort minor, ReadOnlySpan<byte> payload)
    {
        // spec: Docs/RE/opcodes.md — 8-byte LE header; size = 8 + payload, then major, minor.
        byte[] frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), (ushort)(8 + payload.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), major);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), minor);
        payload.CopyTo(frame.AsSpan(8));
        return frame;
    }

    [Fact] // spec: Docs/RE/packets/5-0_char_despawn.yaml
    public void Routes_char_despawn_and_decodes_fields()
    {
        // Sort=1 (PC) u32, ActorId=0x11223344 u32, Flags=1, 3 pad bytes.
        byte[] payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x11223344);
        payload[8] = 1;
        byte[] frame = BuildFrame(5, 0, payload);

        var handler = new RecordingHandler();
        bool routed = PacketRouter.Route(frame, handler);

        Assert.True(routed);
        Assert.Equal(1u, handler.DespawnSort);
        Assert.Equal(0x11223344u, handler.DespawnActorId);
        Assert.Equal((byte)1, handler.DespawnFlags);
        Assert.Null(handler.UnhandledOpcode);
    }

    [Fact] // spec: Docs/RE/packets/5-13_actor_movement_update.yaml
    public void Routes_actor_movement_update_and_decodes_floats()
    {
        byte[] payload = new byte[40];
        payload[0] = 2; // Sort = Mob
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0xCAFEBABE);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(8, 4), 1.5f);   // Yaw
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(12, 4), 100.25f); // PosX
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(16, 4), -42.5f);  // PosZ
        byte[] frame = BuildFrame(5, 13, payload);

        var handler = new RecordingHandler();
        bool routed = PacketRouter.Route(frame, handler);

        Assert.True(routed);
        Assert.Equal((byte)2, handler.MoveSort);
        Assert.Equal(0xCAFEBABEu, handler.MoveActorId);
        Assert.Equal(1.5f, handler.MoveYaw);
        Assert.Equal(100.25f, handler.MovePosX);
        Assert.Equal(-42.5f, handler.MovePosZ);
    }

    [Fact] // spec: Docs/RE/packets/3-5_enter_game_response.yaml
    public void Routes_enter_game_ack_and_decodes_tail_fields()
    {
        byte[] payload = new byte[44];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x1c, 4), 7);  // BillingState
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x28, 4), 3);  // CharacterCount
        byte[] frame = BuildFrame(3, 5, payload);

        var handler = new RecordingHandler();
        bool routed = PacketRouter.Route(frame, handler);

        Assert.True(routed);
        Assert.Equal(7u, handler.EnterBillingState);
        Assert.Equal(3u, handler.EnterCharacterCount);
    }

    [Fact] // spec: Docs/RE/packets/5-3_char_spawn.yaml
    public void Routes_char_spawn_header_fields()
    {
        byte[] payload = new byte[908];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0xDEADBEEF);
        byte[] frame = BuildFrame(5, 3, payload);

        var handler = new RecordingHandler();
        bool routed = PacketRouter.Route(frame, handler);

        Assert.True(routed);
        Assert.Equal(1u, handler.SpawnSort);
        Assert.Equal(0xDEADBEEFu, handler.SpawnActorId);
    }

    [Fact] // unspecced opcode -> OnUnhandled, returns false.
    public void Unknown_opcode_falls_through_to_OnUnhandled()
    {
        byte[] frame = BuildFrame(5, 9, new byte[4]); // 5/9 ExpGain: no struct.

        var handler = new RecordingHandler();
        bool routed = PacketRouter.Route(frame, handler);

        Assert.False(routed);
        Assert.Equal(Opcodes.Opcodes.SmsgExpGain, handler.UnhandledOpcode);
    }
}
