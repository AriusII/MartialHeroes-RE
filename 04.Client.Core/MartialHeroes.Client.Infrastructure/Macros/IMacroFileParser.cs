namespace MartialHeroes.Client.Infrastructure.Macros;

/// <summary>
/// Parses a Martial Heroes client macro file into an in-memory collection of
/// <see cref="MacroDefinition"/> objects.
/// <para>
/// The macro file format is OUR format — not a legacy reverse-engineered layout.
/// See <c>Docs/RE/formats/macro_file.md</c> for the canonical spec. The grammar
/// is intentionally line-based and human-readable so players can hand-edit files.
/// </para>
/// </summary>
public interface IMacroFileParser
{
    /// <summary>
    /// Parses the macro file at <paramref name="filePath"/> from disk and returns
    /// all valid macro definitions found in it.
    /// </summary>
    /// <param name="filePath">Absolute path to the <c>.mhm</c> macro file.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// Ordered list of <see cref="MacroDefinition"/> records in file-declaration order.
    /// Empty list if the file contains no macros.
    /// </returns>
    /// <exception cref="Exceptions.MacroFileException">
    /// Thrown when the file cannot be read from disk.
    /// </exception>
    Task<IReadOnlyList<MacroDefinition>> ParseFileAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses macro definitions from an already-loaded string (e.g. from a test
    /// fixture or an in-memory buffer). Does not perform any disk I/O.
    /// </summary>
    /// <param name="content">Full text content of a macro file.</param>
    /// <returns>
    /// Ordered list of <see cref="MacroDefinition"/> records.
    /// </returns>
    IReadOnlyList<MacroDefinition> ParseContent(string content);
}