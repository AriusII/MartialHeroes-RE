namespace MartialHeroes.Client.Infrastructure.Macros;

public interface IMacroFileParser
{
    Task<IReadOnlyList<MacroDefinition>> ParseFileAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    IReadOnlyList<MacroDefinition> ParseContent(string content);
}