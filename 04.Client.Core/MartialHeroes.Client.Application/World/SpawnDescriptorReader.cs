using System.Buffers.Binary;
using MartialHeroes.Client.Application.Contracts.Hud;

namespace MartialHeroes.Client.Application.World;

/// <summary>
///     Decodes the application-relevant fields out of the opaque 880-byte (0x370) SpawnDescriptor record
///     carried by 5/3 SmsgCharSpawn. This is pure wire-field extraction at the network boundary — no
///     game-rule math — so it lives here, not in Domain or in Network.Protocol (which treats the
///     descriptor as an opaque blob). spec: Docs/RE/structs/actor.md (SpawnDescriptor section).
/// </summary>
/// <remarks>
///     <b>CAPTURE-UNVERIFIED offsets.</b> Every offset below is a static inference per the spec; field
///     layouts are hypotheses until a live capture confirms them. The <c>level</c> byte boundary and the
///     world-coordinate offset are explicitly flagged "open" by the spec (see remarks per field).
/// </remarks>
public readonly ref struct SpawnDescriptorReader
{
    // SpawnDescriptor field offsets, relative to the start of the 880-byte record.
    // spec: Docs/RE/structs/actor.md ("SpawnDescriptor - the 5/3 CharSpawn payload").
    private const int OffName = 0x00; // char[17], NUL-terminated. spec: actor.md +0x00
    private const int NameMaxBytes = 17; // up to 16 chars + NUL. spec: actor.md +0x00

    // --- Appearance-driver fields (char-select preview renders the REAL character). ---
    // spec: Docs/RE/packets/3-1_character_list.yaml (Sub-block 1 + APPEARANCE DRIVER notes)
    private const int
        OffAppearanceVariant = 0x2C; // u8, 'variant' arg of model formula. spec: 3-1_character_list.yaml +0x2C

    private const int
        OffFaceA = 0x2E; // u16 faceA (face index; nonzero => slot occupied). spec: frontend_scenes.md §3.2 +0x2E (u16, CODE-CONFIRMED)

    private const int
        OffInternalClass = 0x34; // u16 {1,2,3,4}, 'class' arg of model formula. spec: 3-1_character_list.yaml +0x34

    private const int
        OffEquipTable = 0x58; // 20 entries x 16 bytes; leading dword = part gid. spec: 3-1_character_list.yaml +0x58

    private const int EquipEntryStride = 16; // 16-byte stride per entry. spec: 3-1_character_list.yaml +0x58
    private const int EquipEntryCount = 20; // 20 entries total. spec: 3-1_character_list.yaml +0x58

    private const int OffLevel = 0x3A; // u16 (byte boundary unverified). spec: actor.md +0x3A
    private const int OffCurrentHp = 0x3C; // u32. spec: actor.md +0x3C
    private const int OffCurrentMp = 0x40; // u32. spec: actor.md +0x40
    private const int OffCurrentStamina = 0x44; // u32. spec: actor.md +0x44
    private const int OffWorldX = 0x4C; // f32 (confirmed float). spec: actor.md +0x4C
    private const int OffWorldZ = 0x50; // f32 (confirmed float). spec: actor.md +0x50
    private const int OffServerClass = 0x74; // u16. spec: actor.md +0x74

    /// <summary>The declared SpawnDescriptor size in bytes (0x370). spec: Docs/RE/structs/actor.md.</summary>
    public const int Size = 880;

    private readonly ReadOnlySpan<byte> _data;

    /// <summary>Wraps the 880-byte descriptor span. Throws if it is too short.</summary>
    public SpawnDescriptorReader(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < Size)
            throw new ArgumentOutOfRangeException(
                nameof(descriptor), descriptor.Length,
                $"SpawnDescriptor requires at least {Size} bytes.");

        _data = descriptor;
    }

    /// <summary>
    ///     Decodes the NUL-terminated actor name as CP949 / EUC-KR (NOT ASCII — earlier drafts said
    ///     ASCII/UTF-8, corrected here). Decoded through <see cref="Cp949Text.Decode" />, which owns the
    ///     single CodePagesEncodingProvider registration and trims at the first NUL. ASCII-only names
    ///     round-trip identically; Korean glyphs decode correctly. spec: Docs/RE/structs/actor.md
    ///     (SpawnDescriptor.name at +0x00 is CP949 / EUC-KR; "Decode with CP949, not UTF-8/ASCII").
    /// </summary>
    public string ReadName()
    {
        return Cp949Text.Decode(_data.Slice(OffName, NameMaxBytes));
    }

    /// <summary>Character level. spec: actor.md SpawnDescriptor.level (+0x3A, u16; boundary unverified).</summary>
    public ushort ReadLevel()
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(OffLevel, 2));
    }

    /// <summary>Current hit points. spec: actor.md SpawnDescriptor.current_hp (+0x3C, u32).</summary>
    public uint ReadCurrentHp()
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(OffCurrentHp, 4));
    }

    /// <summary>Current mana / ki points. spec: actor.md SpawnDescriptor.current_mp (+0x40, u32).</summary>
    public uint ReadCurrentMp()
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(OffCurrentMp, 4));
    }

    /// <summary>Current stamina. spec: actor.md SpawnDescriptor.current_stamina (+0x44, u32).</summary>
    public uint ReadCurrentStamina()
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(OffCurrentStamina, 4));
    }

    /// <summary>World X (confirmed float). spec: actor.md SpawnDescriptor.world_x (+0x4C, f32).</summary>
    public float ReadWorldX()
    {
        return BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(OffWorldX, 4));
    }

    /// <summary>World Z (confirmed float; Y forced 0 on spawn). spec: actor.md SpawnDescriptor.world_z (+0x50, f32).</summary>
    public float ReadWorldZ()
    {
        return BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(OffWorldZ, 4));
    }

    /// <summary>Server-assigned class id. spec: actor.md SpawnDescriptor.server_class (+0x74, u16).</summary>
    public ushort ReadServerClass()
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(OffServerClass, 2));
    }

    /// <summary>
    ///     Internal class word {1,2,3,4} — the <c>class</c> argument of the model-class formula
    ///     <c>5*(internal_class + 4*appearance_variant) - 24</c>; THE skeleton driver (distinct from
    ///     <see cref="ReadServerClass" />). spec: Docs/RE/packets/3-1_character_list.yaml (Sub-block 1
    ///     +0x34, u16; APPEARANCE DRIVER notes).
    /// </summary>
    public ushort ReadInternalClass()
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(OffInternalClass, 2));
    }

    /// <summary>
    ///     Body / gender appearance variant — the <c>variant</c> argument of the model-class formula.
    ///     spec: Docs/RE/packets/3-1_character_list.yaml (Sub-block 1 +0x2C, u8; APPEARANCE DRIVER notes).
    /// </summary>
    public byte ReadAppearanceVariant()
    {
        return _data[OffAppearanceVariant];
    }

    /// <summary>
    ///     faceA — the face index / appearance param A. Nonzero ⇒ the slot is occupied (the preview
    ///     occupancy / render-visibility test). NOTE: surfaced as the spec-pinned u16; the 3/1 yaml does
    ///     not itself list +0x2E, so the width and meaning are sourced from frontend_scenes.md §3.2 (which
    ///     pins faceA @ +0x2E as a CODE-CONFIRMED u16). spec: Docs/RE/specs/frontend_scenes.md §3.2 (+0x2E faceA, u16).
    /// </summary>
    public ushort ReadFaceA()
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(OffFaceA, 2));
    }

    /// <summary>
    ///     Reads the leading dword (worn-item / part gid) of one entry in the +0x58 equip table.
    ///     <paramref name="entryIndex" /> is the entry index = the renderer slot id (slot id == entry
    ///     index per the yaml's equip-table description); the renderer overlays slots {3,4,6,2,11,14}.
    ///     spec: Docs/RE/packets/3-1_character_list.yaml (Sub-block 1 +0x58; 20×16, leading dword = part gid);
    ///     Docs/RE/structs/spawn_descriptor.md (equip_ref_table View A, CONFIRMED).
    /// </summary>
    public uint ReadEquipPartGid(int entryIndex)
    {
        if ((uint)entryIndex >= EquipEntryCount)
            throw new ArgumentOutOfRangeException(
                nameof(entryIndex), entryIndex,
                $"Equip-table entry index must be in [0, {EquipEntryCount}).");

        var off = OffEquipTable + entryIndex * EquipEntryStride;
        return BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(off, 4));
    }

    /// <summary>
    ///     The renderer's visible-gear overlay slot ids, in overlay build order. The slot id is the
    ///     equip-table ENTRY INDEX. spec: Docs/RE/packets/3-1_character_list.yaml (overlays {3,4,6,2,11,14});
    ///     Docs/RE/specs/frontend_scenes.md §3.3.7.
    /// </summary>
    public static ReadOnlySpan<int> VisibleGearSlots => [3, 4, 6, 2, 11, 14];

    /// <summary>
    ///     Copies the six visible-gear part gids (overlay slots {3,4,6,2,11,14}, in that build order) from
    ///     the +0x58 equip table into <paramref name="destination" /> (length ≥ 6). Zero-alloc: writes into
    ///     the caller's span. Returns the number written (always 6). spec:
    ///     Docs/RE/packets/3-1_character_list.yaml (Sub-block 1 +0x58; APPEARANCE DRIVER overlays {3,4,6,2,11,14}).
    /// </summary>
    public int ReadVisibleGearGids(Span<uint> destination)
    {
        var slots = VisibleGearSlots;
        if (destination.Length < slots.Length)
            throw new ArgumentException(
                $"Destination must hold at least {slots.Length} gids.", nameof(destination));

        for (var i = 0; i < slots.Length; i++)
            destination[i] = ReadEquipPartGid(slots[i]);

        return slots.Length;
    }
}