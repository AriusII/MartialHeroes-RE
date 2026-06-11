using System.Buffers.Binary;
using System.Text;

namespace MartialHeroes.Client.Application.World;

/// <summary>
/// Decodes the application-relevant fields out of the opaque 880-byte (0x370) SpawnDescriptor record
/// carried by 5/3 SmsgCharSpawn. This is pure wire-field extraction at the network boundary — no
/// game-rule math — so it lives here, not in Domain or in Network.Protocol (which treats the
/// descriptor as an opaque blob). spec: Docs/RE/structs/actor.md (SpawnDescriptor section).
/// </summary>
/// <remarks>
/// <b>CAPTURE-UNVERIFIED offsets.</b> Every offset below is a static inference per the spec; field
/// layouts are hypotheses until a live capture confirms them. The <c>level</c> byte boundary and the
/// world-coordinate offset are explicitly flagged "open" by the spec (see remarks per field).
/// </remarks>
public readonly ref struct SpawnDescriptorReader
{
    // SpawnDescriptor field offsets, relative to the start of the 880-byte record.
    // spec: Docs/RE/structs/actor.md ("SpawnDescriptor - the 5/3 CharSpawn payload").
    private const int OffName = 0x00;            // char[17], NUL-terminated. spec: actor.md +0x00
    private const int NameMaxBytes = 17;         // up to 16 chars + NUL. spec: actor.md +0x00
    private const int OffLevel = 0x3A;           // u16 (byte boundary unverified). spec: actor.md +0x3A
    private const int OffCurrentHp = 0x3C;       // u32. spec: actor.md +0x3C
    private const int OffCurrentMp = 0x40;       // u32. spec: actor.md +0x40
    private const int OffCurrentStamina = 0x44;  // u32. spec: actor.md +0x44
    private const int OffWorldX = 0x4C;          // f32 (confirmed float). spec: actor.md +0x4C
    private const int OffWorldZ = 0x50;          // f32 (confirmed float). spec: actor.md +0x50
    private const int OffServerClass = 0x74;     // u16. spec: actor.md +0x74

    /// <summary>The declared SpawnDescriptor size in bytes (0x370). spec: Docs/RE/structs/actor.md.</summary>
    public const int Size = 880;

    private readonly ReadOnlySpan<byte> _data;

    /// <summary>Wraps the 880-byte descriptor span. Throws if it is too short.</summary>
    public SpawnDescriptorReader(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < Size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptor), descriptor.Length,
                $"SpawnDescriptor requires at least {Size} bytes.");
        }

        _data = descriptor;
    }

    /// <summary>Decodes the NUL-terminated name (ASCII). spec: actor.md SpawnDescriptor.name (+0x00, char[17]).</summary>
    public string ReadName()
    {
        ReadOnlySpan<byte> nameBytes = _data.Slice(OffName, NameMaxBytes);
        int nul = nameBytes.IndexOf((byte)0);
        if (nul >= 0)
        {
            nameBytes = nameBytes[..nul];
        }

        return nameBytes.IsEmpty ? string.Empty : Encoding.ASCII.GetString(nameBytes);
    }

    /// <summary>Character level. spec: actor.md SpawnDescriptor.level (+0x3A, u16; boundary unverified).</summary>
    public ushort ReadLevel() => BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(OffLevel, 2));

    /// <summary>Current hit points. spec: actor.md SpawnDescriptor.current_hp (+0x3C, u32).</summary>
    public uint ReadCurrentHp() => BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(OffCurrentHp, 4));

    /// <summary>Current mana / ki points. spec: actor.md SpawnDescriptor.current_mp (+0x40, u32).</summary>
    public uint ReadCurrentMp() => BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(OffCurrentMp, 4));

    /// <summary>Current stamina. spec: actor.md SpawnDescriptor.current_stamina (+0x44, u32).</summary>
    public uint ReadCurrentStamina() =>
        BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(OffCurrentStamina, 4));

    /// <summary>World X (confirmed float). spec: actor.md SpawnDescriptor.world_x (+0x4C, f32).</summary>
    public float ReadWorldX() => BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(OffWorldX, 4));

    /// <summary>World Z (confirmed float; Y forced 0 on spawn). spec: actor.md SpawnDescriptor.world_z (+0x50, f32).</summary>
    public float ReadWorldZ() => BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(OffWorldZ, 4));

    /// <summary>Server-assigned class id. spec: actor.md SpawnDescriptor.server_class (+0x74, u16).</summary>
    public ushort ReadServerClass() =>
        BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(OffServerClass, 2));
}
