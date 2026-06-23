
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudCommandBar : Control
{

    private const float BarHeight = 45f;
    private const float SubStripH = 20f;
    private const float EntryBtnSize = 29f;
    private const float EntryBtnY = 10f;
    private const float QuickBtnSize = 21f;
    private const float QuickBtnY = 14f;

    private const int MsgChatBusy = 10082;
    private const int MsgProductUnavailable = 45003;
    private const int MsgNumberFormat = 2222;

    private static readonly float[] LeftClusterX = { 218f, 250f, 282f, 314f, 346f };

    private static readonly float[] RightClusterX = { 622f, 654f, 686f, 718f, 750f, 782f };

    private static readonly float[] QuickSlotX = { 856f, 898f, 940f };

    private bool _expanded;
    private Panel? _mainBar;
    private Action? _onHelp;


    private Action? _onInventory;
    private Action? _onParty;
    private Action? _onProduct;
    private Action? _onQuest;
    private Action? _onRelation;
    private Action? _onSkill;
    private Action? _onStats;
    private Panel? _subStrip;


    public void Wire(
        Action? onInventory,
        Action? onSkill,
        Action? onQuest,
        Action? onStats,
        Action? onHelp,
        Action? onParty,
        Action? onProduct,
        Action? onRelation = null)
    {
        _onInventory = onInventory;
        _onSkill = onSkill;
        _onQuest = onQuest;
        _onStats = onStats;
        _onHelp = onHelp;
        _onParty = onParty;
        _onProduct = onProduct;
        _onRelation = onRelation;
    }


    public void Build(HudAtlasLibrary atlas, HudTextLibrary? text = null)
    {
        Name = "HudCommandBar";

        AnchorLeft = 0f;
        AnchorTop = 1f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        OffsetTop = -(BarHeight + SubStripH);
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Stop;

        _mainBar = new Panel { Name = "MainBar" };
        _mainBar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var barStyle = new StyleBoxFlat();
        barStyle.BgColor = new Color(0.08f, 0.06f, 0.04f, 0.92f);
        barStyle.SetBorderWidthAll(1);
        barStyle.BorderColor = new Color(0.55f, 0.40f, 0.10f, 0.9f);
        _mainBar.AddThemeStyleboxOverride("panel", barStyle);
        AddChild(_mainBar);

        _subStrip = new Panel { Name = "SubStrip" };
        _subStrip.AnchorLeft = 0f;
        _subStrip.AnchorTop = 0f;
        _subStrip.AnchorRight = 1f;
        _subStrip.AnchorBottom = 0f;
        _subStrip.OffsetTop = BarHeight;
        _subStrip.OffsetBottom = BarHeight + SubStripH;
        var subStyle = new StyleBoxFlat();
        subStyle.BgColor = new Color(0.05f, 0.04f, 0.02f, 0.9f);
        subStyle.SetBorderWidthAll(1);
        subStyle.BorderColor = new Color(0.40f, 0.30f, 0.08f, 0.8f);
        _subStrip.AddThemeStyleboxOverride("panel", subStyle);
        AddChild(_subStrip);

        BuildEntryButton("BtnInventory", LeftClusterX[0], EntryBtnY,
            text?.GetCaption(0, "목록") ?? "목록", 4001, OnAction);
        BuildEntryButton("BtnRelation", LeftClusterX[1], EntryBtnY,
            text?.GetCaption(0, "관계") ?? "관계", 4002, OnAction);
        BuildEntryButton("BtnSkill", LeftClusterX[2], EntryBtnY,
            text?.GetCaption(0, "스킬") ?? "스킬", 4003, OnAction);
        BuildEntryButton("BtnQuest", LeftClusterX[3], EntryBtnY,
            text?.GetCaption(0, "퀘스트") ?? "퀘스트", 4004, OnAction);
        BuildEntryButton("BtnStatus", LeftClusterX[4], EntryBtnY,
            text?.GetCaption(0, "상태") ?? "상태", 4005, OnAction);

        BuildEntryButton("BtnHelp", RightClusterX[0], EntryBtnY,
            text?.GetCaption(0, "도움말") ?? "도움말", 4011, OnAction);
        BuildEntryButton("BtnParty", RightClusterX[1], EntryBtnY,
            text?.GetCaption(0, "파티") ?? "파티", 4012, OnAction);
        BuildEntryButton("BtnProduct", RightClusterX[2], EntryBtnY,
            text?.GetCaption(0, "제작") ?? "제작", 4013, OnAction);
        BuildEntryButton("BtnSlot161", RightClusterX[3], EntryBtnY,
            text?.GetCaption(0, "[161]") ?? "[161]", 4009, OnAction);
        BuildEntryButton("BtnSlot164", RightClusterX[4], EntryBtnY,
            text?.GetCaption(0, "[164]") ?? "[164]", 4010, OnAction);
        BuildEntryButton("BtnWarstone", RightClusterX[5], EntryBtnY,
            text?.GetCaption(0, "전투석") ?? "전투석", 4022, OnAction);

        BuildModeButton("BtnAttackMode", 448f, 0f, 128f, 46f, 4014, OnAction);
        BuildModeButton("BtnPeaceMode", 448f + 128f, 0f, 128f, 46f, 4015, OnAction);

        for (var i = 0; i < QuickSlotX.Length; i++)
        {
            var action = 4019 + i;
            var capturedI = i;
            BuildQuickSlotButton($"QuickSlot{i}", QuickSlotX[i], QuickBtnY, action, OnAction);
        }

        var collapseBtn = new Button
        {
            Name = "CollapseToggle",
            Text = "▲",
            Position = new Vector2(1008f - (BarHeight + SubStripH), 30f),
            Size = new Vector2(11f, 11f),
            MouseFilter = MouseFilterEnum.Stop
        };
        collapseBtn.AnchorLeft = 1f;
        collapseBtn.AnchorRight = 1f;
        collapseBtn.AnchorTop = 0f;
        collapseBtn.AnchorBottom = 0f;
        collapseBtn.OffsetLeft = -16f;
        collapseBtn.OffsetRight = -5f;
        collapseBtn.OffsetTop = 30f;
        collapseBtn.OffsetBottom = 41f;
        collapseBtn.Pressed += () => OnAction(4023);
        AddChild(collapseBtn);

        GD.Print("[HudCommandBar] Built — DefaultMenu slot 148 (bottom command strip). " +
                 "5 left-cluster entries (Inventory/Relation/Skill/Quest/Status) + " +
                 "6 right-cluster entries (Help/Party/Product/slot161/slot164/Warstone) + " +
                 "mode buttons + 3 quick-slot buttons + collapse toggle. " +
                 "Entry→slot wiring via HudMaster callbacks. " +
                 "TODO(world-campaign): slot 161, slot 164, warstone (240), relation (185), mode sends. " +
                 "spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED.");
    }


    private void BuildEntryButton(string nodeName, float x, float y, string label, int actionId, Action<int> handler)
    {
        var btn = new Button
        {
            Name = nodeName,
            Text = label,
            Position = new Vector2(x, y),
            Size = new Vector2(EntryBtnSize, EntryBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        btn.AddThemeFontSizeOverride("font_size", 8);
        var captured = actionId;
        btn.Pressed += () => handler(captured);
        _mainBar?.AddChild(btn);
    }

    private void BuildModeButton(string nodeName, float x, float y, float w, float h, int actionId, Action<int> handler)
    {
        var btn = new Button
        {
            Name = nodeName,
            Text = actionId == 4014 ? "⚔" : "☮",
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            MouseFilter = MouseFilterEnum.Stop
        };
        var captured = actionId;
        btn.Pressed += () => handler(captured);
        _mainBar?.AddChild(btn);
    }

    private void BuildQuickSlotButton(string nodeName, float x, float y, int actionId, Action<int> handler)
    {
        var btn = new Button
        {
            Name = nodeName,
            Text = "●",
            Position = new Vector2(x, y),
            Size = new Vector2(QuickBtnSize, QuickBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        btn.AddThemeFontSizeOverride("font_size", 8);
        var captured = actionId;
        btn.Pressed += () => handler(captured);
        _mainBar?.AddChild(btn);
    }


    private void OnAction(int actionId)
    {
        switch (actionId)
        {
            case 4000:
                _expanded = !_expanded;
                if (_subStrip != null) _subStrip.Visible = _expanded;
                GD.Print($"[HudCommandBar] action 4000 — bar expand: {_expanded}. " +
                         "spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED.");
                break;

            case 4001:
                _onInventory?.Invoke();
                GD.Print("[HudCommandBar] action 4001 → Inventory (slot 158). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4002:
                _onRelation?.Invoke();
                GD.Print("[HudCommandBar] action 4002 → RelationPanel slot 185 — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4003:
                _onSkill?.Invoke();
                GD.Print("[HudCommandBar] action 4003 → Skill (slot 159). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4004:
                _onQuest?.Invoke();
                GD.Print("[HudCommandBar] action 4004 → Quest (slot 206 mode 2). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4005:
                _onStats?.Invoke();
                GD.Print("[HudCommandBar] action 4005 → StatusPanel (slot 146). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4006:
            case 4008:
                GD.Print($"[HudCommandBar] action {actionId} → RelationPanel slot 193 — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4007:
                _onStats?.Invoke();
                GD.Print("[HudCommandBar] action 4007 → status group (chat-busy: msg 10082 — TODO). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4009:
                GD.Print("[HudCommandBar] action 4009 → slot 161 — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4010:
                GD.Print("[HudCommandBar] action 4010 → slot 164 — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4011:
                _onHelp?.Invoke();
                GD.Print("[HudCommandBar] action 4011 → HelpPanel (member §8.24) / button slot 176. " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4012:
                _onParty?.Invoke();
                GD.Print("[HudCommandBar] action 4012 → Party (slot 220). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4013:
                _onProduct?.Invoke();
                GD.Print("[HudCommandBar] action 4013 → ProductPanel (slot 230). " +
                         "TODO(world-campaign): billing+scene gates (msg 45003). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4014:
                GD.Print("[HudCommandBar] action 4014 → attack-mode OFF — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4015:
                GD.Print("[HudCommandBar] action 4015 → attack-mode ON — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4019:
            case 4020:
            case 4021:
                GD.Print($"[HudCommandBar] action {actionId} → quick-slot stance {actionId - 4019} — " +
                         "TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4022:
                GD.Print("[HudCommandBar] action 4022 → warstone (slot 240) — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4023:
                _expanded = !_expanded;
                if (_subStrip != null) _subStrip.Visible = _expanded;
                GD.Print($"[HudCommandBar] action 4023 — collapse toggle: {_expanded}. " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4024:
                _onQuest?.Invoke();
                GD.Print("[HudCommandBar] action 4024 → Quest (slot 206 mode 7). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            default:
                GD.Print($"[HudCommandBar] action {actionId} — unhandled (residual). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;
        }
    }
}