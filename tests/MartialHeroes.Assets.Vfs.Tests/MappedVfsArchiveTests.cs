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
}