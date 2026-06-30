using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudProductWindow : Control
{
    private const int RecipeCellCount = 8;

    private const int MsgPrice = 45002;
    private const int MsgHaveCount = 45004;
    private const int MsgDetailLbl0 = 45011;
    private const int MsgDetailLbl1 = 45012;
    private const int MsgDetailLbl2 = 45013;
    private const int MsgDetailLbl3 = 45014;
    private const int MsgProdState0 = 714;
    private const int MsgProdState1 = 729;

    private const int MsgProdState2 = 744;

    private static readonly int[] RecipeCellX = { 29, 212, 395, 578 };
    private static readonly int[] RecipeCellY = { 172, 364 };


    private bool _open;
    private int _selectedRecipe = -1;

    private ClientContext? _ctx;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        _ctx = ClientContext.Instance;
        Name = "HudProductWindow";

        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -400f;
        OffsetTop = -350f;
        OffsetRight = 400f;
        OffsetBottom = 350f;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.06f, 0.10f, 0.97f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.50f, 0.40f, 0.20f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        var title = new Label
        {
            Name = "Title",
            Text = "Production / Crafting",
            Position = new Vector2(10f, 10f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(title);

        var previewPlaceholder = new ColorRect
        {
            Name = "ItemPreview3D_TODO",
            Color = new Color(0.10f, 0.10f, 0.18f, 0.7f),
            Position = new Vector2(20f, 40f),
            Size = new Vector2(120f, 80f)
        };
        AddChild(previewPlaceholder);
        var previewLbl = new Label
        {
            Name = "PreviewLbl",
            Text = string.Empty,
            Position = new Vector2(20f, 42f),
            Size = new Vector2(200f, 20f),
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(previewLbl);

        for (var row = 0; row < 2; row++)
        for (var col = 0; col < 4; col++)
        {
            var cellIdx = row * 4 + col;
            var cellX = RecipeCellX[col];
            var cellY = RecipeCellY[row];

            var capturedIdx = cellIdx;
            var cellBtn = new Button
            {
                Name = $"RecipeCell{cellIdx}",
                Text = string.Empty,
                Position = new Vector2(cellX, cellY),
                Size = new Vector2(160f, 40f),
                MouseFilter = MouseFilterEnum.Stop
            };
            cellBtn.Pressed += () => OnRecipeSelect(capturedIdx);
            AddChild(cellBtn);

            var makeBtn = new Button
            {
                Name = $"MakeBtn{cellIdx}",
                Text = "Make",
                Position = new Vector2(cellX + 110f, cellY + 45f),
                Size = new Vector2(50f, 20f),
                MouseFilter = MouseFilterEnum.Stop
            };
            var capturedMakeIdx = cellIdx + 8;
            makeBtn.Pressed += () => OnMakeAction(capturedMakeIdx);
            AddChild(makeBtn);
        }

        int[] detailIds = { MsgDetailLbl0, MsgDetailLbl1, MsgDetailLbl2, MsgDetailLbl3 };
        for (var i = 0; i < detailIds.Length; i++)
        {
            var caption = text?.GetCaption(detailIds[i], $"[msg {detailIds[i]}]") ?? $"[msg {detailIds[i]}]";
            var dlbl = new Label
            {
                Name = $"DetailLbl{i}",
                Text = caption,
                Position = new Vector2(20f, 40f + i * 22f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(dlbl);
        }

        var qtyBox = new LineEdit
        {
            Name = "QtyTextbox",
            Text = "1",
            MaxLength = 16,
            Position = new Vector2(20f, 510f),
            Size = new Vector2(80f, 24f),
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(qtyBox);

        var confirmBtn = new Button
        {
            Name = "ConfirmBtn",
            Text = "Confirm Order",
            Position = new Vector2(110f, 510f),
            Size = new Vector2(120f, 30f),
            MouseFilter = MouseFilterEnum.Stop
        };
        confirmBtn.Pressed += OnConfirmOrder;
        AddChild(confirmBtn);

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(760f, 10f),
            Size = new Vector2(24f, 24f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => Toggle(false);
        AddChild(closeBtn);

        var listStub = new Label
        {
            Name = "RecipeListStub",
            Text = string.Empty,
            Position = new Vector2(10f, 660f),
            Size = new Vector2(780f, 20f),
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(listStub);

        GD.Print("[HudProductWindow] Built — ProductPanel slot 230 (4×2 recipe grid, qty textbox, " +
                 "3D preview placeholder, action 36/90/0..15). " +
                 "Outbound WIRED: Make/Confirm → UseCases.ConfirmProductPurchaseAsync (C2S 2/153 production commit, " +
                 "4-byte slot tuple from the selected list slot, npc_index 0xFF=none) via ClientContext.Instance. " +
                 "Inbound recipe rows + 4/79 SmsgCraftingResult BLOCKED: no IHudEventHub crafting channel and " +
                 "CraftingResultEvent rides the single-consumer IClientEventBus drained only by GameLoop — rows stay " +
                 "empty (no mock data); recipe-list feed is runtime-capture-pending (crafting.md §6). " +
                 "spec: Docs/RE/specs/crafting.md §3.2/§4/§6 + cash_shop_browser.md §5.3 + ui_system.md §8.18 CODE-CONFIRMED.");
    }


    private void OnRecipeSelect(int cellIdx)
    {
        _selectedRecipe = cellIdx;
        GD.Print($"[HudProductWindow] Recipe select: cell {cellIdx}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.18.3 — action 0..7.");
    }

    private void OnMakeAction(int actionId)
    {
        var cell = actionId - RecipeCellCount;
        if (cell < 0 || cell >= RecipeCellCount)
        {
            GD.PrintErr($"[HudProductWindow] Make action {actionId} out of recipe range. " +
                        "spec: Docs/RE/specs/ui_system.md §8.18.3 — actions 8..15.");
            return;
        }

        _selectedRecipe = cell;
        CommitProduction($"make-cell-{cell}");
    }

    private void OnConfirmOrder()
    {
        CommitProduction("confirm-order");
    }

    private void CommitProduction(string origin)
    {
        if (_selectedRecipe < 0 || _selectedRecipe >= RecipeCellCount)
        {
            GD.Print($"[HudProductWindow] CommitProduction({origin}) ignored — no recipe selected. " +
                     "spec: Docs/RE/specs/crafting.md §6 step 4.");
            return;
        }

        var ctx = _ctx ?? ClientContext.Instance;
        if (ctx?.UseCases is null)
        {
            GD.PrintErr($"[HudProductWindow] CommitProduction({origin}) blocked — ClientContext.UseCases unavailable.");
            return;
        }

        var confirmCode = ProductionConfirmCode(_selectedRecipe);
        _ = ctx.UseCases.ConfirmProductPurchaseAsync(confirmCode);
        GD.Print($"[HudProductWindow] CommitProduction({origin}) → ConfirmProductPurchaseAsync(0x{confirmCode:X8}) " +
                 $"= C2S 2/153 production commit (list_slot={_selectedRecipe}, production_npc_index=0xFF none; " +
                 "server arms the 60s timeout and replies 4/79). " +
                 "spec: Docs/RE/specs/crafting.md §3.2/§6 + cash_shop_browser.md §5.3 CODE-CONFIRMED.");
    }

    private static uint ProductionConfirmCode(int listSlot)
    {
        const uint noProductionNpc = 0xFFu;
        return (noProductionNpc << 24) | ((uint)(listSlot & 0xFF) << 16);
    }


    public void Toggle(bool? forceState = null)
    {
        var wasOpen = _open;
        _open = forceState ?? !_open;
        Visible = _open;

        if (_open && !wasOpen)
            GD.Print("[HudProductWindow] Opened — production commit (2/153) routes on Make/Confirm user intent; " +
                     "no auto-emit on open. Recipe rows await an inbound feed that is BLOCKED (no IHudEventHub " +
                     "crafting channel; 4/79 rides the GameLoop-drained IClientEventBus) — grid stays empty, no mock data. " +
                     "spec: Docs/RE/specs/crafting.md §6 + ui_system.md §8.18.5 CODE-CONFIRMED.");
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}