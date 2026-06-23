using Godot;
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


    private readonly TextureRect[] _gridCells = new TextureRect[GridCellCount];


    private bool _visible;


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
                 "spec: Docs/RE/specs/ui_hud_layout.md §1.1; ui_system.md §8.10.1 CODE-CONFIRMED.");
    }

    private void BuildItemGrid(HudAtlasLibrary atlas, int startX, int startY)
    {
        for (var row = 0; row < GridRows; row++)
        for (var col = 0; col < GridCols; col++)
        {
            var idx = row * GridCols + col;
            var x = startX + col * CellSide;
            var y = startY + row * CellSide;

            var cell = new TextureRect
            {
                Name = $"ItemCell{idx}",
                Position = new Vector2(x, y),
                Size = new Vector2(CellSide, CellSide),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Stop
            };

            var cellPanel = new Panel { Name = "CellBg" };
            cellPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var cs = new StyleBoxFlat();
            cs.BgColor = new Color(0.06f, 0.06f, 0.1f, 0.75f);
            cs.SetBorderWidthAll(1);
            cs.BorderColor = new Color(0.35f, 0.3f, 0.2f, 0.7f);
            cellPanel.AddThemeStyleboxOverride("panel", cs);
            cell.AddChild(cellPanel);

            AddChild(cell);
            _gridCells[idx] = cell;
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

            var cell = new TextureRect
            {
                Name = $"EquipCell{i}",
                Position = new Vector2(x, y),
                Size = new Vector2(CellSide, CellSide),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Stop
            };

            var cellPanel = new Panel { Name = "CellBg" };
            cellPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var cs = new StyleBoxFlat();
            cs.BgColor = new Color(0.09f, 0.06f, 0.12f, 0.75f);
            cs.SetBorderWidthAll(1);
            cs.BorderColor = new Color(0.4f, 0.25f, 0.45f, 0.7f);
            cellPanel.AddThemeStyleboxOverride("panel", cs);
            cell.AddChild(cellPanel);

            AddChild(cell);
            _equipCells[i] = cell;
        }
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
        GD.Print("[HudInventoryPanel] BindHub: inventory-contents event wiring deferred (TODO world-campaign).");
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