namespace MartialHeroes.Client.Domain.Inventory;

/// <summary>
/// The reserved slot indices of the flat 20-slot item array and their meanings.
/// spec: Docs/RE/specs/inventory_trade.md §1.1 / §1.2 / §10.
/// </summary>
/// <remarks>
/// Slot indices are a single 0..19 space shared by worn gear and bag items (the 20 × 16-byte item
/// array). Only three indices have observed special meaning (§1.2). spec: inventory_trade.md §1.1/§1.2.
/// </remarks>
public static class EquipSlots
{
    /// <summary>Total item-array slots (worn gear + bag). spec: inventory_trade.md §1.1 / §10 (20 entries).</summary>
    public const int ItemArraySize = 20;

    /// <summary>Visual / appearance slot — an equip resolving here triggers a full visual gear rebuild. spec: inventory_trade.md §1.2 / §10 (slot 15).</summary>
    public const int VisualAppearanceSlot = 15;

    /// <summary>Special weapon slot. spec: inventory_trade.md §1.2 / §10 (slot 14).</summary>
    public const int SpecialWeaponSlot = 14;

    /// <summary>
    /// Excluded from "equip-onto-other" and from the worn-item stat sum. The category it holds is
    /// <c>UNVERIFIED</c>. spec: inventory_trade.md §1.2 / §10 (slot 8) and combat.md §2.1 (skip rule).
    /// </summary>
    public const int StatExcludedSlot = 8;

    /// <summary>True for a valid 0..19 item-array slot index. spec: inventory_trade.md §1.1.</summary>
    public static bool IsValidSlot(int slot) => (uint)slot < ItemArraySize;

    /// <summary>True when an equip resolving to <paramref name="slot"/> triggers the visual gear rebuild. spec: inventory_trade.md §1.2 (slot 15).</summary>
    public static bool TriggersVisualRebuild(int slot) => slot == VisualAppearanceSlot;

    /// <summary>True when <paramref name="slot"/> is skipped by the worn-item stat sum. spec: inventory_trade.md §1.2 / combat.md §2.1.</summary>
    public static bool IsExcludedFromStatSum(int slot) => slot == StatExcludedSlot;
}