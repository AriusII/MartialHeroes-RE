using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Shared.Kernel.Tests.Enums;

/// <summary>
/// Tests for <see cref="CharacterClass"/>.
///
/// Verifies numeric values against the RE spec (1-based class ids from the per-class
/// stat-grid loader), the underlying wire type, and member count.
/// spec: Docs/RE/formats/config_tables.md §2.6 — "Class names (CONFIRMED)".
/// </summary>
public sealed class CharacterClassTests
{
    // -------------------------------------------------------------------------
    // Numeric values — spec: Docs/RE/formats/config_tables.md §2.6 (CONFIRMED)
    // Class IDs are 1-based: Musa=1, Jagaek=2, Dosa=3, Seungnyeo=4.
    // -------------------------------------------------------------------------

    [Fact]
    public void Musa_HasValue_One()
    {
        // spec: Docs/RE/formats/config_tables.md §2.6 — Class ID 1 = Musa (CONFIRMED)
        Assert.Equal(1, (int)CharacterClass.Musa);
    }

    [Fact]
    public void Jagaek_HasValue_Two()
    {
        // spec: Docs/RE/formats/config_tables.md §2.6 — Class ID 2 = Jagaek (CONFIRMED)
        Assert.Equal(2, (int)CharacterClass.Jagaek);
    }

    [Fact]
    public void Dosa_HasValue_Three()
    {
        // spec: Docs/RE/formats/config_tables.md §2.6 — Class ID 3 = Dosa (CONFIRMED)
        Assert.Equal(3, (int)CharacterClass.Dosa);
    }

    [Fact]
    public void Seungnyeo_HasValue_Four()
    {
        // spec: Docs/RE/formats/config_tables.md §2.6 — Class ID 4 = Seungnyeo (CONFIRMED)
        Assert.Equal(4, (int)CharacterClass.Seungnyeo);
    }

    // Consolidated theory covering all four members.
    // spec: Docs/RE/formats/config_tables.md §2.6 — 1-based class id table (CONFIRMED).
    [Theory]
    [InlineData(CharacterClass.Musa, 1)]
    [InlineData(CharacterClass.Jagaek, 2)]
    [InlineData(CharacterClass.Dosa, 3)]
    [InlineData(CharacterClass.Seungnyeo, 4)]
    public void CharacterClass_Values_MatchSpec(CharacterClass cls, int expected)
    {
        // spec: Docs/RE/formats/config_tables.md §2.6 — values match legacy wire byte (1-based)
        Assert.Equal(expected, (int)cls);
    }

    // -------------------------------------------------------------------------
    // There is no zero value — classes are 1-based.
    // spec: Docs/RE/formats/config_tables.md §2.6 — class ids start at 1.
    // -------------------------------------------------------------------------

    [Fact]
    public void CharacterClass_HasNoZeroMember()
    {
        // spec: Docs/RE/formats/config_tables.md §2.6 — no class id 0 (1-based enum)
        bool hasZero = Enum.GetValues<CharacterClass>().Any(c => (byte)c == 0);
        Assert.False(hasZero);
    }

    // -------------------------------------------------------------------------
    // Underlying type is byte
    // spec: Docs/RE/formats/config_tables.md §3.5 — class index crosses the wire as a
    // single-byte / word field in stance selector packets.
    // -------------------------------------------------------------------------

    [Fact]
    public void CharacterClass_UnderlyingType_IsByte()
    {
        // spec: CharacterClass.cs — "public enum CharacterClass : byte"
        // (underlying type is byte per wire requirement; §3.5 stance-selector field)
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(CharacterClass)));
    }

    [Fact]
    public void CharacterClass_CastToByte_PreservesValue()
    {
        // spec: Docs/RE/formats/config_tables.md §2.6 — 1-based class ids 1..4
        Assert.Equal((byte)1, (byte)CharacterClass.Musa);
        Assert.Equal((byte)2, (byte)CharacterClass.Jagaek);
        Assert.Equal((byte)3, (byte)CharacterClass.Dosa);
        Assert.Equal((byte)4, (byte)CharacterClass.Seungnyeo);
    }

    [Fact]
    public void CharacterClass_FourDistinctMembers()
    {
        // spec: Docs/RE/formats/config_tables.md §2.6 — exactly four playable classes
        var members = Enum.GetValues<CharacterClass>();
        Assert.Equal(4, members.Length);
    }
}
