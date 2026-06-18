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
//     - Size = the legacy logical size; CellHeight = the D3DX Height parameter.
//     - Weight 400 = normal, 700 = bold, 800 = extra-bold.
//   Fixed-advance layout uses the explicit per-slot advanceWidth.
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
    ///   advanceWidth, cellHeight, Weight.
    /// </summary>
    public readonly struct SlotDescriptor
    {
        public string[] FaceNames { get; }
        public int Size { get; } // legacy logical point/pixel size field
        public int AdvanceWidth { get; } // D3DX Width — fixed-advance layout
        public int CellHeight { get; } // D3DX Height — Godot font size
        public int Weight { get; } // GDI weight

        public SlotDescriptor(string[] faceNames, int size, int advanceWidth, int cellHeight, int weight)
        {
            FaceNames = faceNames;
            Size = size;
            AdvanceWidth = advanceWidth;
            CellHeight = cellHeight;
            Weight = weight;
        }
    }

    // spec: Docs/RE/specs/frontend_scenes.md — 15-slot table: face, size, advanceWidth, cellHeight, weight.
    private static readonly SlotDescriptor[] Slots =
    [
        new(DotumCheFaces, 12, 6, 12, 0), // 0
        new(DotumFaces, 10, 5, 10, 0), // 1
        new(DotumCheFaces, 32, 16, 32, 800), // 2
        new(DotumCheFaces, 18, 12, 24, 800), // 3
        new(DotumCheFaces, 12, 6, 12, 800), // 4
        new(BatangCheFaces, 12, 6, 12, 0), // 5
        new(BatangCheFaces, 18, 12, 24, 700), // 6
        new(BatangCheFaces, 12, 6, 12, 700), // 7
        new(BatangCheFaces, 12, 6, 12, 700), // 8
        new(DotumCheFaces, 12, 6, 12, 700), // 9
        new(DotumFaces, 16, 10, 20, 800), // 10
        new(DotumCheFaces, 10, 5, 10, 400), // 11
        new(DotumCheFaces, 12, 6, 12, 400), // 12
        new(DotumCheFaces, 14, 7, 14, 400), // 13
        new(DotumCheFaces, 16, 8, 16, 400), // 14
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
            FontNames = desc.FaceNames,
            FontWeight = desc.Weight == 0 ? 400 : desc.Weight,
            Antialiasing = TextServer.FontAntialiasing.Lcd,
            Hinting = TextServer.Hinting.None,
        };

        return sf;
    }

    /// <summary>
    /// Returns the row height (Godot font size) for the given slot.
    /// Use this to set <c>AddThemeFontSizeOverride("font_size", …)</c> on a label.
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.2 — D3DX Height = rowHeight.
    /// </summary>
    public static int RowHeight(int slot) => GetSlotDescriptor(slot).CellHeight;

    /// <summary>
    /// Returns the character cell width for the given slot.
    /// Used to compute fixed-advance label widths: <c>charWidth × text.Length</c>.
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.3 — "bounding rect = {x, y, x+charWidth×strlen, y+rowHeight}".
    /// </summary>
    public static int CharWidth(int slot) => GetSlotDescriptor(slot).AdvanceWidth;

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