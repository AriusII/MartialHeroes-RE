using Godot;

namespace MartialHeroes.Client.Godot.Ui;

public static class HudFont
{
    public const int SlotCount = 15;

    private static readonly string[] DotumCheFaces =
        ["DotumChe", "Dotum", "Gulim", "GulimChe", "Malgun Gothic"];

    private static readonly string[] DotumFaces =
        ["Dotum", "DotumChe", "Gulim", "Malgun Gothic"];

    private static readonly string[] BatangCheFaces =
        ["BatangChe", "Batang", "Gulim", "Malgun Gothic"];

    private static readonly SlotDescriptor[] Slots =
    [
        new(DotumCheFaces, 12, 6, 12, 0),
        new(DotumFaces, 10, 5, 10, 0),
        new(DotumCheFaces, 32, 16, 32, 800),
        new(DotumCheFaces, 18, 12, 24, 800),
        new(DotumCheFaces, 12, 6, 12, 800),
        new(BatangCheFaces, 12, 6, 12, 0),
        new(BatangCheFaces, 18, 12, 24, 700),
        new(BatangCheFaces, 12, 6, 12, 700),
        new(BatangCheFaces, 12, 6, 12, 700),
        new(DotumCheFaces, 12, 6, 12, 700),
        new(DotumFaces, 16, 10, 20, 800),
        new(DotumCheFaces, 10, 5, 10, 400),
        new(DotumCheFaces, 12, 6, 12, 400),
        new(DotumCheFaces, 14, 7, 14, 400),
        new(DotumCheFaces, 16, 8, 16, 400)
    ];


    public static ref readonly SlotDescriptor GetSlotDescriptor(int slot)
    {
        if ((uint)slot >= (uint)Slots.Length) slot = 0;
        return ref Slots[slot];
    }

    public static SystemFont CreateSlot(int slot)
    {
        ref readonly var desc = ref GetSlotDescriptor(slot);

        var sf = new SystemFont
        {
            FontNames = desc.FaceNames,
            FontWeight = desc.Weight == 0 ? 400 : desc.Weight,
            SubpixelPositioning = TextServer.SubpixelPositioning.Disabled,
            Hinting = TextServer.Hinting.None,
            Antialiasing = TextServer.FontAntialiasing.Gray
        };

        return sf;
    }

    public static int RowHeight(int slot)
    {
        return GetSlotDescriptor(slot).CellHeight;
    }

    public static int CharWidth(int slot)
    {
        return GetSlotDescriptor(slot).AdvanceWidth;
    }

    public static void ApplyToLabel(Label label, int slot)
    {
        label.AddThemeFontOverride("font", CreateSlot(slot));
        label.AddThemeFontSizeOverride("font_size", RowHeight(slot));
    }

    public static void ApplyToLineEdit(LineEdit edit, int slot)
    {
        edit.AddThemeFontOverride("font", CreateSlot(slot));
        edit.AddThemeFontSizeOverride("font_size", RowHeight(slot));
    }

    public readonly struct SlotDescriptor
    {
        public string[] FaceNames { get; }
        public int Size { get; }
        public int AdvanceWidth { get; }
        public int CellHeight { get; }
        public int Weight { get; }

        public SlotDescriptor(string[] faceNames, int size, int advanceWidth, int cellHeight, int weight)
        {
            FaceNames = faceNames;
            Size = size;
            AdvanceWidth = advanceWidth;
            CellHeight = cellHeight;
            Weight = weight;
        }
    }
}