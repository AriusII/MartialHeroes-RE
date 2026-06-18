// spec: Docs/RE/opcodes.md; Docs/RE/specs/handlers.md §4/1; Docs/RE/specs/client_runtime.md §9.1/§9.4.
// 4/1 is the 9100-byte world-state tick / world-entry payload. Routing and the fixed read size are
// confirmed; interior value semantics remain capture/debugger-pending.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 4/1 — server game-state tick and world-entry snapshot. Fixed 9100-byte body in the recovered
/// handler, modelled as an opaque payload with accessor helpers for the pinned world-entry seed.
/// spec: Docs/RE/specs/handlers.md §4/1; Docs/RE/specs/client_runtime.md §9.1/§9.4.
/// </summary>
/// <remarks>
/// The handler branches on body byte +0; form <c>1</c> is the world-entry path. The only payload
/// fields this clean slice consumes are scenario code at +0x00C and spawn X/Z at +0x2374/+0x2378.
/// World Y is not on the wire and is forced to zero by Application.
/// </remarks>
[PacketOpcode(4, 1)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGameStateTick
{
    /// <summary>Packed opcode 0x40001 (4/1). spec: Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgGameStateTick;

    /// <summary>Fixed body read size: 9100 bytes (0x238C). spec: handlers.md §4/1.</summary>
    public const int WireSize = 0x238C;

    /// <summary>Minimum length needed to read form, scenario, and spawn X/Z. spec: client_runtime.md §9.4.</summary>
    public const int WorldEntrySeedSize = SpawnZOffset + sizeof(float);

    /// <summary>Body byte +0 selector; value 1 is the world-entry form. spec: handlers.md §4/1.</summary>
    public const int FormOffset = 0x0000;

    /// <summary>Body byte +0 value for the world-entry branch. spec: handlers.md §4/1.</summary>
    public const byte WorldEntryForm = 1;

    /// <summary>Scenario/map-mode code at body +0x00C. spec: client_runtime.md §9.1 step 5 / §9.4.</summary>
    public const int ScenarioModeOffset = 0x000C;

    /// <summary>Spawn X at body +0x2374. spec: client_runtime.md §9.1 step 5 / §9.4.</summary>
    public const int SpawnXOffset = 0x2374;

    /// <summary>Spawn Z at body +0x2378. spec: client_runtime.md §9.1 step 5 / §9.4.</summary>
    public const int SpawnZOffset = 0x2378;

    /// <summary>Opaque 9100-byte tick body. Interior sections are not decomposed in this clean slice.</summary>
    public readonly PayloadBuffer Payload;

    /// <summary>
    /// Reads the pinned world-entry seed from a raw 4/1 payload. Returns false if the frame is too
    /// short or is not the world-entry form.
    /// </summary>
    public static bool TryReadWorldEntrySeed(ReadOnlySpan<byte> payload, out SmsgGameStateTickSeed seed)
    {
        seed = default;

        if (payload.Length < WorldEntrySeedSize || payload[FormOffset] != WorldEntryForm)
        {
            return false;
        }

        seed = new SmsgGameStateTickSeed(
            payload[FormOffset],
            BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(ScenarioModeOffset, sizeof(int))),
            BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(SpawnXOffset, sizeof(float))),
            BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(SpawnZOffset, sizeof(float))));
        return true;
    }

    /// <summary>Opaque 9100-byte (0x238C) game-state tick body. spec: handlers.md §4/1.</summary>
    [InlineArray(WireSize)]
    public struct PayloadBuffer
    {
        private byte _element0;
    }
}

/// <summary>
/// The decoded 4/1 world-entry seed: form byte, scenario code, and X/Z spawn position. World Y is
/// not on the wire. spec: Docs/RE/specs/client_runtime.md §9.1/§9.4.
/// </summary>
public readonly record struct SmsgGameStateTickSeed(byte Form, int ScenarioMode, float SpawnX, float SpawnZ);