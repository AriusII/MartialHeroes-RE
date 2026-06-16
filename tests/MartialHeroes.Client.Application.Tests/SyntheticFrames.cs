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
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), major); // major@+4
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), minor); // minor@+6
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
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x10, 4), currentHp); // CurrentHp @0x10
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x18, 4), stamina); // Stamina @0x18
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x1c, 4), vitalC); // VitalC (MP) @0x1c
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
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(sd + 0x3A, 2), level); // +0x3A
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(sd + 0x3C, 4), currentHp); // +0x3C
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(sd + 0x40, 4), currentMp); // +0x40
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(sd + 0x44, 4), currentStamina); // +0x44
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(sd + 0x4C, 4), worldX); // +0x4C
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(sd + 0x50, 4), worldZ); // +0x50
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(sd + 0x74, 2), serverClass); // +0x74

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
        payload[0] = 0xAB;
        payload[1] = 0xCD;
        payload[2] = 0xEF;
        payload[3] = 0x01; // opaque headers
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

    /// <summary>4/12 equip result (16-byte payload). spec: structs/item.md (EquipItemResult).</summary>
    public static byte[] EquipResult(byte result, byte fromSlot, byte toSlot, byte guard = 1)
    {
        Span<byte> p = stackalloc byte[16];
        p.Clear();
        p[0x00] = guard;
        p[0x08] = result; // 0 = error, 1 = ok
        p[0x0a] = fromSlot;
        p[0x0c] = toSlot;
        return Frame(4, 12, p);
    }

    /// <summary>4/22 item-slot state ack (36-byte payload). spec: structs/item.md (EquipSlotBody).</summary>
    public static byte[] ItemSlotState(
        byte result, byte fromSlot, byte toSlot, int bonus1, int bonus2, int bonus3)
    {
        Span<byte> p = stackalloc byte[36];
        p.Clear();
        p[0x08] = result;
        p[0x0a] = fromSlot;
        p[0x0b] = toSlot;
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x18, 4), bonus1);
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x1c, 4), bonus2);
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x20, 4), bonus3);
        return Frame(4, 22, p);
    }

    /// <summary>4/19 NPC buy / acquire ack (56-byte payload). spec: structs/item.md (NpcBuy ack).</summary>
    public static byte[] NpcAcquire(byte result, byte reason, byte bagSlot, int itemActorId, int goldLo)
    {
        Span<byte> p = stackalloc byte[56];
        p.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x08, 4), goldLo); // GoldLo @0x08
        p[0x10] = result;
        p[0x11] = reason;
        p[0x12] = bagSlot;
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x2c, 4), itemActorId); // ItemQuadB @0x2c
        return Frame(4, 19, p);
    }

    /// <summary>5/33 skill hotbar slot set (20-byte payload). spec: structs/skill.md (SkillHotbarSlotSet).</summary>
    public static byte[] HotbarSlotSet(byte sort, uint actorId, byte hotbarSlot, int skillId, short points)
    {
        Span<byte> p = stackalloc byte[20];
        p.Clear();
        p[0x00] = sort;
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), actorId);
        p[0x08] = hotbarSlot;
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x0c, 4), skillId);
        BinaryPrimitives.WriteInt16LittleEndian(p.Slice(0x10, 2), points);
        return Frame(5, 33, p);
    }

    /// <summary>4/41 skill hotbar assign result (24-byte payload). spec: structs/skill.md (SkillHotbarAssignResult).</summary>
    public static byte[] HotbarAssignResult(
        byte gate, byte resultCode, int hotbarSlotEcho, int skillIdEcho, uint pool, uint header = 1, uint actorId = 1)
    {
        Span<byte> p = stackalloc byte[24];
        p.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x00, 4), header);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), actorId);
        p[0x08] = gate;
        p[0x09] = resultCode;
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x0c, 4), hotbarSlotEcho);
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x10, 4), skillIdEcho);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x14, 4), pool);
        return Frame(4, 41, p);
    }

    /// <summary>4/150 skill-point update (16-byte fixed header). spec: structs/skill.md (SkillPointUpdate).</summary>
    public static byte[] SkillPointUpdate(uint mode, uint value, int idKey = 7, byte valid = 1)
    {
        Span<byte> p = stackalloc byte[16];
        p.Clear();
        p[0x00] = valid;
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x04, 4), idKey);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x08, 4), mode);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x0c, 4), value);
        return Frame(4, 150, p);
    }

    /// <summary>5/31 buff slot update (56-byte payload). spec: handlers.md §4 (5/31).</summary>
    public static byte[] BuffSlotUpdate(byte sort, uint actorId, uint slot, uint effectCode, uint duration, uint extra)
    {
        Span<byte> p = stackalloc byte[56];
        p.Clear();
        p[0x00] = sort;
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), actorId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x08, 4), slot);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x0c, 4), effectCode);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x10, 4), duration);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x14, 4), extra);
        return Frame(5, 31, p);
    }

    /// <summary>5/67 stats update (36-byte payload). spec: handlers.md §4 (5/67).</summary>
    public static byte[] StatsUpdate(
        byte sort, uint actorId, uint stat0, uint stat2, long currentXp, uint stat6, uint stat4, uint stat5)
    {
        Span<byte> p = stackalloc byte[36];
        p.Clear();
        p[0x00] = sort;
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), actorId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x08, 4), stat0);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x0c, 4), stat2);
        BinaryPrimitives.WriteInt64LittleEndian(p.Slice(0x10, 8), currentXp);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x18, 4), stat6);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x1c, 4), stat4);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x20, 4), stat5);
        return Frame(5, 67, p);
    }

    /// <summary>5/9 experience gain (32-byte payload). spec: Docs/RE/specs/progression.md §3.4.</summary>
    public static byte[] ExpGain(
        byte sort, uint actorId, long amount, uint sourceSort = 0, uint sourceId = 0,
        int profSlotA = -1, int profSlotB = -1)
    {
        Span<byte> p = stackalloc byte[32];
        p.Clear();
        p[0x00] = sort; // recipient sort (low byte of the +0 u32). spec: progression.md §3.4.
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), actorId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x08, 4), sourceSort); // low byte == 2 enables the §3.1 split
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x0c, 4), sourceId);
        BinaryPrimitives.WriteInt64LittleEndian(p.Slice(0x10, 8), amount); // experience amount
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x18, 4), profSlotA);
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(0x1c, 4), profSlotB);
        return Frame(5, 9, p);
    }

    /// <summary>5/11 rank/honor XP gain (20-byte payload). spec: Docs/RE/specs/progression.md §4.1.</summary>
    public static byte[] RankXpGain(uint actorId, byte sort, ulong amount, byte mode)
    {
        Span<byte> p = stackalloc byte[20];
        p.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x00, 4), actorId);
        p[0x04] = sort;
        BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(0x08, 8), amount);
        p[0x10] = mode; // 2 = direct add (no level math). spec: progression.md §4.1.
        return Frame(5, 11, p);
    }

    /// <summary>4/100 combat attack update (188-byte payload). spec: handlers.md §3 (4/100).</summary>
    public static byte[] CombatAttackUpdate(byte phase, sbyte subKind, uint value)
    {
        Span<byte> p = stackalloc byte[188];
        p.Clear();
        p[0x08] = phase;
        p[0x0a] = unchecked((byte)subKind);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x0c, 4), value);
        return Frame(4, 100, p);
    }

    /// <summary>
    /// 5/7 chat broadcast (36-byte header + length-prefixed text body). spec: 5-7_chat_broadcast.yaml.
    /// </summary>
    public static byte[] ChatBroadcast(
        byte senderSort, uint senderId, byte channel, uint contextId, string senderName, string text)
    {
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(senderName);
        byte[] textBytes = System.Text.Encoding.ASCII.GetBytes(text);

        // 36-byte header + [u32 len incl NUL][text][0x00]. spec: handlers.md §17.12.
        var p = new byte[36 + 4 + textBytes.Length + 1];
        p[0x00] = senderSort;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x04, 4), senderId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x08, 4), contextId);
        p[0x0e] = channel;
        int nameLen = Math.Min(nameBytes.Length, 19); // 20-byte buffer, leave a NUL
        Array.Copy(nameBytes, 0, p, 0x10, nameLen);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(36, 4), (uint)(textBytes.Length + 1));
        Array.Copy(textBytes, 0, p, 40, textBytes.Length);
        return Frame(5, 7, p);
    }

    /// <summary>
    /// 3/1 character list (3-byte header + one 981-byte record per set bit). Each record's first 880
    /// bytes are a SpawnDescriptor. spec: 3-1_character_list.yaml.
    /// </summary>
    public static byte[] CharacterList(byte serverId, byte channelId,
        params (int Slot, string Name, ushort Level, uint Hp, ushort Class)[] slots)
    {
        byte mask = 0;
        foreach (var s in slots)
        {
            mask |= (byte)(1 << s.Slot);
        }

        const int recordSize = 981;
        var p = new byte[3 + slots.Length * recordSize];
        p[0x00] = serverId;
        p[0x01] = channelId;
        p[0x02] = mask;

        // Records appear in ascending slot order (LSB-first). Sort the supplied slots to match the decode.
        var ordered = slots.OrderBy(s => s.Slot).ToArray();
        int cursor = 3;
        foreach (var s in ordered)
        {
            int sd = cursor; // descriptor starts at the record start (first 880 bytes).
            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(s.Name);
            int nameLen = Math.Min(nameBytes.Length, 16);
            Array.Copy(nameBytes, 0, p, sd + 0x00, nameLen);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(sd + 0x3A, 2), s.Level);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(sd + 0x3C, 4), s.Hp);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(sd + 0x74, 2), s.Class);
            cursor += recordSize;
        }

        return Frame(3, 1, p);
    }

    /// <summary>
    /// 3/14 char-spawn response (16-byte payload). spec: opcodes.md (3/14 SmsgCharSpawnResponse);
    /// login_flow.md §5.3 (result + slot + 3×u32). CAMPAIGN-10 ladder de-swap: the 16-byte enter-game
    /// spawn result is opcode 3/14 (was mislabelled 3/7; 3/7 is the 8-byte manage result).
    /// </summary>
    public static byte[] CharSpawnResult(
        byte result, byte slot, uint param1 = 0, uint param2 = 0, uint param3 = 0)
    {
        Span<byte> p = stackalloc byte[16];
        p.Clear();
        p[0x00] = result; // 0 = failure; nonzero = spawn
        p[0x01] = slot;
        // 0x02 padding (u16) stays zero.
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), param1);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x08, 4), param2);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x0c, 4), param3);
        return Frame(3, 14, p); // spec: opcodes.md — 3/14 SmsgCharSpawnResponse (16-byte enter-game spawn)
    }

    /// <summary>
    /// 3/7 char manage / delete result (8-byte payload). spec: opcodes.md (3/7 SmsgCharManageResult);
    /// login_flow.md §5.5 (result@0, subtype@2, ready_time u32@4). CAMPAIGN-10 ladder de-swap: the
    /// 8-byte manage/delete result is opcode 3/7 (was mislabelled 3/4; 3/4 is SceneEntityUpdate).
    /// </summary>
    public static byte[] CharManageResult(byte result, byte subtype, uint readyTime)
    {
        Span<byte> p = stackalloc byte[8];
        p.Clear();
        p[0x00] = result; // 1 = success
        // 0x01 reserved
        p[0x02] = subtype; // 0/1/2 (2 = delete-confirm)
        // 0x03 reserved
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), readyTime);
        return Frame(3, 7, p); // spec: opcodes.md — 3/7 SmsgCharManageResult (8-byte manage/delete)
    }

    /// <summary>
    /// 3/6 rename result (19-byte payload). spec: login_flow.md §5.6 (result@0; 18-byte name-or-error
    /// overlay @1). On success the overlay holds a CP949/ASCII name; on failure overlay[0] = error code.
    /// </summary>
    public static byte[] RenameCharResult(byte result, string? name = null, byte errorCode = 0)
    {
        Span<byte> p = stackalloc byte[19];
        p.Clear();
        p[0x00] = result; // nonzero = success
        if (result != 0 && name is not null)
        {
            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
            int n = Math.Min(nameBytes.Length, 17); // 18-byte field leaves room for a NUL
            for (int i = 0; i < n; i++)
            {
                p[0x01 + i] = nameBytes[i];
            }
        }
        else if (result == 0)
        {
            p[0x01] = errorCode; // NameOrError[0] carries the error code on failure
        }

        return Frame(3, 6, p);
    }

    /// <summary>
    /// 3/23 character-create result (12-byte payload). spec: login_flow.md §5.4
    /// (result@0, code@1, value1 u32@4, value2 u32@8).
    /// </summary>
    public static byte[] CharCreateResult(byte result, byte code, uint value1 = 0, uint value2 = 0)
    {
        Span<byte> p = stackalloc byte[12];
        p.Clear();
        p[0x00] = result; // 1 = success
        p[0x01] = code; // assigned slot id on success / error code on failure
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), value1);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x08, 4), value2);
        return Frame(3, 23, p);
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