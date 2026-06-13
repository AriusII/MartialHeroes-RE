using Godot;
using MartialHeroes.Client.Application.Hud;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Passive chat panel — a scrollable log over a 1000-line ring buffer, with per-channel ARGB
/// colour, a one-line input box, and a channel selector.
///
/// PASSIVE: zero game logic, zero protocol knowledge. It:
///   1. Reads from <see cref="IHudEventHub.ChatLines"/> (drained once per <c>_Process</c> frame).
///   2. Surfaces chat-send intent via <see cref="SendChatRequested"/> (integrator wires to a use-case call).
///
/// Integration (Stage Integrate — do NOT edit GameHud.cs or project.godot):
///   - Call <see cref="Bind"/> with the <see cref="IHudEventHub"/> after this node is in the tree.
///   - Subscribe to <see cref="SendChatRequested"/> to forward intent to an <c>IApplicationUseCases</c> call.
///
/// Toggle / keyboard contract:
///   <c>Enter</c> focuses the input box (or submits if already focused).
///   The window is always visible; the integrator may add a separate HUD toggle if desired.
///
/// Geometry (per spec §6.1, CODE-CONFIRMED):
///   Root panel: 448 × 324. Scrollable log: 12 visible lines. Bottom input bar: 448 × 41.
///   spec: Docs/RE/specs/chat.md §6.1 — "Root panel 448×324; bottom input bar 448×41; 12-line visible window".
///
/// Ring buffer (per spec §6.2, CODE-CONFIRMED):
///   1000-line ring; each record carries (text, channel, ARGB colour).
///   spec: Docs/RE/specs/chat.md §6.2 — "1000-line ring buffer, 36-byte records: text @+0x00, colour @+0x1C, channel @+0x20".
///
/// Channel colours (per spec §3, CODE-CONFIRMED · CAPTURE-UNVERIFIED):
///   say=0xFFFFFFFF  shout=0xFFCC99FF  party=0xFF00FFFF  guild=0xFF33FF66
///   event=0xFFFFFF00  special=0xFFFF797C  whisper=0xFFFF797C  alliance=0xFF82C4FF
///   spec: Docs/RE/specs/chat.md §3 (channel code → ARGB table).
///
/// Append routing (per spec §6.3, CODE-CONFIRMED · route):
///   channel &lt; 100      → normal chat log append.
///   channel == 11      → distinct insertion (PLAUSIBLE — purpose unrecovered; treated as say).
///   channel &gt; 100 &amp;&amp; != 110 → routed to a separate floating-notice system (dropped here).
///   channel == 110     → dropped.
///   spec: Docs/RE/specs/chat.md §6.3 (append sink routing rules).
///
/// spec: Docs/RE/specs/chat.md — full chat subsystem spec.
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive HUD.
/// </summary>
public sealed partial class ChatWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-derived constants
    // spec: Docs/RE/specs/chat.md §6.1 — geometry CODE-CONFIRMED.
    // spec: Docs/RE/specs/chat.md §6.2 — ring-buffer bounds CODE-CONFIRMED.
    // spec: Docs/RE/specs/chat.md §6.4 — 12-line visible window CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>Root panel width. spec: Docs/RE/specs/chat.md §6.1 — "448×324" CODE-CONFIRMED.</summary>
    private const int PanelW = 448; // spec: Docs/RE/specs/chat.md §6.1

    /// <summary>Root panel height. spec: Docs/RE/specs/chat.md §6.1 — "448×324" CODE-CONFIRMED.</summary>
    private const int PanelH = 324; // spec: Docs/RE/specs/chat.md §6.1

    /// <summary>Input bar height. spec: Docs/RE/specs/chat.md §6.1 — "bottom input bar 448×41" CODE-CONFIRMED.</summary>
    private const int InputBarH = 41; // spec: Docs/RE/specs/chat.md §6.1

    /// <summary>Log area height = panel height − input bar. spec: Docs/RE/specs/chat.md §6.1.</summary>
    private const int LogAreaH = PanelH - InputBarH; // = 283

    /// <summary>
    /// Total ring capacity. spec: Docs/RE/specs/chat.md §6.2 — "1000-line ring buffer" CODE-CONFIRMED.
    /// </summary>
    private const int RingCapacity = 1000; // spec: Docs/RE/specs/chat.md §6.2

    /// <summary>
    /// Visible lines in the scrollback window.
    /// spec: Docs/RE/specs/chat.md §6.4 — "12-line visible window" CODE-CONFIRMED.
    /// </summary>
    private const int VisibleLines = 12; // spec: Docs/RE/specs/chat.md §6.4

    /// <summary>
    /// Editbox max length. spec: Docs/RE/specs/chat.md §2.1 — "hard maximum length 100 characters" CODE-CONFIRMED.
    /// </summary>
    private const int InputMaxLength = 100; // spec: Docs/RE/specs/chat.md §2.1

    // -------------------------------------------------------------------------
    // Per-channel ARGB colours (spec §3, CODE-CONFIRMED · CAPTURE-UNVERIFIED)
    // spec: Docs/RE/specs/chat.md §3 — channel code → log colour (ARGB 0xAARRGGBB).
    // -------------------------------------------------------------------------

    private static readonly System.Collections.Generic.Dictionary<int, uint> ChannelArgb =
        new()
        {
            // spec: Docs/RE/specs/chat.md §3 (channel code table, CODE-CONFIRMED · CAPTURE-UNVERIFIED)
            { 0, 0xFFFFFFFFu }, // say / normal  — white    CODE-CONFIRMED
            { 1, 0xFFCC99FFu }, // shout         — lavender CODE-CONFIRMED
            { 2, 0xFF00FFFFu }, // party         — cyan     CODE-CONFIRMED
            { 3, 0xFF33FF66u }, // guild         — green    CODE-CONFIRMED
            { 6, 0xFFFFFF00u }, // event (misia) — yellow   CODE-CONFIRMED
            { 7, 0xFFFF797Cu }, // special event — pink     CODE-CONFIRMED
            { 9, 0xFFFF797Cu }, // whisper       — pink     CODE-CONFIRMED
            { 15, 0xFF82C4FFu }, // alliance      — blue     CODE-CONFIRMED
        };

    /// <summary>
    /// Returns the ARGB colour for the given channel code, defaulting to white (say colour) for
    /// any unknown code.
    /// spec: Docs/RE/specs/chat.md §3 (default = say = 0xFFFFFFFF).
    /// </summary>
    private static uint ArgbForChannel(int channel) =>
        ChannelArgb.TryGetValue(channel, out uint c) ? c : 0xFFFFFFFFu; // spec: Docs/RE/specs/chat.md §3

    // -------------------------------------------------------------------------
    // Channel display names (for the selector dropdown)
    // spec: Docs/RE/specs/chat.md §5 — tab → channel code table, CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    // Channel selector entries: (display label, channel code)
    // Tab order matches spec §5 (say → guild → alliance → party → event → shout → whisper).
    private static readonly (string Label, int Code)[] ChannelOptions =
    [
        ("Say", 0), // spec: Docs/RE/specs/chat.md §5 tab 3 → channel 0
        ("Guild", 3), // spec: Docs/RE/specs/chat.md §5 tab 4 → channel 3
        ("Alliance", 15), // spec: Docs/RE/specs/chat.md §5 tab 5 → channel 15
        ("Party", 2), // spec: Docs/RE/specs/chat.md §5 tab 6 → channel 2
        ("Event", 6), // spec: Docs/RE/specs/chat.md §5 tab 7 → channel 6 / 7
        ("Shout", 1), // spec: Docs/RE/specs/chat.md §5 tab 8 → channel 1
        ("Whisper", 9), // spec: Docs/RE/specs/chat.md §5 tab → channel 9 (whisper)
    ];

    // -------------------------------------------------------------------------
    // Append-routing sentinels
    // spec: Docs/RE/specs/chat.md §6.3 — append sink routing, CODE-CONFIRMED · route.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Channels ≥ this value are routed to the separate floating-notice system, not the log.
    /// spec: Docs/RE/specs/chat.md §6.3 — "channel > 100 → floating-notice system".
    /// </summary>
    private const int FloatingNoticeThreshold = 100; // spec: Docs/RE/specs/chat.md §6.3

    /// <summary>
    /// Channel 110 is always dropped.
    /// spec: Docs/RE/specs/chat.md §6.3 — "channel == 110 → dropped".
    /// </summary>
    private const int DroppedChannel110 = 110; // spec: Docs/RE/specs/chat.md §6.3

    // -------------------------------------------------------------------------
    // Ring buffer
    // -------------------------------------------------------------------------

    private readonly struct LogLine
    {
        public readonly string Text;
        public readonly int ChannelCode;
        public readonly uint ColorArgb;

        public LogLine(string text, int channelCode, uint colorArgb)
        {
            Text = text;
            ChannelCode = channelCode;
            ColorArgb = colorArgb;
        }
    }

    // Circular ring: _ring[_ringHead % RingCapacity] is the oldest live entry.
    private readonly LogLine[] _ring = new LogLine[RingCapacity];
    private int _ringCount; // 0..RingCapacity
    private int _ringHead; // index of the oldest live entry (ring start)
    private int _scrollOffset; // lines scrolled up from the bottom (0 = bottom)

    // -------------------------------------------------------------------------
    // HUD event hub
    // -------------------------------------------------------------------------

    private IHudEventHub? _hub;
    private CancellationTokenSource? _drainCts;

    // Buffered lines drained from the hub channel (on background async reader, posted to main thread).
    // We collect here to avoid lock contention; they are flushed at the start of each _Process frame.
    private readonly System.Collections.Concurrent.ConcurrentQueue<ChatLineEvent> _pending = new();

    // -------------------------------------------------------------------------
    // Outgoing-chat intent callback
    // Integrator subscribes and forwards to IApplicationUseCases (no network here).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised when the player submits a chat line. Args: (channelCode, text).
    ///
    /// The integrator MUST subscribe and forward this to the appropriate
    /// <c>IApplicationUseCases</c> call — this node has zero network knowledge.
    ///
    /// Toggle/keyboard note for the integrator:
    ///   <c>Enter</c> focuses the input box when it is unfocused; when focused it submits.
    ///   This node handles that toggle internally in <see cref="_Input"/>.
    ///   If additional toggle keys are needed (e.g. for a HUD-level toggle to show/hide the window),
    ///   the integrator should add those in the scene or GameHud, not here.
    /// </summary>
    public event Action<int, string>? SendChatRequested;

    // -------------------------------------------------------------------------
    // Child control references
    // -------------------------------------------------------------------------

    private RichTextLabel _logLabel = null!;
    private ScrollContainer _logScroll = null!;
    private LineEdit _inputBox = null!;
    private OptionButton _channelSelector = null!;

    // StringBuilder re-used for log re-render (avoids per-frame heap allocation).
    private readonly System.Text.StringBuilder _bbSb = new(8192);

    // -------------------------------------------------------------------------
    // Dirty flag: re-render the log only when new lines were appended.
    // -------------------------------------------------------------------------
    private bool _logDirty;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ChatWindow] _Ready BuildUi failed: {ex.Message}");
        }

        // Demo mode: inject sample lines so the window is not blank before a hub is bound.
        // Mirrors the InventoryWindow / SkillWindow "self-populate when no hub" pattern.
        AppendDemoLines();

        GD.Print("[ChatWindow] Ready. " +
                 $"Ring capacity {RingCapacity} (spec §6.2 CODE-CONFIRMED). " +
                 $"Visible lines {VisibleLines} (spec §6.4 CODE-CONFIRMED). " +
                 "Bind(hub) to start live channel consumption.");
    }

    public override void _ExitTree()
    {
        StopDrain();
    }

    /// <summary>
    /// Drains pending chat lines from the hub's channel and re-renders the log.
    /// Called once per Godot main-thread frame — all Control mutation here is on the main thread.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "drain Application channels on _Process".
    /// </summary>
    public override void _Process(double delta)
    {
        // Flush all pending lines that the background drain task posted.
        bool appended = false;
        while (_pending.TryDequeue(out ChatLineEvent? ev))
        {
            AppendLineInternal(ev.ChannelCode, BuildDisplay(ev), ev.ColorArgb);
            appended = true;
        }

        if (appended) _logDirty = true;

        if (_logDirty)
        {
            RenderLog();
            _logDirty = false;
        }
    }

    /// <summary>
    /// Handles Enter to focus / submit the input box.
    ///
    /// Toggle/keyboard contract:
    ///   - Enter when input is unfocused → focus the input box.
    ///   - Enter when input is focused   → submit the line (same as pressing Enter in the LineEdit).
    /// The integrator should document this for end-users:
    ///   "Press Enter to open the chat input. Type and press Enter again to send."
    /// spec: Docs/RE/specs/chat.md §2.1 — "Enter handler" CODE-CONFIRMED.
    /// </summary>
    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode != Key.Enter && key.Keycode != Key.KpEnter) return;

        if (!_inputBox.HasFocus())
        {
            _inputBox.GrabFocus();
            GetViewport().SetInputAsHandled();
        }
        // When the input box is focused, the LineEdit handles Enter internally (triggers _TextSubmitted).
    }

    // -------------------------------------------------------------------------
    // Public API — Bind / Subscribe
    // -------------------------------------------------------------------------

    /// <summary>
    /// Binds this panel to the application HUD event hub. Must be called on the main thread
    /// after this node is in the scene tree.
    ///
    /// Starts an async background reader that drains <see cref="IHudEventHub.ChatLines"/>
    /// and posts lines into a thread-safe queue; the main-thread <see cref="_Process"/> flush
    /// then applies them to the <see cref="RichTextLabel"/> log.
    ///
    /// Idempotent — calling Bind again replaces the previous hub.
    ///
    /// spec: Docs/RE/specs/chat.md §6.3 — append sink contract: (text, channel, colour).
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "drain Application channels on _Process".
    /// </summary>
    public void Bind(IHudEventHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);

        StopDrain();
        _hub = hub;
        StartDrain(hub);

        GD.Print("[ChatWindow] Bound to IHudEventHub. Draining ChatLines channel.");
    }

    /// <summary>
    /// Appends a line directly to the log. Thread-safe — may be called from any thread
    /// (posts to <see cref="_pending"/> and flushes on the next _Process frame).
    ///
    /// Channel routing (spec §6.3, CODE-CONFIRMED · route):
    ///   channel &lt; 100      → appended to log.
    ///   channel == 11      → distinct insertion path (PLAUSIBLE unrecovered; treated as say here).
    ///   channel &gt; 100 &amp;&amp; != 110 → floating-notice system — DROPPED at this layer.
    ///   channel == 110     → dropped.
    ///
    /// spec: Docs/RE/specs/chat.md §6.3 (append sink routing).
    /// spec: Docs/RE/specs/chat.md §3 (channel → ARGB colour).
    /// </summary>
    public void AppendLine(int channelCode, string text)
    {
        // Apply append-sink routing rules.
        // spec: Docs/RE/specs/chat.md §6.3 — routing CODE-CONFIRMED · route.
        if (channelCode == DroppedChannel110) return; // spec §6.3 — channel 110 dropped
        if (channelCode > FloatingNoticeThreshold) return; // spec §6.3 — >100 floating-notice, not chat log

        uint argb = ArgbForChannel(channelCode); // spec §3
        // Post to pending queue so this method is safe from any thread.
        _pending.Enqueue(new ChatLineEvent(channelCode, text, argb));
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildUi()
    {
        // Root: anchor to bottom-left of the viewport (matching legacy position defaults).
        // spec: Docs/RE/specs/chat.md §6.1 — "CHAT_WINDOW_POS_X default = screenW/2 − 512" (PLAUSIBLE relative placement).
        AnchorLeft = 0f;
        AnchorTop = 1f;
        AnchorRight = 0f;
        AnchorBottom = 1f;
        OffsetLeft = 4f;
        OffsetTop = -(PanelH + 4f);
        OffsetRight = PanelW + 4f;
        OffsetBottom = -4f;

        // Panel container — sized 448×324 per spec §6.1.
        // spec: Docs/RE/specs/chat.md §6.1 — "Root panel 448×324" CODE-CONFIRMED.
        var outerPanel = new PanelContainer();
        outerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.CustomMinimumSize = new Vector2(PanelW, PanelH);
        AddChild(outerPanel);

        // Slightly transparent background so the world is still visible beneath.
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0f, 0f, 0f, 0.65f);
        bgStyle.SetBorderWidthAll(1);
        bgStyle.BorderColor = new Color(0.4f, 0.4f, 0.4f, 0.9f);
        outerPanel.AddThemeStyleboxOverride("panel", bgStyle);

        // Root vbox: log area (top, expand) + input bar (bottom, fixed).
        var rootVBox = new VBoxContainer();
        rootVBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.AddChild(rootVBox);

        // ---- Log area (scrollable) ----
        // spec: Docs/RE/specs/chat.md §6.1 — background sprite 448×227 (log area), 12-line visible window.
        _logScroll = new ScrollContainer
        {
            Name = "ChatScroll",
            CustomMinimumSize = new Vector2(PanelW, LogAreaH),
        };
        _logScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        // Clip content within the scroll container; don't resize the log label.
        _logScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        rootVBox.AddChild(_logScroll);

        _logLabel = new RichTextLabel
        {
            Name = "ChatLog",
            BbcodeEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            FitContent = true,
            ScrollFollowing = true,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(PanelW - 8, 0),
        };
        // Font size for ~12 visible lines inside LogAreaH (283 px ÷ 12 ≈ 23 px/line).
        // spec: Docs/RE/specs/chat.md §6.1 — "CHAT_WINDOW_FONT_SIZE default 12" CODE-CONFIRMED.
        _logLabel.AddThemeFontSizeOverride("normal_font_size", 12); // spec: Docs/RE/specs/chat.md §6.1
        _logLabel.AddThemeFontSizeOverride("bold_font_size", 12);
        _logScroll.AddChild(_logLabel);

        // ---- Scroll buttons (spec §6.1 mentions a 17 px scrollbar column) ----
        // RichTextLabel.ScrollFollowingEnabled handles auto-scroll; the ScrollContainer provides the bar.
        // The native Godot scrollbar is 12 px wide; we leave the exact 17 px layout as PLAUSIBLE.
        // spec: Docs/RE/specs/chat.md §6.1 — "17 px-wide right scrollbar column" CODE-CONFIRMED (layout dimension).

        // ---- Input bar (bottom, 448×41) ----
        // spec: Docs/RE/specs/chat.md §6.1 — "bottom input bar 448×41" CODE-CONFIRMED.
        var inputBar = new HBoxContainer
        {
            Name = "InputBar",
            CustomMinimumSize = new Vector2(PanelW, InputBarH),
        };
        rootVBox.AddChild(inputBar);

        // Channel selector dropdown.
        // spec: Docs/RE/specs/chat.md §5 — channel tabs select the active send-channel CODE-CONFIRMED.
        _channelSelector = new OptionButton
        {
            Name = "ChannelSelector",
            CustomMinimumSize = new Vector2(72, InputBarH - 4),
        };
        foreach ((string label, int _) in ChannelOptions)
            _channelSelector.AddItem(label);
        _channelSelector.Selected = 0; // default: say (channel 0), spec §5 tab 3
        inputBar.AddChild(_channelSelector);

        // Input box — single line, 100-char cap.
        // spec: Docs/RE/specs/chat.md §2.1 — "size 330×20, max 100 characters" CODE-CONFIRMED.
        _inputBox = new LineEdit
        {
            Name = "ChatInput",
            PlaceholderText = "Press Enter to chat…",
            MaxLength = InputMaxLength, // spec: Docs/RE/specs/chat.md §2.1 — "max 100 characters" CODE-CONFIRMED
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, InputBarH - 4),
        };
        // Submit on Enter inside the LineEdit.
        // spec: Docs/RE/specs/chat.md §2.2 — "On Enter, the input parser runs" CODE-CONFIRMED.
        _inputBox.TextSubmitted += OnInputSubmitted;
        inputBar.AddChild(_inputBox);

        // Send button (small, for mouse users).
        var sendBtn = new Button
        {
            Text = ">",
            CustomMinimumSize = new Vector2(28, InputBarH - 4),
        };
        sendBtn.Pressed += () => SubmitInput(_inputBox.Text);
        inputBar.AddChild(sendBtn);
    }

    // -------------------------------------------------------------------------
    // Demo mode (no hub bound)
    // -------------------------------------------------------------------------

    private void AppendDemoLines()
    {
        // Show one line per channel so the colour table is visible in offline/demo mode.
        // spec: Docs/RE/specs/chat.md §3 — channel → colour (CODE-CONFIRMED).
        AppendLineInternal(0, "[Say] 안녕하세요! (Hello in Korean — CP949 safe rendering)", ArgbForChannel(0));
        AppendLineInternal(1, "[Shout] 마샬 히어로즈 온라인 복원 프로젝트", ArgbForChannel(1));
        AppendLineInternal(2, "[Party] Party member joined the area.", ArgbForChannel(2));
        AppendLineInternal(3, "[Guild] Guild message — 길드 메세지", ArgbForChannel(3));
        AppendLineInternal(6, "[Event] Event channel — 이벤트", ArgbForChannel(6));
        AppendLineInternal(7, "[Special] Special event notification.", ArgbForChannel(7));
        AppendLineInternal(9, "[Whisper] 속삭임 — log-only, no overhead bubble.", ArgbForChannel(9));
        AppendLineInternal(15, "[Alliance] Alliance broadcast — 동맹", ArgbForChannel(15));
        AppendLineInternal(0, "— DEMO MODE — Bind(hub) to receive live chat lines from IHudEventHub.ChatLines —",
            0xFF888888u);
        _logDirty = true;
    }

    // -------------------------------------------------------------------------
    // Submit handling
    // -------------------------------------------------------------------------

    private void OnInputSubmitted(string text)
    {
        SubmitInput(text);
        // Re-focus after submit so the player can keep typing.
        _inputBox.GrabFocus();
    }

    /// <summary>
    /// Reads the selected channel code and text, raises <see cref="SendChatRequested"/>, and
    /// echoes the line locally (per spec §2.2 step 3.1).
    ///
    /// spec: Docs/RE/specs/chat.md §2.2 — "1. echo locally; 2. send (2:7); 3. stamp overhead bubble"
    /// This node handles step 1 only; steps 2–3 are the integrator's use-case call.
    /// </summary>
    private void SubmitInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        int selectedIndex = _channelSelector.Selected;
        int channelCode = selectedIndex >= 0 && selectedIndex < ChannelOptions.Length
            ? ChannelOptions[selectedIndex].Code
            : 0; // default: say

        // Local echo — append with this channel's colour (spec §2.2 step 3.1).
        // spec: Docs/RE/specs/chat.md §2.2 — "echo locally via the log-append sink" CODE-CONFIRMED.
        AppendLine(channelCode, text);

        // Raise the intent so the integrator forwards to IApplicationUseCases.
        // The integrator must NOT perform any game-logic validation here.
        SendChatRequested?.Invoke(channelCode, text);

        _inputBox.Clear();
    }

    // -------------------------------------------------------------------------
    // Append sink (main-thread internal)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Internal append — always called on the main thread (from _Process flush or _Ready demo).
    /// Writes into the ring buffer; does NOT re-render (caller sets _logDirty).
    /// spec: Docs/RE/specs/chat.md §6.2 — ring: bump line counter capped at 1000, build record, insert.
    /// </summary>
    private void AppendLineInternal(int channelCode, string text, uint colorArgb)
    {
        // Apply routing filter (also enforced in AppendLine for external callers).
        // spec: Docs/RE/specs/chat.md §6.3.
        if (channelCode == DroppedChannel110) return;
        if (channelCode > FloatingNoticeThreshold) return;

        // Ring-buffer write:
        // Tail index = (_ringHead + _ringCount) % RingCapacity when not full.
        // When full, _ringHead advances to discard the oldest entry.
        // spec: Docs/RE/specs/chat.md §6.2 — "1000-line ring; line counter capped at 1000".
        int tailIndex;
        if (_ringCount < RingCapacity)
        {
            tailIndex = (_ringHead + _ringCount) % RingCapacity;
            _ringCount++;
        }
        else
        {
            // Ring full — overwrite oldest (advance head).
            tailIndex = _ringHead;
            _ringHead = (_ringHead + 1) % RingCapacity;
        }

        _ring[tailIndex] = new LogLine(text, channelCode, colorArgb);
        _logDirty = true;

        // Auto-reset scroll to bottom when new content arrives (user sees latest message).
        _scrollOffset = 0;
    }

    // -------------------------------------------------------------------------
    // Log rendering
    // -------------------------------------------------------------------------

    /// <summary>
    /// Re-renders the <see cref="RichTextLabel"/> from the ring buffer, showing the most recent
    /// <see cref="VisibleLines"/> lines above the current <see cref="_scrollOffset"/>.
    /// Uses BBCode colour tags so each channel line renders in its own ARGB colour.
    /// spec: Docs/RE/specs/chat.md §6.4 — "12-line visible window … per-channel colour" CODE-CONFIRMED.
    /// spec: Docs/RE/specs/chat.md §3 — ARGB colour table.
    /// </summary>
    private void RenderLog()
    {
        if (_logLabel is null) return;

        // Determine the visible window: most recent VisibleLines lines, offset by _scrollOffset.
        // Oldest entry is at _ringHead; newest is at (_ringHead + _ringCount − 1) % RingCapacity.
        // spec: Docs/RE/specs/chat.md §6.4 — "walks the ring from the start index with a 1000-entry cap".
        int total = _ringCount;
        if (total == 0)
        {
            _logLabel.Text = string.Empty;
            return;
        }

        // Clamp scrollOffset so we never show before the beginning of the ring.
        int maxOffset = Math.Max(0, total - VisibleLines);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);

        // Range of ring entries to show: [showStart, showEnd) (exclusive end), newest last.
        int showCount = Math.Min(VisibleLines, total - _scrollOffset);
        int showStart = total - _scrollOffset - showCount; // oldest shown entry (0-based from head)

        _bbSb.Clear();
        for (int i = 0; i < showCount; i++)
        {
            int absIndex = (_ringHead + (showStart + i)) % RingCapacity;
            ref readonly LogLine line = ref _ring[absIndex];

            uint argb = line.ColorArgb;
            // Convert ARGB (0xAARRGGBB) to BBCode [color=#rrggbb].
            // Alpha is deliberately ignored — text is always fully opaque in the log.
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);
            _bbSb.Append("[color=#");
            _bbSb.Append(r.ToString("X2"));
            _bbSb.Append(g.ToString("X2"));
            _bbSb.Append(b.ToString("X2"));
            _bbSb.Append(']');
            // Escape BBCode special characters in the text to avoid injection.
            AppendEscaped(_bbSb, line.Text);
            _bbSb.Append("[/color]");
            if (i < showCount - 1) _bbSb.Append('\n');
        }

        _logLabel.ParseBbcode(_bbSb.ToString());

        // Scroll to bottom when _scrollOffset == 0 (following latest output).
        if (_scrollOffset == 0)
        {
            // Defer to next frame so RichTextLabel has laid out new content.
            CallDeferred(MethodName.ScrollLogToBottom);
        }
    }

    private void ScrollLogToBottom()
    {
        if (_logScroll is null) return;
        // Scroll to bottom: assign int.MaxValue — ScrollContainer clamps to its actual max.
        _logScroll.ScrollVertical = int.MaxValue;
    }

    /// <summary>Appends text with '[' and ']' escaped to avoid breaking BBCode tags.</summary>
    private static void AppendEscaped(System.Text.StringBuilder sb, string text)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '[': sb.Append("[lb]"); break;
                case ']': sb.Append(']'); break; // closing bracket is safe unescaped in bbcode
                default: sb.Append(c); break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Background hub drain
    // -------------------------------------------------------------------------

    private void StartDrain(IHudEventHub hub)
    {
        _drainCts = new CancellationTokenSource();
        CancellationToken ct = _drainCts.Token;

        // Background task: reads from ChatLines channel and posts to _pending.
        // The main-thread _Process flush applies them to the Control on the next frame.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation —
        //       "All Control mutation happens on the main thread; drain on _Process or CallDeferred."
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (ChatLineEvent ev in hub.ChatLines.ReadAllAsync(ct))
                {
                    _pending.Enqueue(ev);
                }
            }
            catch (OperationCanceledException)
            {
                // Clean shutdown — expected when Bind is called again or node exits tree.
            }
            catch (Exception ex)
            {
                // GD.PrintErr must be deferred to the main thread.
                CallDeferred(MethodName.PrintDrainError, ex.Message);
            }
        }, ct);
    }

    private void StopDrain()
    {
        _drainCts?.Cancel();
        _drainCts?.Dispose();
        _drainCts = null;
    }

    // Called via CallDeferred from the background task on error.
    private void PrintDrainError(string message)
    {
        GD.PrintErr($"[ChatWindow] Hub drain error: {message}");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a display string from a <see cref="ChatLineEvent"/>, prepending the sender name
    /// when present (as "[SenderName]: text").
    /// spec: Docs/RE/specs/chat.md §6.3 — append sink takes (text, channel, colour).
    /// </summary>
    private static string BuildDisplay(ChatLineEvent ev)
    {
        if (string.IsNullOrEmpty(ev.SenderName))
            return ev.Text;
        return $"[{ev.SenderName}]: {ev.Text}";
    }
}