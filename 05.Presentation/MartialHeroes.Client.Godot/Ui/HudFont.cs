// Ui/HudFont.cs
//
// The 15-slot HUD font descriptor table — faithful to the legacy D3DXCreateFontA table
// built once in the Login state (scene state 1).
//
// MECHANISM:
//   The legacy client used D3DXCreateFontA with charset=129 (HANGUL_CHARSET) for all
//   UI text. We map each slot to a Godot SystemFont using:
//     - FontNames matching the legacy face names (DotumChe / Dotum / BatangChe) plus
//       modern Korean fallbacks (Malgun Gothic, Gulim) for OS portability.
//     - Size = rowHeight from the spec table (the D3DX Height parameter).
//     - Weight 400 = normal, 700 = bold, 800 = extra-bold.
//   Fixed-advance layout is approximated by setting the font size to the spec's
//   rowHeight value; callers size label containers to charWidth × text.length.
//
// NOTE: This helper is ADDITIVE. It does NOT modify FontBootstrap or the existing
//       Autoload/FontBootstrap.cs. Use HudFont.CreateSlot(slot) in new Ui/ widgets.
//
// spec: Docs/RE/specs/ui_system.md §6.2 — 15-slot font descriptor table: CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §6.1 — "charset=129=HANGUL_CHARSET; built in Login state".
// spec: Docs/RE/specs/ui_system.md §6.3 — fixed-advance grid: charWidth per glyph.

using Godot;

namespace MartialHeroes.Client.Godot.Ui;

/// <summary>
/// HUD font helper — creates Godot <see cref="SystemFont"/> objects faithful to the legacy
/// 15-slot D3DXCreateFontA table built at Login (scene state 1).
///
/// <para>Use <see cref="CreateSlot"/> to produce a font for a specific slot, or
/// <see cref="ApplyToLabel"/> to set the slot's face+size on an existing label.</para>
///
/// spec: Docs/RE/specs/ui_system.md §6.2 — 15-slot table: CODE-CONFIRMED.
/// </summary>
public static class HudFont
{
    // -------------------------------------------------------------------------
    // The 15-slot font descriptor table
    // spec: Docs/RE/specs/ui_system.md §6.2 — CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    // Face name lists: legacy name first, then modern Windows fallbacks.
    // spec: Docs/RE/specs/ui_system.md §6.2 — face names DotumChe/Dotum/BatangChe CODE-CONFIRMED.
    private static readonly string[] DotumCheFaces =
        ["DotumChe", "Dotum", "Gulim", "GulimChe", "Malgun Gothic"];

    private static readonly string[] DotumFaces =
        ["Dotum", "DotumChe", "Gulim", "Malgun Gothic"];

    private static readonly string[] BatangCheFaces =
        ["BatangChe", "Batang", "Gulim", "Malgun Gothic"];

    /// <summary>
    /// Font slot descriptor (mirrors the D3DXCreateFontA arguments).
    /// spec: Docs/RE/specs/ui_system.md §6.2 — columns: Face, D3DX Height (rowHeight),
    ///   D3DX Width (charWidth), Weight.
    /// </summary>
    public readonly struct SlotDescriptor
    {
        public string[] FaceNames  { get; }
        public int      RowHeight  { get; } // D3DX Height — Godot font size
        public int      CharWidth  { get; } // D3DX Width  — fixed-advance layout
        public int      Weight     { get; } // GDI weight

        public SlotDescriptor(string[] faceNames, int rowHeight, int charWidth, int weight)
        {
            FaceNames = faceNames;
            RowHeight  = rowHeight;
            CharWidth  = charWidth;
            Weight     = weight;
        }
    }

    // spec: Docs/RE/specs/ui_system.md §6.2 — 15-slot table, index 0..14: CODE-CONFIRMED.
    private static readonly SlotDescriptor[] Slots =
    [
        // Slot  0 — DotumChe  12/6   wt 0
        new(DotumCheFaces,  12, 6,  0),   // spec §6.2 slot 0
        // Slot  1 — Dotum     10/5   wt 0
        new(DotumFaces,     10, 5,  0),   // spec §6.2 slot 1
        // Slot  2 — DotumChe  32/16  wt 800
        new(DotumCheFaces,  32, 16, 800), // spec §6.2 slot 2
        // Slot  3 — DotumChe  24/12  wt 800
        new(DotumCheFaces,  24, 12, 800), // spec §6.2 slot 3
        // Slot  4 — DotumChe  12/6   wt 800
        new(DotumCheFaces,  12, 6,  800), // spec §6.2 slot 4
        // Slot  5 — BatangChe 12/6   wt 0
        new(BatangCheFaces, 12, 6,  0),   // spec §6.2 slot 5
        // Slot  6 — BatangChe 24/12  wt 700
        new(BatangCheFaces, 24, 12, 700), // spec §6.2 slot 6
        // Slot  7 — BatangChe 12/6   wt 700
        new(BatangCheFaces, 12, 6,  700), // spec §6.2 slot 7
        // Slot  8 — BatangChe 12/6   wt 700
        new(BatangCheFaces, 12, 6,  700), // spec §6.2 slot 8
        // Slot  9 — DotumChe  12/6   wt 700
        new(DotumCheFaces,  12, 6,  700), // spec §6.2 slot 9
        // Slot 10 — Dotum     20/10  wt 800
        new(DotumFaces,     20, 10, 800), // spec §6.2 slot 10
        // Slot 11 — DotumChe  10/5   wt 400
        new(DotumCheFaces,  10, 5,  400), // spec §6.2 slot 11
        // Slot 12 — DotumChe  12/6   wt 400
        new(DotumCheFaces,  12, 6,  400), // spec §6.2 slot 12
        // Slot 13 — DotumChe  14/7   wt 400
        new(DotumCheFaces,  14, 7,  400), // spec §6.2 slot 13
        // Slot 14 — DotumChe  16/8   wt 400
        new(DotumCheFaces,  16, 8,  400), // spec §6.2 slot 14
    ];

