using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudInventoryPanel : Control
{
    private const float InvW = 318f;

    private const float InvH = 732f;

    private const int GridCols = 8;
    private const int GridRows = 5;
    private const int CellSide = 38;
    private const int GridCellCount = GridCols * GridRows;

    private const int EquipCellCount = 20;

    private const int ActionIdEquipStart = 50;

    private const int InvTexId = 2;
    private readonly TextureRect[] _equipCells = new TextureRect[EquipCellCount];
    private readonly TextureRect[] _equipIcons = new TextureRect[EquipCellCount];
    private readonly Label[] _equipCounts = new Label[EquipCellCount];


    private readonly TextureRect[] _gridCells = new TextureRect[GridCellCount];
    private readonly TextureRect[] _gridIcons = new TextureRect[GridCellCount];
    private readonly Label[] _gridCounts = new Label[GridCellCount];


    private ChannelReader<InventorySlotsChangedEvent>? _invSlots;

    private readonly InventorySlotRecord[] _bagSlots = new InventorySlotRecord[GridCellCount];

    private readonly InventorySlotRecord[] _equipSlots = new InventorySlotRecord[EquipCellCount];

    public Func<uint, Texture2D?>? ItemIconResolver { get; set; }

    private bool _visible;


    public HudIconLibrary? IconLibrary { get; set; }


    public void Build(HudAtlasLibrary atlas, ClientContext ctx)
    {
        Name = "HudInventoryPanel";

        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = InvW;
        OffsetTop = 0f;
        OffsetRight = InvW + InvW;
        OffsetBottom = InvH;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var chromeTex = atlas.GetById(InvTexId);
        if (chromeTex is null)
            GD.PrintErr("[HudInventoryPanel] inventwindow.dds (uitex 2) unavailable (VFS offline). " +
                        "spec: Docs/RE/specs/ui_system.md §8.6.1.");

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.12f, 0.92f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.5f, 0.4f, 0.2f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        if (chromeTex is not null)
        {
            var chrome = new TextureRect
            {
                Name = "Chrome",
                Texture = chromeTex,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                MouseFilter = MouseFilterEnum.Ignore
            };
            chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(chrome);
        }

        var titleLbl = new Label
        {
            Name = "TitleLabel",
            Text = "인벤토리",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        titleLbl.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        titleLbl.OffsetBottom = 30f;
        AddChild(titleLbl);

        const int GridStartX = 8;
        const int GridStartY = 36;
        BuildItemGrid(atlas, GridStartX, GridStartY);

        var equipStartY = GridStartY + GridRows * CellSide + 10;
        BuildEquipGrid(atlas, GridStartX, equipStartY);

        BuildPaperdoll(GridStartX, equipStartY + EquipCellCount / 4 * CellSide + 10);

        GD.Print("[HudInventoryPanel] Built — 318×732 right-anchored (off-screen). " +
                 "8×5=40-cell grid (38×38, +38 pitch, actions 0..39). " +
                 "20-cell equip sub-grid (actions 50..69). " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §1.1; ui_system.md §8.10.1; inventory_trade.md §1.0/§1.1 CODE-CONFIRMED.");
    }

    private void BuildItemGrid(HudAtlasLibrary atlas, int startX, int startY)
    {
        for (var row = 0; row < GridRows; row++)
        for (var col = 0; col < GridCols; col++)
        {
            var idx = row * GridCols + col;
            var x = startX + col * CellSide;
            var y = startY + row * CellSide;
            BuildCell($"ItemCell{idx}", x, y, _gridCells, _gridIcons, _gridCounts, idx,
                new Color(0.06f, 0.06f, 0.1f, 0.75f), new Color(0.35f, 0.3f, 0.2f, 0.7f));
        }
    }

    private void BuildEquipGrid(HudAtlasLibrary atlas, int startX, int startY)
    {
        for (var i = 0; i < EquipCellCount; i++)
        {
            var col = i % GridCols;
            var row = i / GridCols;
            var x = startX + col * CellSide;
            var y = startY + row * CellSide;
            BuildCell($"EquipCell{i}", x, y, _equipCells, _equipIcons, _equipCounts, i,
                new Color(0.09f, 0.06f, 0.12f, 0.75f), new Color(0.4f, 0.25f, 0.45f, 0.7f));
        }
    }

    private void BuildCell(string name, int x, int y, TextureRect[] cells, TextureRect[] icons, Label[] counts,
        int idx, Color bg, Color border)
    {
        var cell = new TextureRect
        {
            Name = name,
            Position = new Vector2(x, y),
            Size = new Vector2(CellSide, CellSide),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Stop
        };

        var cellPanel = new Panel { Name = "CellBg" };
        cellPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var cs = new StyleBoxFlat();
        cs.BgColor = bg;
        cs.SetBorderWidthAll(1);
        cs.BorderColor = border;
        cellPanel.AddThemeStyleboxOverride("panel", cs);
        cell.AddChild(cellPanel);

        var icon = new TextureRect
        {
            Name = "Icon",
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        cell.AddChild(icon);

        var count = new Label
        {
            Name = "Count",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        count.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        cell.AddChild(count);

        AddChild(cell);
        cells[idx] = cell;
        icons[idx] = icon;
        counts[idx] = count;
    }

    private void BuildPaperdoll(int x, int y)
    {
        var paperdollPlaceholder = new Label
        {
            Name = "PaperdollPlaceholder",
            Text = string.Empty,
            Position = new Vector2(x, y),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(paperdollPlaceholder);
    }


    public void BindHub(IHudEventHub hub)
    {
        _invSlots = hub.InventorySlots;
        GD.Print("[HudInventoryPanel] BindHub: InventorySlots channel connected " +
                 "(40-cell bag + 20-cell equip). spec: Docs/RE/specs/inventory_trade.md §1.0/§1.1.");
    }


    public override void _Process(double delta)
    {
        if (_invSlots is null) return;

        while (_invSlots.TryRead(out var ev))
            if (ev is not null)
                ApplyInventory(ev);
    }

    private void ApplyInventory(InventorySlotsChangedEvent ev)
    {
        var target = ev.Table == InventoryTable.Bag ? _bagSlots : _equipSlots;

        if (ev.ClearAll)
            Array.Clear(target, 0, target.Length);
        else
            for (var i = 0; i < ev.Slots.Length; i++)
            {
                var dst = ev.BaseIndex + i;
                if (dst >= 0 && dst < target.Length)
                    target[dst] = ev.Slots[i];
            }

        RenderTable(_bagSlots, _gridIcons, _gridCounts);
        RenderTable(_equipSlots, _equipIcons, _equipCounts);
    }

    private void RenderTable(InventorySlotRecord[] slots, TextureRect[] icons, Label[] counts)
    {
        for (var i = 0; i < slots.Length; i++)
            if (slots[i].IsEmpty)
                ClearCell(icons, counts, i);
            else
                FillCell(icons, counts, i, slots[i]);
    }

    private void FillCell(TextureRect[] icons, Label[] counts, int idx, in InventorySlotRecord slot)
    {
        icons[idx].Texture = ItemIconResolver?.Invoke(slot.ItemActorId);

        var qty = slot.QtyOrExpiryLo;
        if (qty > 1u)
        {
            counts[idx].Text = qty.ToString();
            counts[idx].Visible = true;
        }
        else
        {
            counts[idx].Text = string.Empty;
            counts[idx].Visible = false;
        }
    }

    private static void ClearCell(TextureRect[] icons, Label[] counts, int idx)
    {
        icons[idx].Texture = null;
        counts[idx].Text = string.Empty;
        counts[idx].Visible = false;
    }


    public void Toggle()
    {
        _visible = !_visible;

        if (_visible)
        {
            OffsetLeft = -InvW;
            OffsetRight = 0f;
        }
        else
        {
            OffsetLeft = InvW;
            OffsetRight = InvW + InvW;
        }

        Visible = _visible;
        GD.Print($"[HudInventoryPanel] Toggle → visible={_visible}. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §1.1 / ui_system.md §8.10.1.");
    }
}
