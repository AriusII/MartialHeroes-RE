using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Shared.Kernel.Tests.Enums;

/// <summary>
/// Tests for <see cref="CharacterClass"/>.
///
/// Verifies numeric values and that the underlying type is <see cref="byte"/>,
/// as required by the wire protocol (single-byte field in character-select packets).
/// </summary>
public sealed class CharacterClassTests
{
    // -------------------------------------------------------------------------
    // Numeric values
    // spec: CharacterClass.cs — Warrior=0, Mage=1, Assassin=2, Monk=3
    // -------------------------------------------------------------------------

    [Fact]
    public void Warrior_HasValue_Zero()
    {
        // spec: CharacterClass.cs line 14 — Warrior = 0
        Assert.Equal(0, (int)CharacterClass.Warrior);
    }

    [Fact]
    public void Mage_HasValue_One()
    {
        // spec: CharacterClass.cs line 17 — Mage = 1
        Assert.Equal(1, (int)CharacterClass.Mage);
    }

    [Fact]
    public void Assassin_HasValue_Two()
    {
        // spec: CharacterClass.cs line 20 — Assassin = 2
        Assert.Equal(2, (int)CharacterClass.Assassin);
    }

    [Fact]
    public void Monk_HasValue_Three()
    {
        // spec: CharacterClass.cs line 23 — Monk = 3
        Assert.Equal(3, (int)CharacterClass.Monk);
    }

    // Consolidated theory covering all four members
    [Theory]
    [InlineData(CharacterClass.Warrior, 0)]
    [InlineData(CharacterClass.Mage, 1)]
    [InlineData(CharacterClass.Assassin, 2)]
    [InlineData(CharacterClass.Monk, 3)]
    public void CharacterClass_Values_MatchSpec(CharacterClass cls, int expected)
    {
        // spec: CharacterClass.cs — values match legacy wire byte
        Assert.Equal(expected, (int)cls);
    }

    // -------------------------------------------------------------------------
    // Underlying type is byte
    // spec: CharacterClass.cs — "public enum CharacterClass : byte"
    // Required so the value fits in a single wire byte without cast truncation.
    // -------------------------------------------------------------------------

    [Fact]
    public void CharacterClass_UnderlyingType_IsByte()
    {
        // spec: CharacterClass.cs line 12 — "enum CharacterClass : byte"
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(CharacterClass)));
    }

    [Fact]
    public void CharacterClass_CastToByte_PreservesValue()
    {
        Assert.Equal((byte)0, (byte)CharacterClass.Warrior);
        Assert.Equal((byte)1, (byte)CharacterClass.Mage);
        Assert.Equal((byte)2, (byte)CharacterClass.Assassin);
        Assert.Equal((byte)3, (byte)CharacterClass.Monk);
    }

    [Fact]
    public void CharacterClass_FourDistinctMembers()
    {
        var members = Enum.GetValues<CharacterClass>();
        Assert.Equal(4, members.Length);
    }
}
