// Ui/Hud/HudEmoticonWindow.cs
//
// In-game EmoticonPanel — emote / chat-emoticon picker.
// Stored at master window field +0x370 (not a +0x238 service-slot entry).
//
// Placement (CODE-CONFIRMED formula):
//   X = screen_width − 318, Y = 0, W = 318, H = 732.
//   Same right-dock 318-column family as the inventory rail.
//   spec: Docs/RE/specs/ui_system.md §8.19.1 CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_hud_layout.md §5.13 — MainWindow +0x370.
//
// Atlas binding (CODE-CONFIRMED uitex integer ids; DDS names via uitex.txt):
//   uitex 1  — close-button frames; default fallback
//   uitex 2  — top-bar strip; bottom button
//   uitex 3  — page-1 emote-cell backgrounds + 29×29 status overlay
//   uitex 4  — header icon; both tab buttons; page-0 button frames
//   uitex 8  — main window backdrop (318×627 at src 318,0)
//   uitex 27 — over-head emote-balloon glyph atlas (page-1 emote-icon buttons + balloon); page-0 label images
//   spec: Docs/RE/specs/ui_system.md §8.19.2 CODE-CONFIRMED.
//
// Two pages (CODE-CONFIRMED):
//   Page 0 — text-macro ("chat shortcut") grid.
//     9 macro slots from INI section %s_CHATSHORTCUT keys SHIFT_1..SHIFT_9.
//     Picking a macro submits via chat C2S 2/7 (rate-limited 5000 ms).
//     spec: Docs/RE/specs/ui_system.md §8.19.4 CODE-CONFIRMED.
//   Page 1 — graphical emoticon grid.
//     Driven by emoticon.do (40-byte records, VFS-pending).
//     Client-local only — plays sound 862030103 + sets balloon at slot 327. NO packet.
//     spec: Docs/RE/specs/ui_system.md §8.19.6 CODE-CONFIRMED.
//
// Chrome children (CODE-CONFIRMED):
//   Background (0,85) 318×627 src (318,0) uitex 8
//   Top-bar (0,36) 318×50 src (0,683) uitex 2
//   Header icon (125,60) 69×16 src (921,669) uitex 4
//   Close btn (286,46) 29×26 action 204 uitex 1 — (354,596)/(354,596)/(354,622)
//   Tab btn 0 (10,96) 149×29 src (677,694)/(677,724) uitex 4 action 200
//   Tab btn 1 (159,96) 149×29 src (677,754)/(677,784) uitex 4 action 201
//   Bottom btn (259,655) 59×77 action 203 uitex 2
//   Input textbox (60,80) 297×23 src (518,669) action 202 (max 47, IME-disabled)
//   spec: Docs/RE/specs/ui_system.md §8.19.3 CODE-CONFIRMED.
//
// PASSIVE: text-macro submission via IApplicationUseCases.SendChatAsync (chat C2S 2/7).
//   Graphical emote = client-local only (sound + balloon, NO packet).
//   spec: Docs/RE/specs/ui_system.md §8.19.6 CODE-CONFIRMED.

