// Layout + decode guards for the packet structs added this pass. Each struct's runtime size must
// equal its spec `size:` / declared header size, the Pack=1 alignment must hold (no padding drift),
// and a synthetic byte buffer must decode to the expected per-offset values. spec sources cited per
// assertion. CAPTURE-UNVERIFIED layouts (capture_verified: false / static inference).

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class NewPacketLayoutTests
{
    // -------------------------------------------------------------------------
    // Size guards (Marshal.SizeOf == spec size, and Unsafe.SizeOf agrees => Pack=1 has no padding)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/structs/item.md (ItemSlotRecord — 16-byte unit)
    public void ItemSlotRecord_size_is_16()
    {
        Assert.Equal(16, Marshal.SizeOf<ItemSlotRecord>());
        Assert.Equal(16, Unsafe.SizeOf<ItemSlotRecord>());
        Assert.Equal(16, ItemSlotRecord.WireSize);
    }

    [Fact] // spec: Docs/RE/structs/item.md (EquipItemResult — 16-byte header, opcode 4/12)
    public void EquipItemResult_size_is_16()
    {
        Assert.Equal(16, Marshal.SizeOf<SmsgEquipItemResult>());
        Assert.Equal(16, Unsafe.SizeOf<SmsgEquipItemResult>());
        Assert.Equal(16, SmsgEquipItemResult.WireSize);
    }

    [Fact] // spec: Docs/RE/structs/item.md (EquipSlotBody — 36-byte body, opcode 4/22)
    public void ItemSlotStateAck_size_is_36()
    {
        Assert.Equal(36, Marshal.SizeOf<SmsgItemSlotStateAck>());
        Assert.Equal(36, Unsafe.SizeOf<SmsgItemSlotStateAck>());
        Assert.Equal(36, SmsgItemSlotStateAck.WireSize);
    }

    [Fact] // spec: Docs/RE/structs/item.md (NpcBuy / inventory-acquire ack — 56-byte body, opcode 4/19)
    public void NpcBuyOrAcquireAck_size_is_56()
    {
        Assert.Equal(56, Marshal.SizeOf<SmsgNpcBuyOrAcquireAck>());
        Assert.Equal(56, Unsafe.SizeOf<SmsgNpcBuyOrAcquireAck>());
        Assert.Equal(56, SmsgNpcBuyOrAcquireAck.WireSize);
    }

    [Fact] // spec: Docs/RE/structs/skill.md (SkillHotbarSlotSet — 20-byte packet, opcode 5/33)
    public void SkillHotbarSlotSet_size_is_20()
    {
        Assert.Equal(20, Marshal.SizeOf<SmsgSkillHotbarSlotSet>());
        Assert.Equal(20, Unsafe.SizeOf<SmsgSkillHotbarSlotSet>());
        Assert.Equal(20, SmsgSkillHotbarSlotSet.WireSize);
    }

    [Fact] // spec: Docs/RE/structs/skill.md (SkillHotbarAssignResult — 24-byte packet, opcode 4/41)
    public void SkillHotbarAssignResult_size_is_24()
    {
        Assert.Equal(24, Marshal.SizeOf<SmsgSkillHotbarAssignResult>());
        Assert.Equal(24, Unsafe.SizeOf<SmsgSkillHotbarAssignResult>());
        Assert.Equal(24, SmsgSkillHotbarAssignResult.WireSize);
    }

    [Fact] // spec: Docs/RE/structs/skill.md (SkillPointUpdate — minimum 16-byte header, opcode 4/150)
    public void SkillPointUpdateHeader_size_is_16()
    {
        Assert.Equal(16, Marshal.SizeOf<SmsgSkillPointUpdateHeader>());
        Assert.Equal(16, Unsafe.SizeOf<SmsgSkillPointUpdateHeader>());
        Assert.Equal(16, SmsgSkillPointUpdateHeader.HeaderSize);
    }

    [Fact] // spec: Docs/RE/packets/5-7_chat_broadcast.yaml (36-byte header)
    public void ChatBroadcastHeader_size_is_36()
    {
        Assert.Equal(36, Marshal.SizeOf<SmsgChatBroadcastHeader>());
        Assert.Equal(36, Unsafe.SizeOf<SmsgChatBroadcastHeader>());
        Assert.Equal(36, SmsgChatBroadcastHeader.HeaderSize);
    }

    [Fact] // spec: Docs/RE/packets/3-1_character_list.yaml (3-byte header)
    public void CharacterListHeader_size_is_3()
    {
        Assert.Equal(3, Marshal.SizeOf<SmsgCharacterListHeader>());
        Assert.Equal(3, Unsafe.SizeOf<SmsgCharacterListHeader>());
        Assert.Equal(3, SmsgCharacterListHeader.HeaderSize);
        Assert.Equal(981, SmsgCharacterListHeader.SlotRecordSize); // 880 + 96 + 1 + 4 per spec
    }

    // -------------------------------------------------------------------------
    // Decode guards (synthetic buffer -> fields land at their specced offsets)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/structs/item.md (ItemSlotRecord)
    public void ItemSlotRecord_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[ItemSlotRecord.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x00..], 0xAABBCCDDu); // Word0
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x04..], 0x11223344u); // ItemActorId
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x08..], 0x55667788u); // ExpiryLo
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x0c..], 0x99AABBCCu); // ExpiryHi

        ref readonly ItemSlotRecord r = ref MemoryMarshal.AsRef<ItemSlotRecord>(body);
        Assert.Equal(0xAABBCCDDu, r.Word0);
        Assert.Equal(0x11223344u, r.ItemActorId);
        Assert.Equal(0x55667788u, r.ExpiryLo);
        Assert.Equal(0x99AABBCCu, r.ExpiryHi);
    }

    [Fact] // spec: Docs/RE/structs/item.md (EquipItemResult, opcode 4/12)
    public void EquipItemResult_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgEquipItemResult.WireSize];
        body[0x00] = 1; // Guard
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x04..], 0xDEADBEEFu); // ActorSortKey
        body[0x08] = 1; // Result = ok
        body[0x0a] = 5; // FromSlot
        body[0x0b] = 6; // FromSub
        body[0x0c] = 15; // ToSlot (visual gear refresh trigger)

        ref readonly SmsgEquipItemResult p = ref MemoryMarshal.AsRef<SmsgEquipItemResult>(body);
        Assert.Equal((byte)1, p.Guard);
        Assert.Equal(0xDEADBEEFu, p.ActorSortKey);
        Assert.Equal((byte)1, p.Result);
        Assert.Equal((byte)5, p.FromSlot);
        Assert.Equal((byte)6, p.FromSub);
        Assert.Equal((byte)15, p.ToSlot);
    }

    [Fact] // spec: Docs/RE/structs/item.md (EquipSlotBody, opcode 4/22)
    public void ItemSlotStateAck_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgItemSlotStateAck.WireSize];
        body[0x08] = 1; // Result
        body[0x0a] = 2; // FromSlot
        body[0x0b] = 3; // ToSlot
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x0c..], 0x44u); // FlagC
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x10..], 0x55u); // Flag10
        BinaryPrimitives.WriteInt32LittleEndian(body[0x18..], 100); // BonusField1
        BinaryPrimitives.WriteInt32LittleEndian(body[0x1c..], 200); // BonusField2
        BinaryPrimitives.WriteInt32LittleEndian(body[0x20..], 300); // BonusField3

        ref readonly SmsgItemSlotStateAck p = ref MemoryMarshal.AsRef<SmsgItemSlotStateAck>(body);
        Assert.Equal((byte)1, p.Result);
        Assert.Equal((byte)2, p.FromSlot);
        Assert.Equal((byte)3, p.ToSlot);
        Assert.Equal(0x44u, p.FlagC);
        Assert.Equal(0x55u, p.Flag10);
        Assert.Equal(100, p.BonusField1);
        Assert.Equal(200, p.BonusField2);
        Assert.Equal(300, p.BonusField3);
    }

    [Fact] // spec: Docs/RE/structs/item.md (NpcBuy / inventory-acquire ack, opcode 4/19)
    public void NpcBuyOrAcquireAck_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgNpcBuyOrAcquireAck.WireSize];
        BinaryPrimitives.WriteInt32LittleEndian(body[0x04..], 77); // ActorId
        BinaryPrimitives.WriteInt32LittleEndian(body[0x08..], 1500); // GoldLo
        BinaryPrimitives.WriteInt32LittleEndian(body[0x0c..], 0); // GoldHi
        body[0x10] = 1; // Result
        body[0x11] = 1; // ReasonCode
        body[0x12] = 9; // BagSlotIndex
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x20..], 11u); // RepairVal1
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x24..], 22u); // RepairVal2
        BinaryPrimitives.WriteInt32LittleEndian(body[0x28..], 1001); // ItemQuadA
        BinaryPrimitives.WriteInt32LittleEndian(body[0x2c..], 2002); // ItemQuadB (item actor id)
        BinaryPrimitives.WriteInt32LittleEndian(body[0x30..], 3); // ItemQuadC (stack)
        BinaryPrimitives.WriteInt32LittleEndian(body[0x34..], 4004); // ItemQuadD

        ref readonly SmsgNpcBuyOrAcquireAck p = ref MemoryMarshal.AsRef<SmsgNpcBuyOrAcquireAck>(body);
        Assert.Equal(77, p.ActorId);
        Assert.Equal(1500, p.GoldLo);
        Assert.Equal(0, p.GoldHi);
        Assert.Equal((byte)1, p.Result);
        Assert.Equal((byte)1, p.ReasonCode);
        Assert.Equal((byte)9, p.BagSlotIndex);
        Assert.Equal(11u, p.RepairVal1);
        Assert.Equal(22u, p.RepairVal2);
        Assert.Equal(1001, p.ItemQuadA);
        Assert.Equal(2002, p.ItemQuadB);
        Assert.Equal(3, p.ItemQuadC);
        Assert.Equal(4004, p.ItemQuadD);
    }

    [Fact] // spec: Docs/RE/structs/skill.md (SkillHotbarSlotSet, opcode 5/33)
    public void SkillHotbarSlotSet_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgSkillHotbarSlotSet.WireSize];
        BinaryPrimitives.WriteInt32LittleEndian(body[0x00..], 1); // Sort (PC in low byte)
        BinaryPrimitives.WriteInt32LittleEndian(body[0x04..], 42); // ActorId
        body[0x08] = 7; // HotbarSlot
        BinaryPrimitives.WriteInt32LittleEndian(body[0x0c..], 9001); // SkillId
        BinaryPrimitives.WriteInt16LittleEndian(body[0x10..], 5); // SkillPoints

        ref readonly SmsgSkillHotbarSlotSet p = ref MemoryMarshal.AsRef<SmsgSkillHotbarSlotSet>(body);
        Assert.Equal(1, p.Sort);
        Assert.Equal(ActorSort.PlayerCharacter, p.SortKind);
        Assert.Equal(42, p.ActorId);
        Assert.Equal((byte)7, p.HotbarSlot);
        Assert.Equal(9001, p.SkillId);
        Assert.Equal((short)5, p.SkillPoints);
        Assert.Equal(240, SmsgSkillHotbarSlotSet.HotbarSlotCount);
    }

    [Fact] // spec: Docs/RE/structs/skill.md (SkillHotbarAssignResult, opcode 4/41)
    public void SkillHotbarAssignResult_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgSkillHotbarAssignResult.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x00..], 1u); // Header
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x04..], 42u); // ActorId
        body[0x08] = 1; // Gate
        body[0x09] = 3; // ResultCode
        BinaryPrimitives.WriteInt32LittleEndian(body[0x0c..], 7); // HotbarSlotEcho
        BinaryPrimitives.WriteInt32LittleEndian(body[0x10..], 9001); // SkillIdEcho
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x14..], 12u); // SkillPointPool

        ref readonly SmsgSkillHotbarAssignResult p = ref MemoryMarshal.AsRef<SmsgSkillHotbarAssignResult>(body);
        Assert.Equal(1u, p.Header);
        Assert.Equal(42u, p.ActorId);
        Assert.Equal((byte)1, p.Gate);
        Assert.Equal((byte)3, p.ResultCode);
        Assert.Equal(7, p.HotbarSlotEcho);
        Assert.Equal(9001, p.SkillIdEcho);
        Assert.Equal(12u, p.SkillPointPool);
    }

    [Fact] // spec: Docs/RE/structs/skill.md (SkillPointUpdate header, opcode 4/150)
    public void SkillPointUpdateHeader_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgSkillPointUpdateHeader.HeaderSize];
        body[0x00] = 1; // Valid
        BinaryPrimitives.WriteInt32LittleEndian(body[0x04..], 42); // IdKey
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x08..], 2u); // Mode (level-up notice)
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x0c..], 30u); // Value (new level)

        ref readonly SmsgSkillPointUpdateHeader p = ref MemoryMarshal.AsRef<SmsgSkillPointUpdateHeader>(body);
        Assert.Equal((byte)1, p.Valid);
        Assert.Equal(42, p.IdKey);
        Assert.Equal(2u, p.Mode);
        Assert.Equal(30u, p.Value);
    }

    [Fact] // spec: Docs/RE/packets/5-7_chat_broadcast.yaml (36-byte header)
    public void ChatBroadcastHeader_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgChatBroadcastHeader.HeaderSize];
        body[0x00] = 1; // SenderSort = PC
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x04..], 0xCAFEu); // SenderId
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x08..], 0xBEEFu); // ContextId
        body[0x0c] = 0xA1; // Reserved0C
        body[0x0d] = 4; // SubCommand
        body[0x0e] = 7; // Channel (whisper)
        body[0x0f] = 0xA2; // Reserved0F
        // SenderName "Sifu\0" CP949/ascii into the 20-byte buffer at 0x10
        body[0x10] = (byte)'S';
        body[0x11] = (byte)'i';
        body[0x12] = (byte)'f';
        body[0x13] = (byte)'u';

        ref readonly SmsgChatBroadcastHeader p = ref MemoryMarshal.AsRef<SmsgChatBroadcastHeader>(body);
        Assert.Equal((byte)1, p.SenderSort);
        Assert.Equal(ActorSort.PlayerCharacter, p.SenderSortKind);
        Assert.Equal(0xCAFEu, p.SenderId);
        Assert.Equal(0xBEEFu, p.ContextId);
        Assert.Equal((byte)4, p.SubCommand);
        Assert.Equal((byte)7, p.Channel);
        Assert.Equal((byte)'S', p.SenderName[0]);
        Assert.Equal((byte)'u', p.SenderName[3]);
        Assert.Equal((byte)0, p.SenderName[4]); // NUL terminator
    }

    [Fact] // spec: Docs/RE/packets/3-1_character_list.yaml (3-byte header)
    public void CharacterListHeader_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgCharacterListHeader.HeaderSize];
        body[0x00] = 2; // ServerId
        body[0x01] = 5; // ChannelId
        body[0x02] = 0b0000_0011; // SlotMask: slots 0 and 1 populated

        ref readonly SmsgCharacterListHeader p = ref MemoryMarshal.AsRef<SmsgCharacterListHeader>(body);
        Assert.Equal((byte)2, p.ServerId);
        Assert.Equal((byte)5, p.ChannelId);
        Assert.Equal((byte)0b0000_0011, p.SlotMask);
    }
}
