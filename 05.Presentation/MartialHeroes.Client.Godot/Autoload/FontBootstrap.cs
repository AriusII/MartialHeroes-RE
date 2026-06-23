using Godot;

namespace MartialHeroes.Client.Godot.Autoload;

public sealed partial class FontBootstrap : Node
{
    private static readonly string[] KoreanFaceNames =
    [
        "Dotum",
        "DotumChe",
        "Gulim",
        "GulimChe",
        "Malgun Gothic",
        "Batang",
        "BatangChe"
    ];


    public override void _Ready()
    {
        try
        {
            InstallKoreanFallbackFont();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FontBootstrap] Failed to install Korean fallback font: {ex.Message}");
        }
    }


    private static void InstallKoreanFallbackFont()
    {
        var sysFont = new SystemFont();

        sysFont.FontNames = KoreanFaceNames;

        sysFont.Antialiasing = TextServer.FontAntialiasing.Lcd;

        sysFont.Hinting = TextServer.Hinting.None;

        ThemeDB.Singleton.FallbackFont = sysFont;

        ThemeDB.Singleton.FallbackFontSize = 12;

        GD.Print("[FontBootstrap] Korean SystemFont installed as ThemeDB.FallbackFont. " +
                 $"Faces: [{string.Join(", ", KoreanFaceNames)}]. " +
                 "spec: Docs/RE/specs/ui_system.md §6.2");
    }
}