    // Number of slots in the table.
    // spec: Docs/RE/specs/ui_system.md §6.2 — exactly 15 slots (indices 0..14): CODE-CONFIRMED.
    public const int SlotCount = 15; // spec: Docs/RE/specs/ui_system.md §6.2

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the <see cref="SlotDescriptor"/> for the given slot index (0..14).
    /// Returns slot 0 (DotumChe 12 / 6 / wt 0) for any out-of-range index.
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.2 — slot 0 is the default for all front-end text.
    /// </summary>
    public static ref readonly SlotDescriptor GetSlotDescriptor(int slot)
    {
        if ((uint)slot >= (uint)Slots.Length) slot = 0;
        return ref Slots[slot];
    }

    /// <summary>
    /// Creates a Godot <see cref="SystemFont"/> for the given font slot (0..14).
    ///
    /// <para>The returned font resolves the legacy Korean face names in priority order with
    /// Malgun Gothic as the Windows 10/11 guaranteed fallback. Font size = rowHeight from
    /// the spec §6.2 table.</para>
    ///
    /// <para>Note: weight emulation on <see cref="SystemFont"/> uses
    /// <see cref="SystemFont.FontWeight"/>. Godot resolves the nearest available weight
    /// from the OS font tables.</para>
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.2 — slot face/height/charWidth/weight: CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_system.md §6.1 — "charset=129=HANGUL_CHARSET": CODE-CONFIRMED.
    /// </summary>
    public static SystemFont CreateSlot(int slot)
    {
        ref readonly SlotDescriptor desc = ref GetSlotDescriptor(slot);

        var sf = new SystemFont
        {
            FontNames        = desc.FaceNames,
            FontWeight       = desc.Weight == 0 ? 400 : desc.Weight,
            Antialiasing     = TextServer.FontAntialiasing.Lcd,
            Hinting          = TextServer.Hinting.None,
        };

        return sf;
    }

    /// <summary>
    /// Returns the row height (Godot font size) for the given slot.
    /// Use this to set <c>AddThemeFontSizeOverride("font_size", …)</c> on a label.
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.2 — D3DX Height = rowHeight.
    /// </summary>
    public static int RowHeight(int slot) => GetSlotDescriptor(slot).RowHeight;

    /// <summary>
    /// Returns the character cell width for the given slot.
    /// Used to compute fixed-advance label widths: <c>charWidth × text.Length</c>.
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.3 — "bounding rect = {x, y, x+charWidth×strlen, y+rowHeight}".
    /// </summary>
    public static int CharWidth(int slot) => GetSlotDescriptor(slot).CharWidth;

    /// <summary>
    /// Applies the font slot's face and size overrides to the given <see cref="Label"/>.
    ///
    /// <para>Sets <c>font</c> and <c>font_size</c> theme overrides so this label uses the
    /// correct Korean face and pixel height for the slot.</para>
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.2 — per-widget font-slot index.
    /// spec: Docs/RE/specs/ui_system.md §6.3 — fixed-advance rendering.
    /// </summary>
    public static void ApplyToLabel(Label label, int slot)
    {
        label.AddThemeFontOverride("font", CreateSlot(slot));
        label.AddThemeFontSizeOverride("font_size", RowHeight(slot));
    }

    /// <summary>
    /// Applies the font slot's face and size overrides to the given <see cref="LineEdit"/>
    /// (used by HudTextbox to set CP949-input text size).
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.2 — GUTextbox font slot at +0xDC: CODE-CONFIRMED.
    /// </summary>
    public static void ApplyToLineEdit(LineEdit edit, int slot)
    {
        edit.AddThemeFontOverride("font", CreateSlot(slot));
        edit.AddThemeFontSizeOverride("font_size", RowHeight(slot));
    }
}
