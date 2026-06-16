using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="BindlistParser"/>.
/// All buffers are built in-memory; no real game file is required.
/// spec: Docs/RE/formats/bindlist.md
/// </summary>
public sealed class BindlistParserTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Encodes a sequence of filenames as a CRLF-delimited byte buffer,
    /// optionally without a trailing CRLF on the last entry (matching the real file).
    /// spec: Docs/RE/formats/bindlist.md §File structure — "CRLF (\r\n), final line has no
    ///   trailing CRLF": CONFIRMED.
    /// </summary>
    private static ReadOnlyMemory<byte> BuildBindlist(
        IEnumerable<string> entries,
        bool trailingCrlf = false)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        var list = entries.ToArray();
        using var ms = new System.IO.MemoryStream();
        for (int i = 0; i < list.Length; i++)
        {
            byte[] nameBytes = cp949.GetBytes(list[i]);
            ms.Write(nameBytes);
            if (i < list.Length - 1 || trailingCrlf)
            {
                ms.WriteByte((byte)'\r');
                ms.WriteByte((byte)'\n');
            }
        }

        return ms.ToArray();
    }

    // =========================================================================
    // 1. Basic count and ordered enumeration
    // =========================================================================

    /// <summary>
    /// Verifies that the correct count of entries is decoded.
    /// spec: Docs/RE/formats/bindlist.md §Entry count — "Total entries: 349": CONFIRMED.
    /// </summary>
    [Fact]
    public void Parse_ThreeEntries_CountIsThree()
    {
        // spec: Docs/RE/formats/bindlist.md §File structure — single column, no header: CONFIRMED.
        var data = BuildBindlist(["g1.bnd", "g5.bnd", "g10.bnd"]);
        BindlistData result = BindlistParser.Parse(data);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Parse_Entries_InOnDiskOrder()
    {
        // spec: Docs/RE/formats/bindlist.md §Naming convention — "sorted but NON-contiguous": CONFIRMED.
        var data = BuildBindlist(["g1.bnd", "g5.bnd", "g10.bnd"]);
        BindlistData result = BindlistParser.Parse(data);

        Assert.Equal("g1.bnd", result.Entries[0]);
        Assert.Equal("g5.bnd", result.Entries[1]);
        Assert.Equal("g10.bnd", result.Entries[2]);
    }

    // =========================================================================
    // 2. No trailing CRLF on the final line (the real file's exact shape)
    // =========================================================================

    /// <summary>
    /// The final line of the real file has no trailing CRLF — it must still be treated as
    /// a valid entry.
    /// spec: Docs/RE/formats/bindlist.md §File structure —
    ///   "final line: terminated WITHOUT a trailing CRLF": CONFIRMED.
    /// </summary>
    [Fact]
    public void Parse_FinalLineWithoutCrlf_IsIncluded()
    {
        // BuildBindlist default = trailingCrlf:false → no \r\n after last entry.
        var data = BuildBindlist(["g1.bnd", "g3.bnd", "g7.bnd"], trailingCrlf: false);
        BindlistData result = BindlistParser.Parse(data);

        Assert.Equal(3, result.Count);
        Assert.Equal("g7.bnd", result.Entries[2]);
    }

    /// <summary>
    /// A file that DOES have a trailing CRLF (e.g. modified by an editor) must not produce
    /// a spurious empty entry at the end.
    /// spec: Docs/RE/formats/bindlist.md §File structure — "no blank lines": CONFIRMED.
    /// </summary>
    [Fact]
    public void Parse_TrailingCrlf_DoesNotProduceSpuriousEntry()
    {
        var data = BuildBindlist(["g1.bnd", "g2.bnd"], trailingCrlf: true);
        BindlistData result = BindlistParser.Parse(data);

        Assert.Equal(2, result.Count);
    }

    // =========================================================================
    // 3. IsRegistered look-up
    // =========================================================================

    [Fact]
    public void IsRegistered_ExistingEntry_ReturnsTrue()
    {
        // spec: Docs/RE/formats/bindlist.md — "authoritative set of g<N>.bnd values that are
        //   valid registered skeletons": CONFIRMED.
        var data = BuildBindlist(["g1.bnd", "g100.bnd", "g349.bnd"]);
        BindlistData result = BindlistParser.Parse(data);

        Assert.True(result.IsRegistered("g100.bnd"));
    }

    [Fact]
    public void IsRegistered_MissingEntry_ReturnsFalse()
    {
        // "A skeleton that is not listed here is not registered even if a g{N}.bnd file
        //  physically exists in the VFS."
        // spec: Docs/RE/formats/bindlist.md — gap entries absent: CONFIRMED.
        var data = BuildBindlist(["g1.bnd", "g3.bnd"]);
        BindlistData result = BindlistParser.Parse(data);

        // g2.bnd is not in the list (non-contiguous gap).
        Assert.False(result.IsRegistered("g2.bnd"));
    }

    [Fact]
    public void IsRegistered_CaseInsensitive()
    {
        // Filenames are ASCII; lookup is case-insensitive for robustness.
        var data = BuildBindlist(["g1.bnd"]);
        BindlistData result = BindlistParser.Parse(data);

        Assert.True(result.IsRegistered("G1.BND"));
    }

    // =========================================================================
    // 4. Non-contiguous numeric range (gaps in the sequence)
    // =========================================================================

    [Fact]
    public void Parse_NonContiguousIds_OnlyListedEntriesPresent()
    {
        // spec: Docs/RE/formats/bindlist.md §Naming convention —
        //   "sorted but NON-contiguous — there are deliberate gaps": CONFIRMED.
        // Simulate g1, g5, g20 — g2..g4 and g6..g19 are absent.
        var data = BuildBindlist(["g1.bnd", "g5.bnd", "g20.bnd"]);
        BindlistData result = BindlistParser.Parse(data);

        Assert.Equal(3, result.Count);
        Assert.True(result.IsRegistered("g1.bnd"));
        Assert.False(result.IsRegistered("g2.bnd"));
        Assert.True(result.IsRegistered("g5.bnd"));
        Assert.False(result.IsRegistered("g6.bnd"));
        Assert.True(result.IsRegistered("g20.bnd"));
    }

    // =========================================================================
    // 5. Empty input (degenerate case)
    // =========================================================================

    [Fact]
    public void Parse_EmptyBuffer_ReturnsEmptyCatalog()
    {
        BindlistData result = BindlistParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Equal(0, result.Count);
        Assert.Empty(result.Entries);
    }

    // =========================================================================
    // 6. Single-entry file without trailing CRLF
    // =========================================================================

    [Fact]
    public void Parse_SingleEntry_NoTrailingCrlf_Decoded()
    {
        var data = BuildBindlist(["g42.bnd"], trailingCrlf: false);
        BindlistData result = BindlistParser.Parse(data);

        Assert.Equal(1, result.Count);
        Assert.Equal("g42.bnd", result.Entries[0]);
        Assert.True(result.IsRegistered("g42.bnd"));
    }

    // =========================================================================
    // 7. Simulated 349-entry list (reference entry count from the spec)
    // =========================================================================

    [Fact]
    public void Parse_349Entries_CountMatches()
    {
        // Build a synthetic file with exactly 349 entries (matching the real file's known count).
        // The real file has non-contiguous ids; we use a simple 1-based range.
        // spec: Docs/RE/formats/bindlist.md §Entry count — "Total entries: 349": CONFIRMED.
        var names = Enumerable.Range(1, 349).Select(n => $"g{n}.bnd").ToArray();

        var data = BuildBindlist(names, trailingCrlf: false);
        BindlistData result = BindlistParser.Parse(data);

        Assert.Equal(349, result.Count);
        // First and last entries must round-trip.
        Assert.Equal(names[0], result.Entries[0]);
        Assert.Equal(names[348], result.Entries[348]);
    }
}