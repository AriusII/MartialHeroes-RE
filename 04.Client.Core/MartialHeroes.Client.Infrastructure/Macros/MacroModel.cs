namespace MartialHeroes.Client.Infrastructure.Macros;

public sealed record MacroDefinition(
    string Name,
    string? TriggerKey,
    IReadOnlyList<string> Commands);