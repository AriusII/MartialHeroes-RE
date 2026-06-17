// Compile-time opcode → struct-size routing map + the zero-copy typed-view seam. These are additive
// and do not touch the IPacketHandler dispatch in PacketRouter (which Client.Application drives via
// OnUnhandled). spec: Docs/RE/opcodes.md and the per-packet specs.

using System.Buffers.Binary;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Routing;
using OpcodeConsts = MartialHeroes.Network.Protocol.Opcodes.Opcodes;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class PacketWireSizesTests
{
    [Theory] // spec: Docs/RE/opcodes.md + per-packet specs — each opcode maps to its struct size.
    [InlineData(OpcodeConsts.SmsgGameStateTick, 0x238C, true)] // handlers.md §4/1
    [InlineData(OpcodeConsts.SmsgEquipItemResult, 16, false)] // item.md 4/12
    [InlineData(OpcodeConsts.SmsgItemSlotStateAck, 36, false)] // item.md 4/22
    [InlineData(OpcodeConsts.SmsgNpcBuyOrAcquireAck, 56, false)] // item.md 4/19
    [InlineData(OpcodeConsts.SmsgSkillHotbarSlotSet, 20, false)] // skill.md 5/33
    [InlineData(OpcodeConsts.SmsgSkillHotbarAssignResult, 24, false)] // skill.md 4/41
    [InlineData(OpcodeConsts.SmsgCharDespawn, 12, false)] // 5/0
    [InlineData(OpcodeConsts.SmsgActorVitalsAndPairState, 32, false)] // 5/53
    [InlineData(OpcodeConsts.SmsgChatBroadcast, 36, true)] // 5/7 var
    [InlineData(OpcodeConsts.SmsgCharacterList, 3, true)] // 3/1 var
    [InlineData(OpcodeConsts.SmsgSkillPointUpdate, 16, true)] // 4/150 var
    [InlineData(OpcodeConsts.CmsgMoveRequest, 16, false)] // 2/13
    [InlineData(OpcodeConsts.CmsgWhisper, 19, true)] // 2/7 var
    [InlineData(OpcodeConsts.CmsgKeepaliveToggle, 1, false)] // 2/112 — opcodes.md (1-byte body)
    public void TryGet_returns_struct_size_for_known_opcodes(uint opcode, int expectedSize, bool expectedVar)
    {
        Assert.True(PacketWireSizes.TryGet(opcode, out int size, out bool isVar));
        Assert.Equal(expectedSize, size);
        Assert.Equal(expectedVar, isVar);
    }

    [Fact] // unmapped opcode (5/9 ExpGain has no modelled struct) -> false.
    public void TryGet_returns_false_for_unmapped_opcode()
    {
        Assert.False(PacketWireSizes.TryGet(OpcodeConsts.SmsgExpGain, out int size, out bool isVar));
        Assert.Equal(0, size);
        Assert.False(isVar);
    }

    [Fact] // TypedPacketView.As reinterprets in place with zero copy.
    public void TypedPacketView_As_reinterprets_in_place()
    {
        Span<byte> body = stackalloc byte[SmsgSkillHotbarSlotSet.WireSize];
        BinaryPrimitives.WriteInt32LittleEndian(body[0x04..], 99); // ActorId
        body[0x08] = 12; // HotbarSlot

        ref readonly SmsgSkillHotbarSlotSet p =
            ref TypedPacketView.As<SmsgSkillHotbarSlotSet>(body, SmsgSkillHotbarSlotSet.WireSize);

        Assert.Equal(99, p.ActorId);
        Assert.Equal((byte)12, p.HotbarSlot);
    }

    [Fact] // TypedPacketView.As throws when the payload is too small.
    public void TypedPacketView_As_throws_when_payload_too_small()
    {
        byte[] tooSmall = new byte[8];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = TypedPacketView.As<SmsgSkillHotbarSlotSet>(tooSmall, SmsgSkillHotbarSlotSet.WireSize);
        });
    }

    [Fact] // TypedPacketView.TryAs returns false (no throw) when the payload is too small.
    public void TypedPacketView_TryAs_returns_false_when_payload_too_small()
    {
        byte[] tooSmall = new byte[4];
        Assert.False(TypedPacketView.TryAs<SmsgEquipItemResult>(
            tooSmall, SmsgEquipItemResult.WireSize, out SmsgEquipItemResult view));
        Assert.Equal((byte)0, view.Result); // left at default on failure
    }

    [Fact] // spec: handlers.md §4/1; client_runtime.md §9.4.
    public void GameStateTick_reads_world_entry_seed_from_pinned_offsets()
    {
        var payload = new byte[SmsgGameStateTick.WireSize];
        payload[SmsgGameStateTick.FormOffset] = SmsgGameStateTick.WorldEntryForm;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(SmsgGameStateTick.ScenarioModeOffset, 4), 6);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(SmsgGameStateTick.SpawnXOffset, 4), 12.5f);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(SmsgGameStateTick.SpawnZOffset, 4), -3.25f);

        Assert.True(SmsgGameStateTick.TryReadWorldEntrySeed(payload, out SmsgGameStateTickSeed seed));
        Assert.Equal((byte)1, seed.Form);
        Assert.Equal(6, seed.ScenarioMode);
        Assert.Equal(12.5f, seed.SpawnX);
        Assert.Equal(-3.25f, seed.SpawnZ);
    }

    [Fact] // form byte +0 must be 1 for the world-entry branch.
    public void GameStateTick_rejects_non_world_entry_form()
    {
        var payload = new byte[SmsgGameStateTick.WorldEntrySeedSize];
        payload[SmsgGameStateTick.FormOffset] = 3;

        Assert.False(SmsgGameStateTick.TryReadWorldEntrySeed(payload, out _));
    }
}