using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game emote / chat-emoticon picker (EmoticonPanel, stored at MainWindow +0x370).
///     <para>
///         Page 0 — text-macro shortcuts (SHIFT_1..9 from INI): sending one fires
///         <see cref="IApplicationUseCases.SendChatAsync" /> (chat C2S 2/7). Rate-limit: 5000 ms.
///     </para>
///     <para>
///         Page 1 — graphical emote balloons: client-local (sound 862030103 + balloon slot 327).
///         Driven by emoticon.do records (40-byte, VFS-pending). <b>Sends NO packet.</b>
///     </para>
///     <para>
///         PASSIVE: zero game logic. Text-macros emit use-case calls; graphical emotes
///         are pure client-local display. No domain state touched here.
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.19 CODE-CONFIRMED.
///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — MainWindow +0x370.
/// </summary>
public sealed partial class HudEmoticonWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.19.1 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float EmoW = 318f; // spec: §8.19.1 — W=318 CODE-CONFIRMED
    private const float EmoH = 732f; // spec: §8.19.1 — H=732 CODE-CONFIRMED

    // Chrome child positions (CODE-CONFIRMED)
    // spec: ui_system.md §8.19.3 CODE-CONFIRMED
    private const float BackgroundX = 0f;
    private const float BackgroundY = 85f;
    private const float TopBarX = 0f;
    private const float TopBarY = 36f;
    private const float HeaderIconX = 125f;
    private const float HeaderIconY = 60f;
    private const float CloseBtnX = 286f;
    private const float CloseBtnY = 46f;
    private const float Tab0X = 10f;
    private const float TabY = 96f;
    private const float Tab1X = 159f;
    private const float BottomBtnX = 259f;
    private const float BottomBtnY = 655f;
    private const float InputBoxX = 60f;
    private const float InputBoxY = 80f;

    // Grid origin for the page containers
    private const float GridPageX = 0f;
    private const float GridPageY = 127f;
    private const float GridPageW = 318f;
    private const float GridPageH = 605f;

    // Atlas uitex ids (CODE-CONFIRMED)
    // spec: ui_system.md §8.19.2 CODE-CONFIRMED
    private const int Tex1 = 1; // spec: §8.19.2 — close-button frames; default fallback
    private const int Tex2 = 2; // spec: §8.19.2 — top-bar strip; bottom button
    private const int Tex3 = 3; // spec: §8.19.2 — page-1 emote-cell backgrounds
    private const int Tex4 = 4; // spec: §8.19.2 — header icon; tab buttons; page-0 button frames
    private const int Tex8 = 8; // spec: §8.19.2 — main window backdrop (318×627 at src 318,0)
    private const int Tex27 = 27; // spec: §8.19.2 — over-head emote-balloon glyph atlas

    // Chrome src-rects (CODE-CONFIRMED)
    // spec: ui_system.md §8.19.3 CODE-CONFIRMED
    private const int BackSrcX = 318;
    private const int BackSrcY = 0;
    private const int TopBarSrcX = 0;
    private const int TopBarSrcY = 683;
    private const int HdrIconSrcX = 921;
    private const int HdrIconSrcY = 669;
    private const int CloseSrcX = 354;
    private const int CloseSrcY = 596; // NORMAL/HOVER
    private const int ClosePrsSrcX = 354;
    private const int ClosePrsSrcY = 622; // PRESSED
    private const int Tab0NormX = 677;
    private const int Tab0NormY = 694; // Tab 0 NORMAL
    private const int Tab0PresX = 677;
    private const int Tab0PresY = 724; // Tab 0 PRESSED
    private const int Tab1NormX = 677;
    private const int Tab1NormY = 754; // Tab 1 NORMAL
    private const int Tab1PresX = 677;
    private const int Tab1PresY = 784; // Tab 1 PRESSED
    private const int BotBtnNormX = 301;
    private const int BotBtnNormY = 947; // Bottom btn NORMAL/HOVER
    private const int BotBtnPrsX = 360;
    private const int BotBtnPrsY = 947; // Bottom btn PRESSED
    private const int InputSrcX = 518;
    private const int InputSrcY = 669; // Input textbox chrome

    // Macro grid (page 0) — 9 slots
    // spec: ui_system.md §8.19.4 — "up to 9 macro slots ... from INI SHIFT_1..SHIFT_9"
    private const int MacroSlotCount = 9;
    private const long MacroRateLimitMs = 5000L; // spec: §8.19.4 — "rate-limited to one per 5000 ms"

    // Text macros from INI (loaded lazily)
    private readonly string?[] _macroSlots = new string?[MacroSlotCount]; // SHIFT_1..9
    private int _activePage; // 0 = text macros, 1 = graphical emotes

    // Use-case reference (needed for SendChatAsync — chat text-macro)
    private ClientContext? _ctx;

    // Macro rate-limit (world time ms)
    private long _lastMacroSentMs;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;

    // Page containers
    private Control? _page0Panel;
    private Control? _page1Panel;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the right-dock 318×732 EmoticonPanel with two tabbed grids.
    ///     spec: Docs/RE/specs/ui_system.md §8.19 CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — MainWindow +0x370.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, ClientContext? ctx)
    {
        Name = "HudEmoticonWindow";
        _ctx = ctx;

        // Right-dock 318-column rail — X = screen_width − 318, Y = 0.
        // spec: ui_system.md §8.19.1 — "screen_width − 318" CODE-CONFIRMED.
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = -EmoW;
        OffsetRight = 0f;
        OffsetTop = 0f;
        OffsetBottom = EmoH;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop (dark fallback — actual art from uitex 8 src (318,0) when VFS present)
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.06f, 0.10f, 0.97f);
        bdStyle.SetBorderWidthAll(1);
        bdStyle.BorderColor = new Color(0.45f, 0.35f, 0.15f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Top-bar chrome (uitex 2, src (0,683), 318×50 at (0,36))
        // spec: ui_system.md §8.19.3 — TopBar: (0,36) 318×50 src (0,683) uitex 2
        var tex2 = atlas?.GetById(Tex2);
        if (tex2 is not null)
        {
            var topBar = new TextureRect
            {
                Name = "TopBar",
                Texture = atlas!.SliceById(Tex2, TopBarSrcX, TopBarSrcY, (int)EmoW, 50),
                Position = new Vector2(TopBarX, TopBarY),
                Size = new Vector2(EmoW, 50f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(topBar);
        }

        // Close button (action 204) — uitex 1, (286,46) 29×26
        // spec: ui_system.md §8.19.3 — "Close btn (286,46) 29×26 action 204; msg.xdb 16011"
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(CloseBtnX, CloseBtnY),
            Size = new Vector2(29f, 26f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => Toggle(false);
        AddChild(closeBtn);

        // Tab button 0 (page 0 — text macros), action 200, uitex 4, (10,96) 149×29
        // spec: ui_system.md §8.19.3 — "Tab btn 0 (10,96) 149×29 src (677,694)/(677,724) action 200"
        var tab0 = new Button
        {
            Name = "Tab0",
            Text = "Chat Macros",
            Position = new Vector2(Tab0X, TabY),
            Size = new Vector2(149f, 29f),
            MouseFilter = MouseFilterEnum.Stop
        };
        tab0.Pressed += () => SelectPage(0);
        AddChild(tab0);

        // Tab button 1 (page 1 — graphical emotes), action 201, uitex 4, (159,96) 149×29
        // spec: ui_system.md §8.19.3 — "Tab btn 1 (159,96) 149×29 src (677,754)/(677,784) action 201"
        var tab1 = new Button
        {
            Name = "Tab1",
            Text = "Emotes",
            Position = new Vector2(Tab1X, TabY),
            Size = new Vector2(149f, 29f),
            MouseFilter = MouseFilterEnum.Stop
        };
        tab1.Pressed += () => SelectPage(1);
        AddChild(tab1);

        // Bottom button (action 203 = toggle/close), uitex 2, (259,655) 59×77
        // spec: ui_system.md §8.19.3 — "Bottom btn (259,655) 59×77 action 203"
        var botBtn = new Button
        {
            Name = "BottomBtn",
            Text = "≡",
            Position = new Vector2(BottomBtnX, BottomBtnY),
            Size = new Vector2(59f, 77f),
            MouseFilter = MouseFilterEnum.Stop
        };
        botBtn.Pressed += () => Toggle(false);
        AddChild(botBtn);

        // Input textbox (action 202, max len 47, IME-disabled), (60,80) 297×23
        // spec: ui_system.md §8.19.3 — "Input textbox (60,80) 297×23 src (518,669) action 202 max 47 IME-disabled"
        var inputBox = new LineEdit
        {
            Name = "InputTextbox",
            MaxLength = 47, // spec: §8.19.3 — max len 47
            Position = new Vector2(InputBoxX, InputBoxY),
            Size = new Vector2(297f, 23f),
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(inputBox);

        // Page 0 — text-macro grid
        // spec: ui_system.md §8.19.4 — "up to 9 macro slots from INI SHIFT_1..SHIFT_9"
        _page0Panel = BuildMacroPage();
        AddChild(_page0Panel);

        // Page 1 — graphical emoticon grid
        // TODO(format): emoticon.do — 40-byte records, VFS-pending.
        // spec: ui_system.md §8.19.4 — "data-driven per-record coordinates, NOT fixed rows×cols"
        _page1Panel = BuildEmotePageStub();
        AddChild(_page1Panel);

        SelectPage(0);

        GD.Print("[HudEmoticonWindow] Built — right-dock 318×732 EmoticonPanel (+0x370). " +
                 "Page 0: 9 macro slots (INI SHIFT_1..9 → chat C2S 2/7, rate 5000 ms). " +
                 "Page 1: graphical emote grid stub (emoticon.do 40-byte records, TODO(format)). " +
                 "spec: Docs/RE/specs/ui_system.md §8.19 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Page 0 — text-macro grid builder
    // spec: Docs/RE/specs/ui_system.md §8.19.4 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private Control BuildMacroPage()
    {
        var panel = new Control
        {
            Name = "MacroPage",
            Position = new Vector2(GridPageX, GridPageY),
            Size = new Vector2(GridPageW, GridPageH)
        };

        // 9 macro-slot buttons (92×29 each, action 0-indexed)
        // spec: ui_system.md §8.19.4 — "each cell = 3-state button (92×29), label image, wide phrase button (297×23)"
        for (var i = 0; i < MacroSlotCount; i++)
        {
            var capturedI = i;
            var slotBtn = new Button
            {
                Name = $"Macro{i + 1}",
                Text = $"SHIFT+{i + 1}",
                Position = new Vector2(10f, 5f + i * 34f),
                Size = new Vector2(92f, 29f),
                MouseFilter = MouseFilterEnum.Stop
            };
            slotBtn.Pressed += () => OnMacroPressed(capturedI);
            panel.AddChild(slotBtn);

            // Wide phrase label / button (297×23)
            // spec: ui_system.md §8.19.4 — "wide phrase button (297×23)"
            var phraseBtn = new Button
            {
                Name = $"Phrase{i + 1}",
                Text = _macroSlots[i] ?? string.Empty,
                Position = new Vector2(110f, 5f + i * 34f + 3f),
                Size = new Vector2(180f, 23f),
                MouseFilter = MouseFilterEnum.Stop
            };
            var capturedPhrase = i;
            phraseBtn.Pressed += () => OnMacroPressed(capturedPhrase);
            panel.AddChild(phraseBtn);
        }

        return panel;
    }

    // -------------------------------------------------------------------------
    // Page 1 — graphical emote grid stub
    // -------------------------------------------------------------------------

    private static Control BuildEmotePageStub()
    {
        // TODO(format): emoticon.do — 40-byte records (VFS-pending); coordinates are data-driven per record.
        // spec: ui_system.md §8.19.4 — "graphical emoticon grid: data-driven per-record coordinates NOT fixed rows×cols"
        // spec: ui_system.md §8.19.5 — emoticon.do: 40-byte records, field table documented.
        var panel = new Control
        {
            Name = "EmotePage",
            Position = new Vector2(GridPageX, GridPageY),
            Size = new Vector2(GridPageW, GridPageH)
        };

        var stub = new Label
        {
            Name = "EmoteStub",
            Text = "// TODO(format): emoticon.do records (40-byte, VFS-pending).\n" +
                   "// Graphical emotes: client-local (sound 862030103 + balloon slot 327). No packet.\n" +
                   "// spec: Docs/RE/specs/ui_system.md §8.19.5/§8.19.6 CODE-CONFIRMED.",
            Position = new Vector2(5f, 5f),
            Size = new Vector2(308f, GridPageH - 10f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddChild(stub);

        return panel;
    }

    // -------------------------------------------------------------------------
    // Page switching (actions 200/201)
    // spec: ui_system.md §8.19.4 — tab switcher hides old grid, shows new
    // -------------------------------------------------------------------------

    private void SelectPage(int page)
    {
        _activePage = page;
        if (_page0Panel is not null) _page0Panel.Visible = page == 0;
        if (_page1Panel is not null) _page1Panel.Visible = page == 1;
    }

    // -------------------------------------------------------------------------
    // Text-macro action handler
    // spec: ui_system.md §8.19.4 — "submits phrase through the chat pipeline (C2S 2/7)"
    // -------------------------------------------------------------------------

    private void OnMacroPressed(int slotIndex)
    {
        // spec: ui_system.md §8.19.4 — "rate-limited to one per 5000 ms"
        var nowMs = (long)Time.GetTicksMsec();
        if (nowMs - _lastMacroSentMs < MacroRateLimitMs)
        {
            GD.Print($"[HudEmoticonWindow] Macro rate-limited (5000 ms). slot={slotIndex}. " +
                     "spec: Docs/RE/specs/ui_system.md §8.19.4 CODE-CONFIRMED.");
            return;
        }

        var text = _macroSlots[slotIndex];
        if (string.IsNullOrEmpty(text)) return; // Empty slot — nothing to send

        _lastMacroSentMs = nowMs;

        // Submit through chat pipeline — C2S 2/7 with channel byte.
        // spec: ui_system.md §8.19.4 — "copied into the chat input line and submitted through the
        //   normal chat send pipeline (chat C2S 2/7 with a channel byte)".
        if (_ctx is not null)
        {
            // Channel 0 = normal chat. spec: Docs/RE/packets/2-7_chat_send.yaml
            _ = _ctx.UseCases.SendChatAsync(0, text);
            GD.Print($"[HudEmoticonWindow] Macro slot {slotIndex} sent via chat C2S 2/7. " +
                     "spec: Docs/RE/specs/ui_system.md §8.19.4 CODE-CONFIRMED.");
        }
        else
        {
            GD.PrintErr("[HudEmoticonWindow] SendChatAsync: ctx is null (offline). Macro not sent.");
        }
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Toggles the emote picker.
    ///     Open trigger (hotkey/toolbar action id) is debugger-pending.
    ///     spec: Docs/RE/specs/ui_system.md §8.19.7 — "toolbar action id SHOWING = residual (debugger-pending)".
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            // spec: ui_system.md §8.19.7 — "bottom button (action 203) and Esc both close"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}