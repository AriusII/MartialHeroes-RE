
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

    private const uint ColorSay = 0xFFFFFFFFu;
    private const uint ColorShout = 0xFFCC99FFu;
    private const uint ColorParty = 0xFF00FFFFu;
    private const uint ColorGuild = 0xFF33FF66u;
    private const uint ColorEvent = 0xFFFFFF00u;
    private const uint ColorWhisper = 0xFFFF797Cu;
    private const uint ColorAlly = 0xFF82C4FFu;


    private readonly Queue<(string text, uint argb)> _ring = new(RingCapacity);
    private readonly StringBuilder _sb = new(4096);


    private ChannelReader<ChatLineEvent>? _chatLines;
    private bool _dirty;
    private LineEdit _input = null!;


    private RichTextLabel _log = null!;


    public event Action<int, string>? SendChatRequested;


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
        _log.OffsetTop = 2f;
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

        GD.Print("[HudChatPanel] Built — 448×324 output window + 330×20 input box. " +
                 "Ring=1000-line, visible=12. spec: Docs/RE/specs/ui_hud_layout.md §1.2 / chat.md §6.1.");
    }


    public void BindHub(IHudEventHub hub)
    {
        _chatLines = hub.ChatLines;
        GD.Print("[HudChatPanel] BindHub: ChatLines channel connected.");
    }

    public void WireSendIntent(ClientContext ctx)
    {
        SendChatRequested += (channel, text) =>
            _ = ctx.UseCases.SendChatAsync((uint)channel, text);
        GD.Print("[HudChatPanel] WireSendIntent: SendChatRequested wired to UseCases.SendChatAsync.");
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

    private static string EscapeBb(string s)
    {
        return s.Replace("[", "(").Replace("]", ")");
    }

    private void OnTextSubmitted(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return;
        _input.Clear();
        SendChatRequested?.Invoke(0, trimmed);
    }
}