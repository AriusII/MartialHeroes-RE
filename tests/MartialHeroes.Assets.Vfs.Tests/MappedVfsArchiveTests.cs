using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Vfs;
using Xunit;

namespace MartialHeroes.Assets.Vfs.Tests;

/// <summary>
/// Round-trip tests for <see cref="MappedVfsArchive"/> against synthetically-built archives.
/// No real game files are used or required.
/// All format decisions cite Docs/RE/formats/pak.md.
/// </summary>
public sealed class MappedVfsArchiveTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    // -----------------------------------------------------------------------
    // Directory parsing
    // -----------------------------------------------------------------------

    [Fact]
    public void EntryCount_MatchesNumberOfWrittenEntries()
    {
        // spec: Docs/RE/formats/pak.md — entry_count @ +12 drives directory size. CONFIRMED.
        using var archive = SyntheticArchive.Build(
            ("textures/stone.dds", Ascii("STONE")),
            ("textures/water.dds", Ascii("WATER")),
            ("models/hero.msh", Ascii("HERO_MESH")));

        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);
        Assert.Equal(3, vfs.EntryCount);
    }

    [Fact]
    public void EmptyArchive_EntryCountIsZero()
    {
        using var archive = SyntheticArchive.Build();
        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);
        Assert.Equal(0, vfs.EntryCount);
    }

    // -----------------------------------------------------------------------
    // GetFileContent — exact byte slices
    // -----------------------------------------------------------------------

    [Fact]
    public void GetFileContent_ReturnsExactBytes_ForEachEntry()
    {
        // Verify the dataOffset + dataSize fields produce the correct slice.
        // spec: Docs/RE/formats/pak.md — dataOffset @ +104 i64 LE, dataSize @ +112 i64 LE. CONFIRMED.
        byte[] alpha = Ascii("ALPHA_DATA");
        byte[] beta = Ascii("BETA_PAYLOAD_LONGER");
        byte[] gamma = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        using var archive = SyntheticArchive.Build(
            ("files/alpha.bin", alpha),
            ("files/beta.bin", beta),
            ("files/gamma.bin", gamma));

        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        Assert.Equal(alpha, vfs.GetFileContent("files/alpha.bin").ToArray());
        Assert.Equal(beta, vfs.GetFileContent("files/beta.bin").ToArray());
        Assert.Equal(gamma, vfs.GetFileContent("files/gamma.bin").ToArray());
    }

    [Fact]
    public void GetFileContent_EmptyEntry_ReturnsEmptyMemory()
    {
        using var archive = SyntheticArchive.Build(
            ("empty.dat", Array.Empty<byte>()));

        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        var result = vfs.GetFileContent("empty.dat");
        Assert.Equal(0, result.Length);
    }

    // -----------------------------------------------------------------------
    // Case-insensitive lookup
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("textures/Logo.TGA")] // mixed case
    [InlineData("TEXTURES/LOGO.TGA")] // all upper
    [InlineData("textures/logo.tga")] // canonical lower
    [InlineData("TeXtUrEs/lOgO.TgA")] // random case
    public void GetFileContent_IsCaseInsensitive(string requestedPath)
    {
        // spec: Docs/RE/formats/pak.md §"Lookup algorithm" step 1: "lowercase the
        // requested virtual path". CONFIRMED.
        byte[] expected = Ascii("LOGO_BYTES");
        using var archive = SyntheticArchive.Build(
            ("textures/logo.tga", expected));

        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        var result = vfs.GetFileContent(requestedPath);
        Assert.Equal(expected, result.ToArray());
    }

    // -----------------------------------------------------------------------
    // Contains
    // -----------------------------------------------------------------------

    [Fact]
    public void Contains_ReturnsTrueForExistingEntry()
    {
        using var archive = SyntheticArchive.Build(
            ("sound/bgm.ogg", Ascii("OGG")));
        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        Assert.True(vfs.Contains("sound/bgm.ogg"));
        Assert.True(vfs.Contains("SOUND/BGM.OGG")); // case-insensitive
    }

    [Fact]
    public void Contains_ReturnsFalseForMissingEntry()
    {
        using var archive = SyntheticArchive.Build(
            ("sound/bgm.ogg", Ascii("OGG")));
        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        Assert.False(vfs.Contains("sound/missing.ogg"));
    }

    // -----------------------------------------------------------------------
    // Error handling
    // -----------------------------------------------------------------------

    [Fact]
    public void GetFileContent_ThrowsFileNotFound_ForMissingEntry()
    {
        using var archive = SyntheticArchive.Build(
            ("a/b.dat", Ascii("X")));
        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        Assert.Throws<FileNotFoundException>(() =>
            vfs.GetFileContent("a/c.dat"));
    }

    [Fact]
    public void GetFileContent_ThrowsObjectDisposed_AfterDispose()
    {
        using var archive = SyntheticArchive.Build(
            ("test.bin", Ascii("DATA")));

        var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);
        vfs.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            vfs.GetFileContent("test.bin"));
    }

    // -----------------------------------------------------------------------
    // Sorted directory / GetEntries
    // -----------------------------------------------------------------------

    [Fact]
    public void Entries_AreSortedAscendingByName()
    {
        // spec: Docs/RE/formats/pak.md — "TOC must be sorted ascending by lowercased name".
        // CONFIRMED.  We provide entries out of order and verify the parsed TOC is sorted.
        using var archive = SyntheticArchive.Build(
            ("z_last.dat", Ascii("Z")),
            ("a_first.dat", Ascii("A")),
            ("m_middle.dat", Ascii("M")));

        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        var entries = vfs.GetEntries().ToArray();
        for (int i = 1; i < entries.Length; i++)
            Assert.True(
                string.CompareOrdinal(entries[i - 1].Name, entries[i].Name) < 0,
                $"Entry [{i - 1}] \"{entries[i - 1].Name}\" is not before [{i}] \"{entries[i].Name}\"");
    }

    // -----------------------------------------------------------------------
    // Zero-copy slice identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GetFileContent_ReturnedMemory_IsNotACopy()
    {
        // The ReadOnlyMemory<byte> should NOT be backed by a managed byte[].
        // It must be backed by the MemoryManager (mapped memory).
        // We verify this by checking that MemoryMarshal.TryGetArray returns false.
        byte[] payload = Ascii("PAYLOAD");
        using var archive = SyntheticArchive.Build(("asset.dat", payload));
        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        var memory = vfs.GetFileContent("asset.dat");

        bool isArrayBacked =
            System.Runtime.InteropServices.MemoryMarshal.TryGetArray<byte>(memory, out _);
        Assert.False(isArrayBacked,
            "GetFileContent must return memory backed by the memory-mapped view, not a byte[].");
    }

    // -----------------------------------------------------------------------
    // Concurrent read safety
    // -----------------------------------------------------------------------

    [Fact]
    public void ConcurrentReads_FromSameArchive_Succeed()
    {
        // Multiple threads must be able to read different entries simultaneously.
        // spec: thread safety is guaranteed by the read-only mapped view (no shared mutable state
        // per-read after construction).
        byte[] data1 = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        byte[] data2 = Enumerable.Range(255, 256).Select(i => (byte)(i % 256)).ToArray();

        using var archive = SyntheticArchive.Build(
            ("concurrent/a.bin", data1),
            ("concurrent/b.bin", data2));

        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        const int iterations = 200;
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, iterations, i =>
        {
            try
            {
                string path = (i % 2 == 0) ? "concurrent/a.bin" : "concurrent/b.bin";
                byte[] expected = (i % 2 == 0) ? data1 : data2;
                var result = vfs.GetFileContent(path);
                Assert.Equal(expected, result.ToArray());
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        Assert.Empty(errors);
    }

    // -----------------------------------------------------------------------
    // Header-echo / real-magic / FILETIME fidelity (spec: Docs/RE/formats/pak.md)
    // -----------------------------------------------------------------------

    [Fact]
    public void Open_ParsesRealVfs001Magic_AndIgnoresPopulatedFiletime()
    {
        // The fixture now writes the real "VFS001" magic at offset 0 and non-zero FILETIME-shaped bytes
        // at +120/+128/+136. The parser must read-and-discard both (asserting neither) and still resolve
        // entry_count from +12 and each entry's payload correctly.
        // spec: Docs/RE/formats/pak.md §Header (magic read-and-discarded), §TOC record (FILETIME never read).
        byte[] a = Ascii("FIRST");
        byte[] b = Ascii("SECOND_PAYLOAD");
        using var archive = SyntheticArchive.Build(
            ("alpha.dat", a),
            ("beta.dat", b));

        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        Assert.Equal(2, vfs.EntryCount);
        Assert.Equal(a, vfs.GetFileContent("alpha.dat").ToArray());
        Assert.Equal(b, vfs.GetFileContent("beta.dat").ToArray());
    }

    [Fact]
    public void GetFileContent_ResolvesPayload_WhenBlobLeadsWith24ByteHeaderEcho()
    {
        // data.vfs leads with a verbatim 24-byte header echo, so entry 0's dataOffset is 24, not 0.
        // GetFileContent must honour the recorded dataOffset and never assume payloads begin at byte 0.
        // spec: Docs/RE/formats/pak.md §"data.vfs leads with a verbatim 24-byte header echo".
        byte[] payload = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 };
        using var archive = SyntheticArchive.Build(("only.bin", payload));
        using var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        // The single entry must resolve to its real bytes, proving the >= 24 offset is honoured.
        Assert.Equal(payload, vfs.GetFileContent("only.bin").ToArray());

        // And the recorded offset must be exactly 24 (the header echo length).
        var entries = vfs.GetEntries().ToArray();
        Assert.Single(entries);
        Assert.Equal(24, entries[0].DataOffset);
    }

    // -----------------------------------------------------------------------
    // Failure / edge branches (spec: Docs/RE/formats/pak.md)
    // -----------------------------------------------------------------------

    [Fact]
    public void GetFileContent_ThrowsInvalidData_WhenDataSizeHighDwordNonZero()
    {
        // A non-zero high dword on dataSize causes the original read to fail; the port surfaces it as
        // InvalidDataException rather than attempting an oversized read.
        // spec: Docs/RE/formats/pak.md §TOC record — "a non-zero high dword causes the read to fail".
        byte[] header = SyntheticArchive.MakeHeader(entryCount: 1);

        // Name "big.dat", dataOffset 24 (post header echo), dataSize with the high dword set.
        byte[] nameField = new byte[100];
        Encoding.ASCII.GetBytes("big.dat").CopyTo(nameField, 0);
        const long highDwordSet = 0x0000_0001_0000_0004L; // high dword = 1, low dword = 4
        byte[] record = SyntheticArchive.MakeRecord(nameField, dataOffset: 24, dataSize: highDwordSet);

        byte[] inf = [.. header, .. record];
        // data.vfs: 24-byte echo + a few payload bytes so the file is non-empty.
        byte[] vfs = new byte[24 + 8];
        header.CopyTo(vfs, 0);

        using var archive = SyntheticArchive.BuildRaw(inf, vfs);
        using var v = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        Assert.Throws<InvalidDataException>(() => v.GetFileContent("big.dat"));
    }

    [Fact]
    public void GetFileContent_ThrowsInvalidData_WhenEntryClaimsDataButBlobIsEmpty()
    {
        // An entry with dataSize > 0 against a zero-byte data.vfs is a corrupt archive: there is no
        // mapped view to slice. GetFileContent must surface a clear InvalidDataException.
        // spec: Docs/RE/formats/pak.md §Two-file scheme (payload lives in data.vfs).
        byte[] header = SyntheticArchive.MakeHeader(entryCount: 1);

        byte[] nameField = new byte[100];
        Encoding.ASCII.GetBytes("ghost.dat").CopyTo(nameField, 0);
        byte[] record = SyntheticArchive.MakeRecord(nameField, dataOffset: 24, dataSize: 16);

        byte[] inf = [.. header, .. record];
        byte[] vfs = []; // zero-byte blob — no mapping is created

        using var archive = SyntheticArchive.BuildRaw(inf, vfs);
        using var v = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        Assert.Throws<InvalidDataException>(() => v.GetFileContent("ghost.dat"));
    }

    [Fact]
    public void Open_NameSpanningAll100Bytes_WithNoNullTerminator_UsesFullWidth()
    {
        // A name field that fills all 100 bytes with no NUL must be decoded at the full 100-byte width.
        // spec: Docs/RE/formats/pak.md §TOC record — name @ +0, char[100], lookup stops at first null.
        byte[] nameField = new byte[100];
        // Fill all 100 bytes with lower-case 'a' (no null terminator anywhere in the field).
        Array.Fill(nameField, (byte)'a');
        string expectedName = new('a', 100);

        byte[] header = SyntheticArchive.MakeHeader(entryCount: 1);
        byte[] record = SyntheticArchive.MakeRecord(nameField, dataOffset: 24, dataSize: 0);

        byte[] inf = [.. header, .. record];
        byte[] vfs = new byte[24]; // header echo only; the entry is zero-length
        header.CopyTo(vfs, 0);

        using var archive = SyntheticArchive.BuildRaw(inf, vfs);
        using var v = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        var entries = v.GetEntries().ToArray();
        Assert.Single(entries);
        Assert.Equal(100, entries[0].Name.Length);
        Assert.Equal(expectedName, entries[0].Name);
    }

    [Fact]
    public void Open_TreatsHighBitSetEntryCount_AsEmptyDirectory()
    {
        // The original mount routine treats entry_count as a signed i32 with a "count <= 0" guard. A
        // header whose entry_count has the high bit set is therefore a non-positive count → empty
        // directory, NOT an overflow during the 144 × count allocation.
        // spec: Docs/RE/specs/vfs_overview.md §Mount — "signed i32 ... count <= 0 guard".
        byte[] header = SyntheticArchive.MakeHeader(entryCount: 0);
        // Overwrite entry_count @ +12 with a high-bit-set value (negative as i32).
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 0x8000_0001u);

        // No TOC records follow (the parser must not attempt to read any).
        byte[] vfs = []; // zero-byte blob

        using var archive = SyntheticArchive.BuildRaw(header, vfs);
        using var v = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);

        Assert.Equal(0, v.EntryCount);
    }

    // -----------------------------------------------------------------------
    // Pointer-lifetime: many reads then clean dispose (regression for the AcquirePointer leak)
    // -----------------------------------------------------------------------

    [Fact]
    public void GetFileContent_ManyReads_ThenDispose_DoesNotLeakViewPointer()
    {
        // Regression for the historic leak where each GetFileContent acquired a fresh view-handle
        // pointer that was never released. The archive now acquires once at open and releases once at
        // dispose, so thousands of reads followed by a single Dispose must complete cleanly and a read
        // after dispose must throw ObjectDisposedException (the handle is no longer pinned).
        // spec: Docs/RE/formats/pak.md PORT CHOICE note (single long-lived mapped view).
        byte[] payload = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray();
        using var archive = SyntheticArchive.Build(("stream/cell.ted", payload));

        var vfs = MappedVfsArchive.Open(archive.InfPath, archive.VfsPath);
        for (int i = 0; i < 5000; i++)
        {
            var mem = vfs.GetFileContent("stream/cell.ted");
            Assert.Equal(512, mem.Length);
            Assert.Equal(payload[0], mem.Span[0]);
            Assert.Equal(payload[511], mem.Span[511]);
        }

        vfs.Dispose();

        // Idempotent dispose must not throw.
        vfs.Dispose();

        Assert.Throws<ObjectDisposedException>(() => vfs.GetFileContent("stream/cell.ted"));
    }
}