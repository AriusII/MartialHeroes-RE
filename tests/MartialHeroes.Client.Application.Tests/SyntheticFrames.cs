using System.Buffers.Binary;
using MartialHeroes.Network.Protocol;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Client.Application.Tests;

/// <summary>
/// Builds synthetic wire frames (8-byte header + payload) for driving the PacketRouter in tests.
/// Offsets mirror the packet struct layouts (Pack=1) and the SpawnDescriptor spec. spec:
/// Docs/RE/opcodes.md (frame header), Docs/RE/structs/actor.md (SpawnDescriptor).
/// </summary>
internal static class SyntheticFrames
{
    private static byte[] Frame(ushort major, ushort minor, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[FrameHeader.Size + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), (ushort)frame.Length); // size@+0
        // bytes @+2..+3 unused for framing
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), major);                 // major@+4
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), minor);                 // minor@+6
        payload.CopyTo(frame.AsSpan(FrameHeader.Size));
        return frame;
    }

    /// <summary>5/13 actor movement update (40-byte payload). spec: 5-13_actor_movement_update.yaml.</summary>
    public static byte[] MovementUpdate(
        byte sort, uint actorId, float yaw, float posX, float posZ, float destX, float destZ,
        byte runFlag = 0, float speedScale = 1f, byte motionCode = 0, byte stance = 0)
    {
        Span<byte> p = stackalloc byte[40];
        p.Clear();
        p[0x00] = sort;
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), actorId);
        BinaryPrimitives.WriteSingleLittleEndian(p.Slice(0x08, 4), yaw);
        BinaryPrimitives.WriteSingleLittleEndian(p.Slice(0x0c, 4), posX);
        BinaryPrimitives.WriteSingleLittleEndian(p.Slice(0x10, 4), posZ);
        BinaryPrimitives.WriteSingleLittleEndian(p.Slice(0x14, 4), destX);
        BinaryPrimitives.WriteSingleLittleEndian(p.Slice(0x18, 4), destZ);
        p[0x1c] = runFlag;
        BinaryPrimitives.WriteSingleLittleEndian(p.Slice(0x20, 4), speedScale);
        p[0x24] = motionCode;
        p[0x26] = stance;
        return Frame(5, 13, p);
    }

    /// <summary>5/0 char despawn (12-byte payload). spec: 5-0_char_despawn.yaml.</summary>
    public static byte[] Despawn(byte sort, uint actorId, byte flags)
    {
        Span<byte> p = stackalloc byte[12];
        p.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x00, 4), sort);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), actorId);
        p[0x08] = flags;
        return Frame(5, 0, p);
    }

    /// <summary>3/5 enter-game ack (44-byte payload). spec: 3-5_enter_game_response.yaml.</summary>
    public static byte[] EnterGameAck(uint billingState = 1, uint characterCount = 1)
    {
        Span<byte> p = stackalloc byte[44];
        p.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x1c, 4), billingState);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x28, 4), characterCount);
        return Frame(3, 5, p);
    }

    /// <summary>
    /// 5/53 actor vitals and pair state (32-byte payload). spec: 5-53_actor_vitals_and_pair_state.yaml.
    /// </summary>
    public static byte[] Vitals(
        byte sort, uint actorId, uint currentHp, uint stamina, uint vitalC,
        byte levelOrState = 0, byte stateByte = 0)
    {
        Span<byte> p = stackalloc byte[32];
        p.Clear();
        p[0x00] = sort;
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), actorId);
        p[0x0a] = levelOrState;
        p[0x0b] = stateByte;
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x10, 4), currentHp);  // CurrentHp @0x10
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x18, 4), stamina);    // Stamina @0x18
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x1c, 4), vitalC);     // VitalC (MP) @0x1c
        return Frame(5, 53, p);
    }

    /// <summary>
    /// 5/1 extended actor spawn (912-byte payload: 12-byte prefix + 880-byte SpawnDescriptor +
    /// 20-byte trailer). SpawnDescriptor sub-fields per Docs/RE/structs/spawn_descriptor.md.
    /// spec: 5-1_actor_spawn_extended.yaml.
    /// </summary>
    public static byte[] SpawnExtended(
        byte sort, uint actorId, string name, ushort level,
        uint currentHp, uint currentMp, uint currentStamina,
        float worldX, float worldZ, ushort serverClass)
    {
        var p = new byte[912];

        // packet-level prefix (12 bytes): sort @0x000, actorId @0x004, title/relation bytes @0x008.
        p[0x000] = sort;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x004, 4), actorId);

        // SpawnDescriptor starts at packet offset 0x00c. Sub-offsets are descriptor-relative.
        const int sd = 0x00c;
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        int nameLen = Math.Min(nameBytes.Length, 16); // spec: spawn_descriptor.md +0x00
        Array.Copy(nameBytes, 0, p, sd + 0x00, nameLen);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(sd + 0x3A, 2), level);          // +0x3A
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(sd + 0x3C, 4), currentHp);      // +0x3C
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(sd + 0x40, 4), currentMp);      // +0x40
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(sd + 0x44, 4), currentStamina); // +0x44
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(sd + 0x4C, 4), worldX);         // +0x4C
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(sd + 0x50, 4), worldZ);         // +0x50
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(sd + 0x74, 2), serverClass);    // +0x74

        return Frame(5, 1, p);
    }

    /// <summary>5/32 level up (48-byte payload). spec: 5-32_level_up.yaml.</summary>
    public static byte[] LevelUp(
        byte sort, uint actorId, ushort newLevel, uint currentHp, uint currentMp, uint stamina,
        int remainingStatPoints = 0)
    {
        Span<byte> p = stackalloc byte[48];
        p.Clear();
        p[0x00] = sort;
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), actorId);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x08, 2), newLevel);
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x0c, 4), remainingStatPoints);
        // HpMpPacked @0x14: HP = low i32, MP = high i32.
        long packed = (long)currentHp | ((long)currentMp << 32);
        BinaryPrimitives.WriteInt64LittleEndian(p.Slice(0x14, 8), packed);
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x1c, 4), (int)stamina); // Stamina @0x1c
        return Frame(5, 32, p);
    }

    /// <summary>4/29 stat update (36-byte payload). spec: 4-29_stat_update.yaml.</summary>
    public static byte[] StatUpdate(
        uint handle, byte resultOk, uint stat0, uint stat1, uint stat2, uint stat3, uint stat4,
        uint remainingStatPoints)
    {
        Span<byte> p = stackalloc byte[36];
        p.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x00, 4), handle);
        p[0x08] = resultOk;
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x0c, 4), stat0);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x10, 4), stat1);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x14, 4), stat2);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x18, 4), stat3);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x1c, 4), stat4);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x20, 4), remainingStatPoints);
        return Frame(4, 29, p);
    }

    /// <summary>0/0 KeyExchange (62-byte payload). spec: crypto.md §6.2.1.</summary>
    public static byte[] KeyExchange(byte[] modulusDigits, byte[] exponentDigits, uint scalar1, uint scalar2)
    {
        var payload = new byte[62];
        payload[0] = 0xAB; payload[1] = 0xCD; payload[2] = 0xEF; payload[3] = 0x01; // opaque headers
        int cursor = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor, 4), (uint)modulusDigits.Length);
        cursor += 4;
        modulusDigits.CopyTo(payload.AsSpan(cursor));
        cursor += modulusDigits.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor, 4), (uint)exponentDigits.Length);
        cursor += 4;
        exponentDigits.CopyTo(payload.AsSpan(cursor));
        cursor += exponentDigits.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor, 4), scalar1);
        cursor += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor, 4), scalar2);
        return Frame(0, 0, payload);
    }

    /// <summary>
    /// 5/3 char spawn (908-byte payload: 8-byte head + 880-byte SpawnDescriptor + 20-byte trailer).
    /// SpawnDescriptor sub-fields per Docs/RE/structs/actor.md. spec: 5-3_char_spawn.yaml.
    /// </summary>
    public static byte[] CharSpawn(
        byte sort, uint actorId, string name, ushort level,
        uint currentHp, uint currentMp, uint currentStamina,
        float worldX, float worldZ, ushort serverClass)
    {
        var p = new byte[908];

        // packet-level head
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x000, 4), sort);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x004, 4), actorId);

        // SpawnDescriptor starts at packet offset 0x008. Sub-offsets are descriptor-relative.
        const int sd = 0x008;
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        int nameLen = Math.Min(nameBytes.Length, 16); // up to 16 chars + NUL. spec: actor.md +0x00
        Array.Copy(nameBytes, 0, p, sd + 0x00, nameLen);
        // NUL terminator already present (zeroed array).
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(sd + 0x3A, 2), level);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(sd + 0x3C, 4), currentHp);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(sd + 0x40, 4), currentMp);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(sd + 0x44, 4), currentStamina);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(sd + 0x4C, 4), worldX);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(sd + 0x50, 4), worldZ);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(sd + 0x74, 2), serverClass);

        return Frame(5, 3, p);
    }
}
