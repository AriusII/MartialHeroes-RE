// Ui/Widgets/HudLabel.cs
//
// HUD label widget — faithful reimplementation of GULabel behaviour.
//
// Fixed-advance CP949 text rendering:
//   The legacy D3DXFont draws with a fixed-advance grid:
//     bounding rect = {x, y, x + charWidth(slot)*strlen, y + rowHeight(slot)*scale}
//   The backing node remains a Godot Label for existing scene code, but its draw callback
//   places each glyph at slot.advanceWidth rather than using proportional shaping.
//
// Font slot defaults to 0 (DotumChe 12/6/wt0) — the zero-initialised label slot default.
//
// spec: Docs/RE/specs/ui_system.md §6.3 — fixed-advance grid rendering: CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §6.2 — 15-slot font table: CODE-CONFIRMED.
// spec: Docs/RE/structs/gucomponent.md +0xE4 — GULabel font-slot field, default 0.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
///     GULabel-faithful fixed-advance HUD text label.
///     <para>
///         CP949 strings from msg.xdb arrive already decoded as .NET strings from
///         <see cref="Assets.HudTextLibrary" />. Pass them directly to <see cref="Text" />.
///         Never decode bytes in the UI layer.
///     </para>
///     spec: Docs/RE/specs/ui_system.md §6.3 — fixed-advance text rendering.
/// </summary>
public sealed partial class HudLabel : HudWidget
{
    private readonly FixedAdvanceLabel _label;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates a HudLabel.
    /// </summary>
    /// <param name="x">Screen-local X on the 1024×768 canvas.</param>
    /// <param name="y">Screen-local Y.</param>
    /// <param name="w">Width in pixels (determines label wrap / clip boundary).</param>
    /// <param name="h">Height in pixels.</param>
    /// <param name="text">Initial text (CP949-decoded .NET string from msg.xdb or empty).</param>
    /// <param name="color">Text colour (ARGB).</param>
    /// <param name="fontSlot">
    ///     Font slot index 0..14 (default 0 = DotumChe 12/6/wt0).
    ///     spec §6.2 / §6.3 — GULabel slot at +0xE4, default 0: CODE-CONFIRMED.
    /// </param>
    /// <param name="multiline">True for multi-line autowrap labels.</param>
    public HudLabel(
        int x, int y, int w, int h,
        string text = "",
        Color? color = null,
        int fontSlot = 0, // spec: §6.2/§6.3 GULabel font slot default 0: CODE-CONFIRMED
        bool multiline = false)
    {
        _label = new FixedAdvanceLabel(fontSlot, color ?? Colors.White, multiline)
        {
            Text = text,
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            AutowrapMode = multiline
                ? TextServer.AutowrapMode.WordSmart
                : TextServer.AutowrapMode.Off,
            ClipText = !multiline,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        _label.SetFixedAdvanceColor(color ?? Colors.White);

        // Apply font slot.
        // spec: §6.3 — GULabel font slot at +0xE4, default 0 = DotumChe 12/6: CODE-CONFIRMED.
        HudFont.ApplyToLabel(_label, fontSlot);
    }

    // -------------------------------------------------------------------------
    // Public properties
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Gets or sets the label text. Pass already-decoded CP949 strings from HudTextLibrary.
    ///     Never decode bytes in the UI layer.
    /// </summary>
    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>Gets or sets the text colour.</summary>
    public Color FontColor
    {
        get => _label.FixedAdvanceColor;
        set => _label.SetFixedAdvanceColor(value);
    }

    // -------------------------------------------------------------------------
    // Fixed-advance width helper
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Computes the fixed-advance layout width for a string at the given font slot:
    ///     <c>charWidth(slot) × text.Length</c>.
    ///     spec: Docs/RE/specs/ui_system.md §6.3 — "bounding rect right = x + charWidth×strlen".
    /// </summary>
    public static int FixedAdvanceWidth(string text, int fontSlot)
    {
        return HudFont.CharWidth(fontSlot) * text.Length;
    }

    // -------------------------------------------------------------------------
    // HudWidget
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public override Control? GetControl()
    {
        return _label;
    }

    private sealed partial class FixedAdvanceLabel : Label
    {
        private readonly int _advanceWidth;
        private readonly int _cellHeight;
        private readonly Font _font;
        private readonly int _fontSize;
        private readonly bool _multiline;
        private Color _fontColor;

        public FixedAdvanceLabel(int fontSlot, Color fontColor, bool multiline)
        {
            _font = HudFont.CreateSlot(fontSlot);
            _fontSize = HudFont.RowHeight(fontSlot);
            _advanceWidth = HudFont.CharWidth(fontSlot);
            _cellHeight = HudFont.RowHeight(fontSlot);
            _multiline = multiline;
            _fontColor = fontColor;

            AddThemeFontOverride("font", _font);
            AddThemeFontSizeOverride("font_size", _fontSize);
            // Hide Label's proportional native text; _Draw below emits fixed-advance glyphs.
            // spec: Docs/RE/specs/frontend_scenes.md — labels use explicit advanceWidth per slot.
            AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0f));
        }

        public Color FixedAdvanceColor => _fontColor;

        public void SetFixedAdvanceColor(Color color)
        {
            _fontColor = color;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (string.IsNullOrEmpty(Text)) return;

            var lines = BuildLines(Text);
            float totalHeight = lines.Length * _cellHeight;
            var y = VerticalAlignment switch
            {
                VerticalAlignment.Center => Mathf.Max(0f, (Size.Y - totalHeight) * 0.5f),
                VerticalAlignment.Bottom => Mathf.Max(0f, Size.Y - totalHeight),
                _ => 0f
            };

            foreach (var line in lines)
            {
                DrawFixedLine(line, y);
                y += _cellHeight;
                if (y > Size.Y) break;
            }
        }

        private string[] BuildLines(string text)
        {
            var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            var rawLines = normalised.Split('\n');
            if (!_multiline || _advanceWidth <= 0 || Size.X <= 0f) return rawLines;

            var maxChars = Math.Max(1, (int)Math.Floor(Size.X / _advanceWidth));
            var lines = new List<string>(rawLines.Length);
            foreach (var rawLine in rawLines)
            {
                if (rawLine.Length <= maxChars)
                {
                    lines.Add(rawLine);
                    continue;
                }

                for (var offset = 0; offset < rawLine.Length; offset += maxChars)
                    lines.Add(rawLine.Substring(offset, Math.Min(maxChars, rawLine.Length - offset)));
            }

            return [.. lines];
        }

        private void DrawFixedLine(string line, float top)
        {
            float lineWidth = line.Length * _advanceWidth;
            var x = HorizontalAlignment switch
            {
                HorizontalAlignment.Center => Mathf.Max(0f, (Size.X - lineWidth) * 0.5f),
                HorizontalAlignment.Right => Mathf.Max(0f, Size.X - lineWidth),
                _ => 0f
            };

            var baseline = top + _fontSize;
            for (var i = 0; i < line.Length; i++)
            {
                var glyph = line[i].ToString();
                DrawString(_font, new Vector2(x + i * _advanceWidth, baseline), glyph,
                    HorizontalAlignment.Left, -1f, _fontSize, _fontColor);
            }
        }
    }
}