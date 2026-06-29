using System.Text;
using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudChatPanel : Control
{
    private const int PanelW = 448;
    private const int PanelH = 324;
    private const int LogBgH = 227;
    private const int InputBarH = 41;
    private const int ScrollbarW = 17;
    private const int EditW = 330;
    private const int EditH = 20;
    private const int EditLocalX = 5;
    private const int EditLocalY = 4;
    private const int EditMaxLength = 100;
    private const int VisibleLines = 12;
    private const int RingCapacity = 1000;

    private const int TabStripH = 16;
    private const int TabBtnW = 62;

    private const int ChSay = 0;
    private const int ChWhisper = 1;
    private const int ChParty = 2;
    private const int ChGuild = 3;
    private const int ChReserved = 4;
    private const int ChMisia = 6;
    private const int ChSpecialMisia = 7;
    private const int ChGm = 9;
    private const int ChAlliance = 15;

    private const int WhisperMaxLength = 119;

    private const uint ColorSay = 0xFFFFFFFFu;
    private const uint ColorShout = 0xFFCC99FFu;
    private const uint ColorParty = 0xFF00FFFFu;
    private const uint ColorGuild = 0xFF33FF66u;
    private const uint ColorEvent = 0xFFFFFF00u;
    private const uint ColorWhisper = 0xFFFF797Cu;
    private const uint ColorAlly = 0xFF82C4FFu;


    private readonly Queue<(string text, uint argb)> _ring = new(RingCapacity);
    private readonly StringBuilder _sb = new(4096);
    private int _activeChannel = ChSay;


    private ChannelReader<ChatLineEvent>? _chatLines;
    private bool _dirty;
    private LineEdit _input = null!;


    private RichTextLabel _log = null!;


    public event Action<int, string, string?>? SendChatRequested;

    public event Action<int, string?, string>? SpeechBubbleRequested;


    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudChatPanel";
        AnchorLeft = 0f;
        AnchorTop = 1f;
        AnchorRight = 0f;
        AnchorBottom = 1f;
        OffsetLeft = 0f;
        OffsetTop = -PanelH;
        OffsetRight = PanelW;
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Stop;

        var bg = new Panel { Name = "BgPanel" };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0f, 0f, 0f, 0.55f);
        bgStyle.SetBorderWidthAll(1);
        bgStyle.BorderColor = new Color(0.4f, 0.4f, 0.4f, 0.7f);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        BuildChannelTabs();

        _log = new RichTextLabel
        {
            Name = "Log",
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SelectionEnabled = false,
            MouseFilter = MouseFilterEnum.Pass
        };
        _log.AnchorLeft = 0f;
        _log.AnchorTop = 0f;
        _log.AnchorRight = 1f;
        _log.AnchorBottom = 0f;
        _log.OffsetLeft = 2f;
        _log.OffsetTop = TabStripH + 2f;
        _log.OffsetRight = -(ScrollbarW + 2f);
        _log.OffsetBottom = LogBgH - 2f;
        AddChild(_log);

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

        _input = new LineEdit
        {
            Name = "InputEditBox",
            MaxLength = EditMaxLength,
            PlaceholderText = "",
            CustomMinimumSize = new Vector2(EditW, EditH)
        };
        _input.AnchorLeft = 0f;
        _input.AnchorTop = 0f;
        _input.AnchorRight = 0f;
        _input.AnchorBottom = 0f;
        _input.OffsetLeft = EditLocalX;
        _input.OffsetTop = LogBgH + EditLocalY;
        _input.OffsetRight = EditLocalX + EditW;
        _input.OffsetBottom = LogBgH + EditLocalY + EditH;
        _input.TextSubmitted += OnTextSubmitted;
        HudFont.ApplyToLineEdit(_input, 4);
        AddChild(_input);

        GD.Print("[HudChatPanel] Built — 448×324 output window + 330×20 input box + 7 channel tabs. " +
                 "Ring=1000-line, visible=12. spec: Docs/RE/specs/ui_hud_layout.md §1.2 / chat.md §5/§6.1.");
    }

    private void BuildChannelTabs()
    {
        (string label, int channel)[] tabs =
        {
            ("Say", ChSay),
            ("Guild", ChGuild),
            ("Alliance", ChAlliance),
            ("Party", ChParty),
            ("Event", ChMisia),
            ("Shout", ChWhisper),
            ("Notice", ChSay)
        };

        for (var i = 0; i < tabs.Length; i++)
        {
            var (label, channel) = tabs[i];
            var capturedChannel = channel;
            var tabBtn = new Button
            {
                Name = $"ChatTab{i}",
                Text = label,
                Position = new Vector2(i * TabBtnW, 0f),
                Size = new Vector2(TabBtnW, TabStripH),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop
            };
            tabBtn.Pressed += () =>
            {
                _activeChannel = capturedChannel;
                GD.Print($"[HudChatPanel] active send-channel → {capturedChannel}. " +
                         "spec: Docs/RE/specs/chat.md §5 (tab → channel).");
            };
            AddChild(tabBtn);
        }
    }


    public void BindHub(IHudEventHub hub)
    {
        _chatLines = hub.ChatLines;
        GD.Print("[HudChatPanel] BindHub: ChatLines channel connected.");
    }

    public void WireSendIntent(ClientContext ctx)
    {
        SendChatRequested += (channel, text, recipient) =>
            _ = ctx.UseCases.SendChatAsync((uint)channel, text, recipient);
        GD.Print("[HudChatPanel] WireSendIntent: SendChatRequested wired to UseCases.SendChatAsync " +
                 "(channel + optional whisper recipient). spec: Docs/RE/specs/chat.md §4.1/§4.2.");
    }


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
        var ch = ev.ChannelCode;
        if (ch >= 100) return;

        var argb = ev.ColorArgb != 0 ? ev.ColorArgb : ArgbForChannel(ch);
        var display = ev.SenderName is { Length: > 0 } name
            ? $"{name}: {ev.Text}"
            : ev.Text;

        if (_ring.Count >= RingCapacity)
            _ring.Dequeue();

        _ring.Enqueue((display, argb));
        _dirty = true;

        if (ev.SenderName is { Length: > 0 } speaker && HasOverheadBubble(ch))
            SpeechBubbleRequested?.Invoke(ch, speaker, ev.Text);
    }

    private void FlushLog()
    {
        _sb.Clear();
        var skip = _ring.Count > VisibleLines ? _ring.Count - VisibleLines : 0;
        var idx = 0;
        foreach (var (text, argb) in _ring)
        {
            if (idx++ < skip) continue;
            var r = ((argb >> 16) & 0xFF) / 255f;
            var g = ((argb >> 8) & 0xFF) / 255f;
            var b = (argb & 0xFF) / 255f;
            _sb.Append($"[color=#{(int)(r * 255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}]");
            _sb.Append(EscapeBb(text));
            _sb.Append("[/color]\n");
        }

        _log.ParseBbcode(_sb.ToString());
    }

    private static uint ArgbForChannel(int ch)
    {
        return ch switch
        {
            ChSay => ColorSay,
            ChWhisper => ColorShout,
            ChParty => ColorParty,
            ChGuild => ColorGuild,
            ChMisia => ColorEvent,
            ChSpecialMisia => ColorWhisper,
            ChGm => ColorWhisper,
            ChAlliance => ColorAlly,
            _ => ColorSay
        };
    }

    private static bool HasOverheadBubble(int ch)
    {
        return ch is ChSay or ChParty or ChGuild or ChMisia or ChSpecialMisia or ChAlliance;
    }

    private static string EscapeBb(string s)
    {
        return s.Replace("[", "(").Replace("]", ")");
    }


    private void OnTextSubmitted(string text)
    {
        var line = text.Replace("\r", "").Replace("\n", "").Trim();
        _input.Clear();
        if (line.Length == 0) return;

        if (line[0] == '/')
        {
            HandleSlashCommand(line);
            return;
        }

        var channel = _activeChannel;
        string? recipient = null;
        var body = line;

        if (TrySplitToken(line, "party", out var rest))
        {
            channel = ChParty;
            body = rest;
        }
        else if (TrySplitToken(line, "guild", out rest))
        {
            channel = ChGuild;
            body = rest;
        }
        else if (TrySplitToken(line, "specialmisia", out rest))
        {
            channel = ChSpecialMisia;
            body = rest;
        }
        else if (TrySplitToken(line, "misia", out rest))
        {
            channel = ChMisia;
            body = rest;
        }
        else if (TrySplitToken(line, "alliance", out rest))
        {
            channel = ChAlliance;
            body = rest;
        }
        else if (TrySplitToken(line, "whisper", out rest))
        {
            channel = ChWhisper;
            SplitNameAndText(rest, out recipient, out body);
        }

        if (channel == ChWhisper && body.Length > WhisperMaxLength)
            body = body[..WhisperMaxLength];

        if (body.Length == 0)
            return;

        if (channel == ChReserved)
        {
            GD.Print("[HudChatPanel] channel 4 is reserved/local — accepted, no (2:7) send. " +
                     "spec: Docs/RE/specs/chat.md §3 (code 4: no send).");
            return;
        }

        EchoLocal(channel, recipient, body);
        SendChatRequested?.Invoke(channel, body, recipient);

        if (HasOverheadBubble(channel))
            SpeechBubbleRequested?.Invoke(channel, null, body);
    }

    private void HandleSlashCommand(string line)
    {
        var firstSpace = line.IndexOf(' ');
        var cmd = (firstSpace < 0 ? line : line[..firstSpace]).ToLowerInvariant();

        switch (cmd)
        {
            case "/":
            case "/help":
                GD.Print("[HudChatPanel] /help (or bare /) — help text is unrecovered (not fabricated); " +
                         "consumed, no (2:7) send. spec: Docs/RE/specs/chat.md §2.2 / §13 OQ5.");
                break;
            case "/option":
                GD.Print("[HudChatPanel] /option <int> <int> — per-account INI write; no chat send. " +
                         "spec: Docs/RE/specs/chat.md §2.2.");
                break;
            case "/msgchk":
                GD.Print("[HudChatPanel] /msgchk <int> — server message-check by id; no chat send. " +
                         "spec: Docs/RE/specs/chat.md §2.2.");
                break;
            case "/show":
            case "/hide":
                GD.Print($"[HudChatPanel] {cmd} 3dgage — debug gauge toggle; no chat send. " +
                         "spec: Docs/RE/specs/chat.md §2.2.");
                break;
            case "/item":
            case "/killdrop":
            case "/sysctl":
            case "/sysicon":
                GD.Print($"[HudChatPanel] {cmd} — GM-gated debug command (GM flag clear → ignored); " +
                         "no chat send. spec: Docs/RE/specs/chat.md §2.2 (GM-gated).");
                break;
            default:
                GD.Print($"[HudChatPanel] unknown slash command '{cmd}' — consumed, no chat send. " +
                         "spec: Docs/RE/specs/chat.md §2.2.");
                break;
        }
    }

    private void EchoLocal(int channel, string? recipient, string body)
    {
        var argb = ArgbForChannel(channel);
        var display = recipient is { Length: > 0 } name ? $"{name}: {body}" : body;

        if (_ring.Count >= RingCapacity)
            _ring.Dequeue();

        _ring.Enqueue((display, argb));
        _dirty = true;
    }

    private static bool TrySplitToken(string line, string token, out string rest)
    {
        var space = line.IndexOf(' ');
        if (space < 0)
        {
            if (line.Equals(token, StringComparison.Ordinal))
            {
                rest = string.Empty;
                return true;
            }

            rest = line;
            return false;
        }

        if (line.AsSpan(0, space).SequenceEqual(token))
        {
            rest = line[(space + 1)..].Trim();
            return true;
        }

        rest = line;
        return false;
    }

    private static void SplitNameAndText(string rest, out string? name, out string body)
    {
        var space = rest.IndexOf(' ');
        if (space < 0)
        {
            name = rest.Length > 0 ? rest : null;
            body = string.Empty;
            return;
        }

        name = rest[..space];
        body = rest[(space + 1)..].Trim();
    }
}