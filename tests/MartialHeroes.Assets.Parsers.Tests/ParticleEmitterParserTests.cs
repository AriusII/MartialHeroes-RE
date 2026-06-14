using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="ParticleEmitterParser"/>.
/// All fixtures are built in-memory from scratch — no real game files required.
///
/// Format under test: <c>particleEmitter.eff</c> at VFS path
/// <c>data/effect/particle/particleEmitter.eff</c>.
///
/// Layout (all CONFIRMED from loader analysis):
///   No file header, no magic, no count prefix.
///   Entry = 28-byte header + num_frames × 52-byte sub-record + 64-byte texture name.
///   Loop terminates when num_frames == 0 (sentinel) or &lt; 28 bytes remain.
///   entry_id is in the ≥ 10000 space (first real entry = 10001).
///   Runtime selection: raw entry_id equality — NO −10000 subtraction.
///
/// spec: Docs/RE/formats/effects.md §E.2 File layout: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §E.4 Runtime selection — raw-id equality: CONFIRMED.
/// </summary>
public sealed class ParticleEmitterParserTests
{
    // ── Binary fixture helpers ──────────────────────────────────────────────

    private static void WriteU32LE(byte[] buf, int off, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off, 4), v);

    private static void WriteF32LE(byte[] buf, int off, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(off, 4), v);

    /// <summary>
    /// Builds a single 28-byte entry header.
    /// spec: Docs/RE/formats/effects.md §E.2.1 Entry header (28 bytes / 0x1C): CONFIRMED.
    /// </summary>
    private static byte[] BuildEntryHeader(
        uint entryId,
        uint numFrames,
        float spriteSizeX = 1.0f,
        float spriteSizeY = 2.0f,
        uint maxParticles = 100,
        uint rawTexHandle = 0,
        uint rawSubrecPtr = 0)
    {
        byte[] hdr = new byte[28];
        // entry_id u32 LE @ 0x00. spec: §E.2.1 — CONFIRMED.
        WriteU32LE(hdr, 0x00, entryId);
        // num_frames u32 LE @ 0x04. spec: §E.2.1 — CONFIRMED.
        WriteU32LE(hdr, 0x04, numFrames);
        // sprite_size_x f32 LE @ 0x08. spec: §E.2.1 — HIGH.
        WriteF32LE(hdr, 0x08, spriteSizeX);
        // sprite_size_y f32 LE @ 0x0C. spec: §E.2.1 — HIGH.
        WriteF32LE(hdr, 0x0C, spriteSizeY);
        // max_particles u32 LE @ 0x10. spec: §E.2.1 — HIGH.
        WriteU32LE(hdr, 0x10, maxParticles);
        // tex_handle_slot u32 LE @ 0x14. MEDIUM (overwritten at load).
        WriteU32LE(hdr, 0x14, rawTexHandle);
        // subrecord_array_ptr u32 LE @ 0x18. MEDIUM (overwritten at load).
        WriteU32LE(hdr, 0x18, rawSubrecPtr);
        return hdr;
    }

    /// <summary>
    /// Builds one 52-byte sub-record with a known colour quad and all-zero lead/tail.
    /// spec: Docs/RE/formats/effects.md §E.2.2 Sub-record (52 bytes / 0x34): CONFIRMED stride.
    /// </summary>
    private static byte[] BuildSubRecord(byte r = 0, byte g = 0, byte b = 0, byte a = 0xFF)
    {
        byte[] sr = new byte[52];
        // colour quad @ +0x08..+0x0B. spec: §E.2.2 — MEDIUM.
        sr[0x08] = r;
        sr[0x09] = g;
        sr[0x0A] = b;
        sr[0x0B] = a;
        return sr;
    }

    /// <summary>
    /// Builds a 64-byte NUL-padded ASCII texture name.
    /// spec: Docs/RE/formats/effects.md §E.2.3 — texture_name char[64]: CONFIRMED.
    /// </summary>
    private static byte[] BuildTextureName(string name)
    {
        byte[] field = new byte[64]; // NUL-padded
        byte[] encoded = Encoding.ASCII.GetBytes(name);
        int copyLen = Math.Min(encoded.Length, 63); // leave one NUL terminator
        Array.Copy(encoded, 0, field, 0, copyLen);
        return field;
    }

    /// <summary>
    /// Builds a terminator entry (num_frames == 0, 28 bytes of zeros except entry_id and num_frames).
    /// spec: Docs/RE/formats/effects.md §E.2 — "read loop stops when num_frames == 0": CONFIRMED.
    /// </summary>
    private static byte[] BuildTerminator(uint entryId = 0)
        => BuildEntryHeader(entryId, numFrames: 0);

    /// <summary>
    /// Concatenates byte arrays into a single buffer.
    /// </summary>
    private static byte[] Concat(params byte[][] parts)
    {
        int total = 0;
        foreach (var p in parts) total += p.Length;
        byte[] result = new byte[total];
        int off = 0;
        foreach (var p in parts)
        {
            p.CopyTo(result, off);
            off += p.Length;
        }

        return result;
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyBuffer_ReturnsEmptyTable()
    {
        // An empty file has no entries (< 28 bytes at start = loop body never executed).
        // spec: Docs/RE/formats/effects.md §E.2 — "read loop stops when < 28 bytes remain": CONFIRMED.
        ParticleEmitterTable table = ParticleEmitterParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(table.Entries);
    }

    [Fact]
    public void Parse_TerminatorOnly_ReturnsEmptyTable()
    {
        // A file containing only a terminator entry (num_frames == 0) yields zero entries.
        // spec: Docs/RE/formats/effects.md §E.2 — "read loop stops when num_frames == 0": CONFIRMED.
        byte[] buf = BuildTerminator(entryId: 99999u);
        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Empty(table.Entries);
    }

    [Fact]
    public void Parse_SingleEntry_OneSubRecord_EntryIdDecoded()
    {
        // spec: Docs/RE/formats/effects.md §E.2.1 — entry_id u32 LE @ 0x00: CONFIRMED.
        // entry_id is in the ≥ 10000 space (first observed entry = 10001).
        // spec: Docs/RE/formats/effects.md §E.2.1 — "GPU-particle id in the ≥ 10000 space": CONFIRMED.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10001u, numFrames: 1, spriteSizeX: 3.5f, spriteSizeY: 4.0f, maxParticles: 50),
            BuildSubRecord(r: 100, g: 150, b: 200, a: 0xFF),
            BuildTextureName("particle_tex_01"),
            BuildTerminator()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(table.Entries);
        ParticleEmitterEntry entry = table.Entries[0];
        Assert.Equal(10001u, entry.EntryId);
    }

    [Fact]
    public void Parse_SingleEntry_SpriteSizeDecoded()
    {
        // spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_x f32 LE @ 0x08: HIGH.
        // spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_y f32 LE @ 0x0C: HIGH.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10002u, numFrames: 1, spriteSizeX: 1.25f, spriteSizeY: 2.75f, maxParticles: 200),
            BuildSubRecord(),
            BuildTextureName("tex"),
            BuildTerminator()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        ParticleEmitterEntry entry = table.Entries[0];
        Assert.Equal(1.25f, entry.SpriteSizeX, precision: 5);
        Assert.Equal(2.75f, entry.SpriteSizeY, precision: 5);
    }

    [Fact]
    public void Parse_SingleEntry_MaxParticlesDecoded()
    {
        // spec: Docs/RE/formats/effects.md §E.2.1 — max_particles u32 LE @ 0x10: HIGH.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10003u, numFrames: 1, maxParticles: 512),
            BuildSubRecord(),
            BuildTextureName("tex"),
            BuildTerminator()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(512u, table.Entries[0].MaxParticles);
    }

    [Fact]
    public void Parse_SingleEntry_NumFrames_SubRecordCount()
    {
        // num_frames drives the sub-record count.
        // spec: Docs/RE/formats/effects.md §E.2.1 — num_frames u32 LE @ 0x04: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §E.2.2 — num_frames × 52-byte sub-records: CONFIRMED.
        const uint frames = 3;
        var subs = new List<byte[]>();
        for (int i = 0; i < frames; i++)
            subs.Add(BuildSubRecord(r: (byte)i, g: 0, b: 0, a: 0xFF));

        byte[] buf = Concat(
            new[] { BuildEntryHeader(entryId: 10010u, numFrames: frames, maxParticles: 10) }
                .Concat(subs)
                .Append(BuildTextureName("multi_sub"))
                .Append(BuildTerminator())
                .ToArray()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(table.Entries);
        Assert.Equal(frames, (uint)table.Entries[0].SubRecords.Length);
    }

    [Fact]
    public void Parse_SubRecord_ColourQuadDecoded()
    {
        // The colour quad at sub-record +0x08..+0x0B is the only field with a confirmed read-site.
        // spec: Docs/RE/formats/effects.md §E.2.2 — color_r/g/b/a @ +0x08..+0x0B: MEDIUM.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10020u, numFrames: 1, maxParticles: 25),
            BuildSubRecord(r: 255, g: 128, b: 64, a: 0xFF),
            BuildTextureName("colour_test"),
            BuildTerminator()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        ParticleSubRecord sr = table.Entries[0].SubRecords[0];
        Assert.Equal(255, sr.ColorR);
        Assert.Equal(128, sr.ColorG);
        Assert.Equal(64, sr.ColorB);
        Assert.Equal(0xFF, sr.ColorA);
    }

    [Fact]
    public void Parse_SubRecord_UnresolvedRegions_HaveCorrectSize()
    {
        // The unresolved lead (8 bytes) and tail (40 bytes) must be stored faithfully.
        // spec: Docs/RE/formats/effects.md §E.2.2 — _unresolved_lead_ (8 B) + _unresolved_tail_ (40 B): UNRESOLVED.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10021u, numFrames: 1, maxParticles: 10),
            BuildSubRecord(),
            BuildTextureName("sizes_test"),
            BuildTerminator()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        ParticleSubRecord sr = table.Entries[0].SubRecords[0];
        Assert.Equal(8, sr.UnresolvedLead.Length);
        Assert.Equal(40, sr.UnresolvedTail.Length);
    }

    [Fact]
    public void Parse_TextureName_Decoded()
    {
        // The trailing 64-byte NUL-padded texture name is the authoritative texture source.
        // spec: Docs/RE/formats/effects.md §E.2.3 — texture_name char[64]: CONFIRMED.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10030u, numFrames: 1, maxParticles: 50),
            BuildSubRecord(),
            BuildTextureName("fire_effect_01"),
            BuildTerminator()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal("fire_effect_01", table.Entries[0].TextureName);
    }

    [Fact]
    public void Parse_TwoEntries_BothDecoded()
    {
        // Multiple entries before terminator.
        // spec: Docs/RE/formats/effects.md §E.2 — "File = Entry[0] Entry[1] ... Entry[k]": CONFIRMED.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10001u, numFrames: 1, maxParticles: 100),
            BuildSubRecord(r: 1),
            BuildTextureName("tex_a"),
            BuildEntryHeader(entryId: 10002u, numFrames: 2, maxParticles: 200),
            BuildSubRecord(r: 2),
            BuildSubRecord(r: 3),
            BuildTextureName("tex_b"),
            BuildTerminator()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, table.Entries.Length);
        Assert.Equal(10001u, table.Entries[0].EntryId);
        Assert.Equal(10002u, table.Entries[1].EntryId);
        Assert.Equal(2, table.Entries[1].SubRecords.Length);
        Assert.Equal("tex_b", table.Entries[1].TextureName);
    }

    [Fact]
    public void Parse_EofWithoutTerminator_ReadsAllCompleteEntries()
    {
        // If the file ends without a num_frames==0 terminator (< 28 bytes at tail),
        // all fully-read entries are returned.
        // spec: Docs/RE/formats/effects.md §E.2 — "fewer than 28 bytes remain = tail guard": CONFIRMED.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10005u, numFrames: 1, maxParticles: 75),
            BuildSubRecord(),
            BuildTextureName("sole_entry")
            // No terminator — EOF after texture name.
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(table.Entries);
        Assert.Equal(10005u, table.Entries[0].EntryId);
    }

    [Fact]
    public void TryGetById_HitAndMiss()
    {
        // Raw entry_id equality — NO −10000 subtraction.
        // spec: Docs/RE/formats/effects.md §E.4 — raw-id equality: CONFIRMED.
        // A map miss produces null (effect renders nothing).
        // spec: Docs/RE/formats/effects.md §E.4 — "miss = no particle system": CONFIRMED.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10007u, numFrames: 1, maxParticles: 30),
            BuildSubRecord(),
            BuildTextureName("lookup_test"),
            BuildTerminator()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        // Hit: raw id 10007 matches exactly.
        ParticleEmitterEntry? hit = table.TryGetById(10007u);
        Assert.NotNull(hit);
        Assert.Equal(10007u, hit!.EntryId);

        // Miss: 10007 − 10000 = 7 is NOT the key (no subtraction).
        Assert.Null(table.TryGetById(7u));

        // Miss: unknown id.
        Assert.Null(table.TryGetById(99999u));
    }

    [Fact]
    public void Parse_TruncatedSubRecordBlock_ThrowsInvalidDataException()
    {
        // If the sub-record block overflows the buffer, throw InvalidDataException.
        // spec: Docs/RE/formats/effects.md §E.2 — bounds validation: per parser spec mandate.
        // Build header claiming 5 sub-records but provide only 1 sub-record + texture name.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10099u, numFrames: 5, maxParticles: 10), // claims 5 sub-records
            BuildSubRecord(), // only 1 provided
            BuildTextureName("truncated")
        );

        Assert.Throws<InvalidDataException>(() =>
            ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void Parse_RawTexHandleSlot_Preserved()
    {
        // The on-disk tex_handle_slot dword at header+0x14 is stored faithfully even though
        // it is overwritten at runtime.
        // spec: Docs/RE/formats/effects.md §E.2.1 — tex_handle_slot u32 LE @ 0x14: MEDIUM (disk value unused).
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10040u, numFrames: 1, maxParticles: 10,
                rawTexHandle: 0xDEADBEEFu, rawSubrecPtr: 0xCAFEBABEu),
            BuildSubRecord(),
            BuildTextureName("raw_test"),
            BuildTerminator()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(0xDEADBEEFu, table.Entries[0].RawTexHandleSlot);
        Assert.Equal(0xCAFEBABEu, table.Entries[0].RawSubrecordArrayPtr);
    }

    [Fact]
    public void Parse_ReadOnlySpan_OverloadWorks()
    {
        // Verify the ReadOnlySpan<byte> overload produces the same result.
        byte[] buf = Concat(
            BuildEntryHeader(entryId: 10050u, numFrames: 1, maxParticles: 10),
            BuildSubRecord(),
            BuildTextureName("span_test"),
            BuildTerminator()
        );

        ParticleEmitterTable table = ParticleEmitterParser.Parse(buf.AsSpan());

        Assert.Single(table.Entries);
        Assert.Equal(10050u, table.Entries[0].EntryId);
    }
}