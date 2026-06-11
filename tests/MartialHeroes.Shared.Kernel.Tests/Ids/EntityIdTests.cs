using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Shared.Kernel.Tests.Ids;

/// <summary>
/// Tests for the strongly-typed entity id value types:
/// <see cref="PlayerId"/>, <see cref="MonsterId"/>, <see cref="ItemId"/>, <see cref="SkillId"/>.
///
/// Goals:
///   1. None sentinel is the zero value.
///   2. Record-struct value equality works correctly.
///   3. IComparable ordering matches the underlying uint ordering.
///   4. Different id types with the same underlying uint are distinct types at compile time
///      (enforced structurally — the test verifies they do NOT share a type, and that value
///       equality holds WITHIN a type but not ACROSS types at the object level).
/// </summary>
public sealed class EntityIdTests
{
    // -------------------------------------------------------------------------
    // None sentinel
    // -------------------------------------------------------------------------

    [Fact]
    public void PlayerId_None_HasZeroValue()
    {
        Assert.Equal(0u, PlayerId.None.Value);
    }

    [Fact]
    public void MonsterId_None_HasZeroValue()
    {
        Assert.Equal(0u, MonsterId.None.Value);
    }

    [Fact]
    public void ItemId_None_HasZeroValue()
    {
        Assert.Equal(0u, ItemId.None.Value);
    }

    [Fact]
    public void SkillId_None_HasZeroValue()
    {
        Assert.Equal(0u, SkillId.None.Value);
    }

    [Fact]
    public void PlayerId_NewZero_EqualsNone()
    {
        Assert.Equal(PlayerId.None, new PlayerId(0u));
    }

    [Fact]
    public void MonsterId_NewZero_EqualsNone()
    {
        Assert.Equal(MonsterId.None, new MonsterId(0u));
    }

    [Fact]
    public void ItemId_NewZero_EqualsNone()
    {
        Assert.Equal(ItemId.None, new ItemId(0u));
    }

    [Fact]
    public void SkillId_NewZero_EqualsNone()
    {
        Assert.Equal(SkillId.None, new SkillId(0u));
    }

    // -------------------------------------------------------------------------
    // Record-struct value equality
    // -------------------------------------------------------------------------

    [Fact]
    public void PlayerId_SameValue_AreEqual()
    {
        var a = new PlayerId(42u);
        var b = new PlayerId(42u);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void PlayerId_DifferentValues_AreNotEqual()
    {
        var a = new PlayerId(1u);
        var b = new PlayerId(2u);
        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void MonsterId_SameValue_AreEqual()
    {
        var a = new MonsterId(99u);
        var b = new MonsterId(99u);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ItemId_SameValue_AreEqual()
    {
        var a = new ItemId(1234u);
        var b = new ItemId(1234u);
        Assert.Equal(a, b);
    }

    [Fact]
    public void SkillId_SameValue_AreEqual()
    {
        var a = new SkillId(7u);
        var b = new SkillId(7u);
        Assert.Equal(a, b);
    }

    // -------------------------------------------------------------------------
    // IComparable ordering
    // -------------------------------------------------------------------------

    [Fact]
    public void PlayerId_Ordering_SmallerValueIsLessThanLargerValue()
    {
        var smaller = new PlayerId(1u);
        var larger = new PlayerId(2u);
        Assert.True(smaller.CompareTo(larger) < 0);
        Assert.True(larger.CompareTo(smaller) > 0);
        Assert.Equal(0, smaller.CompareTo(new PlayerId(1u)));
    }

    [Fact]
    public void MonsterId_Ordering_SmallerValueIsLessThanLargerValue()
    {
        var a = new MonsterId(10u);
        var b = new MonsterId(20u);
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
    }

    [Fact]
    public void ItemId_Ordering_SmallerValueIsLessThanLargerValue()
    {
        var a = new ItemId(5u);
        var b = new ItemId(50u);
        Assert.True(a.CompareTo(b) < 0);
    }

    [Fact]
    public void SkillId_Ordering_SmallerValueIsLessThanLargerValue()
    {
        var a = new SkillId(0u);
        var b = new SkillId(uint.MaxValue);
        Assert.True(a.CompareTo(b) < 0);
    }

    [Fact]
    public void PlayerId_Ordering_EqualValuesReturnZero()
    {
        var a = new PlayerId(100u);
        var b = new PlayerId(100u);
        Assert.Equal(0, a.CompareTo(b));
    }

    // -------------------------------------------------------------------------
    // Type safety — different id types are not interchangeable
    // -------------------------------------------------------------------------
    // These are compile-time guarantees: PlayerId and MonsterId both wrap uint=42
    // but are completely different types. We verify this at runtime via GetType()
    // and by confirming equality methods are type-specific.

    [Fact]
    public void PlayerId_And_MonsterId_WithSameValue_HaveDifferentTypes()
    {
        // Compile-time safety: the C# type system prevents assigning PlayerId to MonsterId.
        // This runtime check confirms the types are distinct even with the same underlying value.
        var player = new PlayerId(42u);
        var monster = new MonsterId(42u);

        Assert.NotEqual(typeof(PlayerId), typeof(MonsterId));
        // object.Equals across different value types returns false
        Assert.False(player.Equals((object)monster));
    }

    [Fact]
    public void ItemId_And_SkillId_WithSameValue_HaveDifferentTypes()
    {
        var item = new ItemId(7u);
        var skill = new SkillId(7u);

        Assert.NotEqual(typeof(ItemId), typeof(SkillId));
        Assert.False(item.Equals((object)skill));
    }

    [Fact]
    public void PlayerId_ValueIsPreservedAfterConstruction()
    {
        uint raw = 0xDEADBEEFu;
        var id = new PlayerId(raw);
        Assert.Equal(raw, id.Value);
    }

    [Fact]
    public void MonsterId_ValueIsPreservedAfterConstruction()
    {
        uint raw = 0x12345678u;
        var id = new MonsterId(raw);
        Assert.Equal(raw, id.Value);
    }

    [Fact]
    public void ItemId_ValueIsPreservedAfterConstruction()
    {
        uint raw = uint.MaxValue;
        var id = new ItemId(raw);
        Assert.Equal(raw, id.Value);
    }

    [Fact]
    public void SkillId_ValueIsPreservedAfterConstruction()
    {
        uint raw = 1u;
        var id = new SkillId(raw);
        Assert.Equal(raw, id.Value);
    }
}
