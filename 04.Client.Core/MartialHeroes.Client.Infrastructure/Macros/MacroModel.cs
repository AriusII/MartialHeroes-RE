namespace MartialHeroes.Client.Infrastructure.Macros;

/// <summary>
/// A single parsed macro: a named slot, an optional trigger key, and an ordered
/// list of command strings the client executes in sequence when the macro fires.
/// </summary>
/// <param name="Name">
/// The macro's logical name, unique within a file. Must be non-empty.
/// </param>
/// <param name="TriggerKey">
/// Optional hotkey string (e.g. "F5", "Ctrl+1"). <see langword="null"/> means
/// the macro is bound to no key and must be triggered programmatically.
/// </param>
/// <param name="Commands">
/// Ordered list of command strings. Each entry is a single action line exactly
/// as it appeared in the macro file after comment/blank stripping.
/// The list is read-only and never null; it may be empty.
/// </param>
public sealed record MacroDefinition(
    string Name,
    string? TriggerKey,
    IReadOnlyList<string> Commands);
