// Ui/Hud/HudChatPanel.cs
//
// In-game chat panel — two-class model (ChatOutputPanel + ChatPanel).
//
// Geometry (CODE-CONFIRMED):
//   Output window: 448 × 324, anchored at screen_width/2 − 512, screen_height − 324.
//   Input edit box: 330 × 20, panel-local (5, 4) inside the output window.
//   Scrollback: 1000-line ring, 12 visible lines.
//
// Channel colours (per spec §3):
//   say=0xFFFFFFFF shout=0xFFCC99FF party=0xFF00FFFF guild=0xFF33FF66
//   event=0xFFFFFF00 special/whisper=0xFFFF797C alliance=0xFF82C4FF
//
// Append routing (per spec §6.3):
//   channel < 100      → normal chat log append.
//   channel == 100     → dropped.
//   channel > 100 && != 110 → dropped (floating-notice, not here).
//   channel == 110     → dropped.
//
// PASSIVE: drains IHudEventHub.ChatLines in _Process; surfaces SendChatRequested.
//
// spec: Docs/RE/specs/ui_hud_layout.md §1.2 CONFIRMED (two-class model, geometry).
// spec: Docs/RE/specs/chat.md §2.1 / §6.1 / §6.2 / §6.3 / §6.4.

using System.Text;
using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game chat — 448×324 scrollable log + 330×20 input box.
///     <para>PASSIVE: drains <see cref="IHudEventHub.ChatLines" />; raises <see cref="SendChatRequested" /> intent.</para>
///     spec: Docs/RE/specs/ui_hud_layout.md §1.2 — geometry CODE-CONFIRMED.
///     spec: Docs/RE/specs/chat.md — full chat subsystem spec.
/// </summary>
public sealed partial class HudChatPanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_hud_layout.md §1.2 — geometry CODE-CONFIRMED
    // spec: Docs/RE/specs/chat.md §6.1–§6.4 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const int PanelW = 448; // spec: ui_hud_layout.md §1.2 / chat.md §6.1
    private const int PanelH = 324; // spec: ui_hud_layout.md §1.2 / chat.md §6.1
    private const int LogBgH = 227; // spec: ui_hud_layout.md §1.2 — background sprite
    private const int InputBarH = 41; // spec: ui_hud_layout.md §1.2 — bottom input bar strip
    private const int ScrollbarW = 17; // spec: ui_hud_layout.md §1.2 — right-edge scrollbar
    private const int EditW = 330; // spec: ui_hud_layout.md §1.2 — input edit box width
    private const int EditH = 20; // spec: ui_hud_layout.md §1.2 — single-line editbox
    private const int EditLocalX = 5; // spec: ui_hud_layout.md §1.2 — panel-local (5, 4)
    private const int EditLocalY = 4; // spec: ui_hud_layout.md §1.2 — panel-local (5, 4)
    private const int EditMaxLength = 100; // spec: ui_hud_layout.md §1.2 — editbox character cap
    private const int VisibleLines = 12; // spec: chat.md §6.4 CODE-CONFIRMED
    private const int RingCapacity = 1000; // spec: chat.md §6.2 CODE-CONFIRMED

    // Channel ARGB colours (CODE-CONFIRMED routing; CAPTURE-UNVERIFIED exact values)
    // spec: Docs/RE/specs/chat.md §3 (channel code → ARGB table)
    private const uint ColorSay = 0xFFFFFFFFu; // spec: chat.md §3 — say white
    private const uint ColorShout = 0xFFCC99FFu; // spec: chat.md §3 — shout
    private const uint ColorParty = 0xFF00FFFFu; // spec: chat.md §3 — party
    private const uint ColorGuild = 0xFF33FF66u; // spec: chat.md §3 — guild
    private const uint ColorEvent = 0xFFFFFF00u; // spec: chat.md §3 — event
    private const uint ColorWhisper = 0xFFFF797Cu; // spec: chat.md §3 — special/whisper
    private const uint ColorAlly = 0xFF82C4FFu; // spec: chat.md §3 — alliance

    // -------------------------------------------------------------------------
    // Ring buffer (view state — not domain state)
    // -------------------------------------------------------------------------

    private readonly Queue<(string text, uint argb)> _ring = new(RingCapacity);
    private readonly StringBuilder _sb = new(4096);

    // -------------------------------------------------------------------------
    // Channel drain state
    // -------------------------------------------------------------------------

    private ChannelReader<ChatLineEvent>? _chatLines;
    private bool _dirty;
    private LineEdit _input = null!;

    // -------------------------------------------------------------------------
    // Child controls
    // -------------------------------------------------------------------------

    private RichTextLabel _log = null!;

    // -------------------------------------------------------------------------
    // Send intent event (integrator wires to a use-case call)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Raised when the player submits a chat line.
    ///     Integrator must wire to <c>ctx.UseCases.SendChatAsync(channel, text)</c>.
    /// </summary>
    public event Action<int, string>? SendChatRequested;

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: positions the 448×324 window and builds the scrollback log + editbox.
    ///     Anchor: screen_width/2 − 512, screen_height − 324 (bottom-left area).
    ///     spec: Docs/RE/specs/ui_hud_layout.md §1.2 CONFIRMED — CHAT_WINDOW_POS_X/Y INI defaults.
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudChatPanel";
        // Anchor: bottom of screen, left of centre.
        // screen_width/2 − 512 = (1024/2) − 512 = 0 on the 1024 reference canvas.
        // screen_height − 324 → bottom-anchored.
        // In Godot: AnchorLeft=0, AnchorBottom=1 (bottom-relative), fixed size 448×324.
        AnchorLeft = 0f;
        AnchorTop = 1f;
        AnchorRight = 0f;
        AnchorBottom = 1f;
        OffsetLeft = 0f; // screen_width/2 − 512 on 1024-wide canvas = 0
        OffsetTop = -PanelH; // screen_height − 324 (bottom-anchored)
        OffsetRight = PanelW;
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Stop;

        // Panel background
        var bg = new Panel { Name = "BgPanel" };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0f, 0f, 0f, 0.55f);
        bgStyle.SetBorderWidthAll(1);
        bgStyle.BorderColor = new Color(0.4f, 0.4f, 0.4f, 0.7f);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        // Scrollable log area (top LogBgH = 227 px)
        // spec: chat.md §6.1 — "background sprite 448×227"
        _log = new RichTextLabel
        {
            Name = "Log",
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SelectionEnabled = false,
            MouseFilter = MouseFilterEnum.Pass
        };
        // Manual anchor+offset (no LayoutPreset.Custom — not available in Godot 4.6.3)
        _log.AnchorLeft = 0f;
        _log.AnchorTop = 0f;
        _log.AnchorRight = 1f;
        _log.AnchorBottom = 0f;
        _log.OffsetLeft = 2f;
        _log.OffsetTop = 2f;
        _log.OffsetRight = -(ScrollbarW + 2f); // leave room for scrollbar
        _log.OffsetBottom = LogBgH - 2f; // spec: chat.md §6.1 — log bg height 227
        AddChild(_log);

        // Input bar strip (bottom InputBarH = 41 px from Y=227)
        // spec: ui_hud_layout.md §1.2 — "input bar 448×41 at y=227"
        var inputBarBg = new Panel { Name = "InputBarBg" };
        inputBarBg.AnchorLeft = 0f;
        inputBarBg.AnchorTop = 0f;
        inputBarBg.AnchorRight = 1f;
        inputBarBg.AnchorBottom = 0f;
        inputBarBg.OffsetLeft = 0f;
        inputBarBg.OffsetTop = LogBgH;
        inputBarBg.OffsetRight = 0f;
        inputBarBg.OffsetBottom = LogBgH + InputBarH;
        var ibStyle = new StyleBoxFlat();
        ibStyle.BgColor = new Color(0f, 0f, 0f, 0.7f);
        inputBarBg.AddThemeStyleboxOverride("panel", ibStyle);
        AddChild(inputBarBg);

        // Edit box: 330×20 at panel-local (5, 4) inside the input bar
        // spec: ui_hud_layout.md §1.2 — "Width=330, Height=20, panel-local (5, 4)"
        _input = new LineEdit
        {
            Name = "InputEditBox",
            MaxLength = EditMaxLength, // spec: ui_hud_layout.md §1.2 — "Max length 100"
            PlaceholderText = "",
            CustomMinimumSize = new Vector2(EditW, EditH)
        };
        _input.AnchorLeft = 0f;
        _input.AnchorTop = 0f;
        _input.AnchorRight = 0f;
        _input.AnchorBottom = 0f;
        _input.OffsetLeft = EditLocalX; // spec: ui_hud_layout.md §1.2 — local x=5
        _input.OffsetTop = LogBgH + EditLocalY; // spec: ui_hud_layout.md §1.2 — local y=4 (in input bar)
        _input.OffsetRight = EditLocalX + EditW;
        _input.OffsetBottom = LogBgH + EditLocalY + EditH;
        _input.TextSubmitted += OnTextSubmitted;
        // Apply HUD caption font slot 4 to the chat input box.
        // spec: Docs/RE/specs/ui_hud_layout.md CYCLE 11 — "dominant HUD caption font slot 4"
        HudFont.ApplyToLineEdit(_input, 4); // spec: ui_hud_layout.md CYCLE 11 — slot 4
        AddChild(_input);

        GD.Print("[HudChatPanel] Built — 448×324 output window + 330×20 input box. " +
                 "Ring=1000-line, visible=12. spec: Docs/RE/specs/ui_hud_layout.md §1.2 / chat.md §6.1.");
    }

    // -------------------------------------------------------------------------
    // Hub binding
    // -------------------------------------------------------------------------

    /// <summary>Binds to the HUD event hub's ChatLines channel.</summary>
    public void BindHub(IHudEventHub hub)
    {
        _chatLines = hub.ChatLines;
        GD.Print("[HudChatPanel] BindHub: ChatLines channel connected.");
    }

    /// <summary>Wires the send intent to the use-case layer (via the context).</summary>
    public void WireSendIntent(ClientContext ctx)
    {
        SendChatRequested += (channel, text) =>
            _ = ctx.UseCases.SendChatAsync((uint)channel, text);
        GD.Print("[HudChatPanel] WireSendIntent: SendChatRequested wired to UseCases.SendChatAsync.");
    }

    // -------------------------------------------------------------------------
    // Per-frame drain
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_chatLines is null) return;

        while (_chatLines.TryRead(out var ev))
        {
            if (ev is null) continue;
            AppendLine(ev);
        }

        if (_dirty)
        {
            FlushLog();
            _dirty = false;
        }
    }

    private void AppendLine(ChatLineEvent ev)
    {
        // Append routing per spec §6.3
        // channel < 100      → normal log
        // channel == 100     → dropped
        // channel > 100 && != 110 → dropped (floating notice)
        // channel == 110     → dropped
        var ch = ev.ChannelCode;
        if (ch >= 100) return; // spec: chat.md §6.3

        var argb = ev.ColorArgb != 0 ? ev.ColorArgb : ArgbForChannel(ch);
        var display = ev.SenderName is { Length: > 0 } name
            ? $"{name}: {ev.Text}"
            : ev.Text;

        // Ring eviction — 1000-line cap
        // spec: chat.md §6.2 CODE-CONFIRMED
        if (_ring.Count >= RingCapacity)
            _ring.Dequeue();

        _ring.Enqueue((display, argb));
        _dirty = true;
    }

    private void FlushLog()
    {
        // Render at most VisibleLines from the tail of the ring.
        // spec: chat.md §6.4 CODE-CONFIRMED — "12 visible lines"
        _sb.Clear();
        var skip = _ring.Count > VisibleLines ? _ring.Count - VisibleLines : 0;
        var idx = 0;
        foreach (var (text, argb) in _ring)
        {
            if (idx++ < skip) continue;
            // Convert ARGB to Godot BBCode colour
            var r = ((argb >> 16) & 0xFF) / 255f;
            var g = ((argb >> 8) & 0xFF) / 255f;
            var b = (argb & 0xFF) / 255f;
            _sb.Append($"[color=#{(int)(r * 255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}]");
            _sb.Append(EscapeBb(text));
            _sb.Append("[/color]\n");
        }

        _log.ParseBbcode(_sb.ToString());
    }

    /// <summary>
    ///     FALLBACK-ONLY colour table. The authoritative colour is <c>ChatLineEvent.ColorArgb</c>
    ///     decided by the core (<c>GamePacketHandler.Chat.ResolveChatColour</c>). This method is used
    ///     ONLY when <c>ev.ColorArgb == 0</c> (i.e., the producer did not supply a colour — e.g., a
    ///     locally-echoed say line). The core is the authority; this is a defensive fallback.
    ///     spec: Docs/RE/specs/chat.md §3 — channel code → ARGB (authoritative source = core layer).
    /// </summary>
    private static uint ArgbForChannel(int ch)
    {
        return ch switch
        {
            // spec: Docs/RE/specs/chat.md §3 — channel code → ARGB table (CAPTURE-UNVERIFIED values)
            // NOTE: These are FALLBACK values only. The core (GamePacketHandler.Chat) is authoritative.
            0 => ColorSay,
            1 => ColorShout,
            2 => ColorParty,
            3 => ColorGuild,
            6 => ColorEvent,
            7 => ColorWhisper,
            9 => ColorWhisper,
            15 => ColorAlly,
            _ => ColorSay
        };
    }

    // Escape square brackets to prevent accidental BBCode injection.
    private static string EscapeBb(string s)
    {
        return s.Replace("[", "(").Replace("]", ")");
    }

    private void OnTextSubmitted(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return;
        _input.Clear();
        // Raise send intent with channel 0 (say); channel selection is a future feature.
        // spec: chat.md §2.2 / §4.1 — Enter handler sends via 2/7 opcode, channel code = 0.
        SendChatRequested?.Invoke(0, trimmed);
    }
}