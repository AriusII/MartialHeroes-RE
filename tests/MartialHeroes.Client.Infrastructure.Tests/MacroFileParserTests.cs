using MartialHeroes.Client.Infrastructure.Exceptions;
using MartialHeroes.Client.Infrastructure.Macros;
using Xunit;

namespace MartialHeroes.Client.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="MacroFileParser"/>.
/// spec: Docs/RE/formats/macro_file.md (project-owned format, not reverse-engineered).
/// </summary>
public sealed class MacroFileParserTests
{
    private static readonly MacroFileParser Parser = new();

    // ── Happy-path ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseContent_FullExample_ProducesExpectedMacros()
    {
        const string input =
            """
            # My keybinds - last edited 2026-06-11

            [SkillAttack] F1
            UseSkill 101
            PlayAnimation attack_heavy

            [OpenInventory] I
            ToggleWindow Inventory

            [Greeting] None
            # This macro has no hotkey.
            EmoteChat /wave
            EmoteChat /bow

            [EmptyMacro] F10
            # No commands here yet.
            """;

        var macros = Parser.ParseContent(input);

        Assert.Equal(4, macros.Count);

        var skillAttack = macros[0];
        Assert.Equal("SkillAttack", skillAttack.Name);
        Assert.Equal("F1", skillAttack.TriggerKey);
        Assert.Equal(["UseSkill 101", "PlayAnimation attack_heavy"], skillAttack.Commands);

        var openInv = macros[1];
        Assert.Equal("OpenInventory", openInv.Name);
        Assert.Equal("I", openInv.TriggerKey);
        Assert.Equal(["ToggleWindow Inventory"], openInv.Commands);

        var greeting = macros[2];
        Assert.Equal("Greeting", greeting.Name);
        Assert.Equal("None", greeting.TriggerKey);
        Assert.Equal(["EmoteChat /wave", "EmoteChat /bow"], greeting.Commands);

        var empty = macros[3];
        Assert.Equal("EmptyMacro", empty.Name);
        Assert.Equal("F10", empty.TriggerKey);
        Assert.Empty(empty.Commands);
    }

    [Fact]
    public void ParseContent_NoTriggerKey_StoresNullTriggerKey()
    {
        const string input =
            """
            [MyMacro]
            DoSomething
            """;

        var macros = Parser.ParseContent(input);

        var m = Assert.Single(macros);
        Assert.Null(m.TriggerKey);
        Assert.Equal(["DoSomething"], m.Commands);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseContent_BlankAndCommentLines_AreIgnored()
    {
        const string input =
            """
            # Top-level comment

            [Alpha] A

            # Mid-macro comment

            CmdOne

            # Another comment
            CmdTwo

            """;

        var macros = Parser.ParseContent(input);

        var m = Assert.Single(macros);
        Assert.Equal("Alpha", m.Name);
        Assert.Equal("A", m.TriggerKey);
        Assert.Equal(["CmdOne", "CmdTwo"], m.Commands);
    }

    [Fact]
    public void ParseContent_EmptyString_ReturnsEmptyList()
    {
        var macros = Parser.ParseContent(string.Empty);
        Assert.Empty(macros);
    }

    [Fact]
    public void ParseContent_OnlyComments_ReturnsEmptyList()
    {
        const string input =
            """
            # just a comment
            # another comment
            """;

        var macros = Parser.ParseContent(input);
        Assert.Empty(macros);
    }

    [Fact]
    public void ParseContent_LinesBeforeFirstHeader_AreIgnored()
    {
        const string input =
            """
            orphan line before any header
            [Valid] V
            Cmd
            """;

        var macros = Parser.ParseContent(input);

        var m = Assert.Single(macros);
        Assert.Equal("Valid", m.Name);
        Assert.Equal(["Cmd"], m.Commands);
    }

    [Fact]
    public void ParseContent_DuplicateName_LastDefinitionWins()
    {
        // spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 7
        const string input =
            """
            [Attack] F1
            OldCommand

            [Attack] F2
            NewCommand
            """;

        var macros = Parser.ParseContent(input);

        // Only one macro should be returned (last definition).
        var m = Assert.Single(macros);
        Assert.Equal("Attack", m.Name);
        Assert.Equal("F2", m.TriggerKey);
        Assert.Equal(["NewCommand"], m.Commands);
    }

    [Fact]
    public void ParseContent_CrLfLineEndings_ParsedIdentically()
    {
        // spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 2
        const string input = "[Move] W\r\nWalkForward\r\n";

        var macros = Parser.ParseContent(input);

        var m = Assert.Single(macros);
        Assert.Equal("Move", m.Name);
        Assert.Equal("W", m.TriggerKey);
        Assert.Equal(["WalkForward"], m.Commands);
    }

    [Fact]
    public void ParseContent_WhitespaceAroundMacroName_IsTrimmed()
    {
        const string input = "[  MyMacro  ] F5\nCmd\n";

        var macros = Parser.ParseContent(input);

        var m = Assert.Single(macros);
        Assert.Equal("MyMacro", m.Name); // trimmed
    }

    [Fact]
    public void ParseContent_CommandLeadingAndTrailingWhitespace_IsTrimmed()
    {
        const string input = "[M] F1\n   indented command   \n";

        var macros = Parser.ParseContent(input);

        var m = Assert.Single(macros);
        Assert.Equal(["indented command"], m.Commands);
    }

    [Fact]
    public void ParseContent_MacroWithNoCommands_ReturnsEmptyCommandList()
    {
        const string input = "[Empty] F9\n";

        var macros = Parser.ParseContent(input);

        var m = Assert.Single(macros);
        Assert.Equal("Empty", m.Name);
        Assert.Empty(m.Commands);
    }

    // ── Disk I/O ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFileAsync_ValidFile_ParsesCorrectly()
    {
        var path = Path.GetTempFileName() + ".mhm";
        try
        {
            await File.WriteAllTextAsync(path, "[Jump] Space\nJumpAction\n");

            var macros = await Parser.ParseFileAsync(path);

            var m = Assert.Single(macros);
            Assert.Equal("Jump", m.Name);
            Assert.Equal("Space", m.TriggerKey);
            Assert.Equal(["JumpAction"], m.Commands);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ParseFileAsync_NonExistentFile_ThrowsMacroFileException()
    {
        await Assert.ThrowsAsync<MacroFileException>(() =>
            Parser.ParseFileAsync(@"C:\does\not\exist\ghost.mhm"));
    }
}