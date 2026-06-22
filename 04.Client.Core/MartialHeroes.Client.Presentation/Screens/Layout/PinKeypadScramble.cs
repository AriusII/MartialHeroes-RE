// Screens/Layout/PinKeypadScramble.cs
//
// Engine-free data surface for the login second-password (PIN) keypad SCRAMBLE.
//
// The anti-keylogger PIN keypad shows ten 52×52 cells (5 columns × 2 rows). Each cell is an over-built
// stack of ten digit-buttons (glyphs 0..9); the scramble shows EXACTLY ONE digit-button per cell, so
// each of the ten digits 0..9 appears exactly once across the ten cells. Because the cell→digit mapping
// is randomized on every open, the on-screen position of a digit is NOT fixed — a port must track which
// digit each visible button represents via the scramble permutation, NOT infer it from a fixed slot.
//
// This type exposes that permutation (cell → digit) and the button→digit mapping the layer-05 renderer
// consumes, and reproduces the original's scramble MECHANISM faithfully:
//   - seeded from a WHOLE-SECOND wall clock (the legacy `srand(time())`, NOT a high-resolution timer):
//     two opens within the same calendar second reproduce the same layout — the host passes the floored
//     unix-second as the seed; this type takes the seed as a PARAMETER and is therefore deterministic
//     (no ambient clock / RNG), matching the Domain-determinism rule;
//   - an ASCENDING uniform permutation of digits 0..9 (the MSVC std::random_shuffle shape): a running
//     bound i walks 2..10, each step swaps the new element with index j = rand() mod i (j ∈ [0, i−1]).
//     This produces an equivalent uniform 10-element permutation. The concrete permutation is EMERGENT
//     from the seed — only the mechanism is specified; do NOT hard-code a permutation.
//
// NOTE: C#'s System.Random is NOT the MSVC CRT rand(), so the exact permutation is not byte-identical to
// the original — and must not be: the keypad is intentionally unpredictable per open. What is reproduced
// is the structure (whole-second seed, ascending uniform shuffle, one digit per cell).
//
// spec: Docs/RE/specs/frontend_layout_tables.md §3 (scramble: srand(time()) whole-second CRT seed;
//       ASCENDING uniform permutation; one digit per cell; re-roll on open/Reset/OK/Cancel; the
//       visible button's tag IS its digit).
// spec: Docs/RE/scenes/login.md §5.2 (each digit-button's tag = its digit value; scramble shows exactly
//       one digit per cell; the digit a press appends is decided by the live permutation, NOT action mod 10).

namespace MartialHeroes.Client.Presentation.Screens.Layout;

/// <summary>
///     The deterministic, engine-free scramble model for the login PIN keypad. Holds the cell → digit
///     permutation produced by a whole-second-seeded ascending uniform shuffle, and answers the two
///     questions the renderer asks: "which digit does cell <c>c</c> currently show?" and "for the visible
///     button at <c>(cell, faceDigit)</c>, what digit does pressing it append?".
///     <para>
///         Deterministic by construction: the wall-clock seed is an explicit <see cref="int" /> parameter
///         (the host floors the unix-second), so this type takes no ambient clock or RNG. Reproduce by
///         re-rolling on every keypad OPEN / Reset / OK / Cancel — never cache a permutation across opens.
///     </para>
///     spec: Docs/RE/specs/frontend_layout_tables.md §3; Docs/RE/scenes/login.md §5.2.
/// </summary>
public sealed class PinKeypadScramble
{
    // 10 cells (5×2); digits 0..9. spec: frontend_layout_tables.md §3.
    private const int CellCount = LoginLayout.PinKeypadCellCount; // 10
    private const int DigitsPerCell = LoginLayout.PinKeypadDigitsPerCell; // 10

    // _cellDigit[cell] = the digit value shown in that cell after the scramble (a permutation of 0..9).
    private readonly int[] _cellDigit = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

    /// <summary>
    ///     Creates a keypad scramble and immediately rolls it from <paramref name="wholeSecondSeed" />.
    /// </summary>
    /// <param name="wholeSecondSeed">
    ///     The whole-second wall-clock seed (the host floors the unix-second). Two scrambles built with the
    ///     same seed produce the same permutation — faithfully reproducing the legacy
    ///     <c>srand(time())</c> whole-second granularity. spec: frontend_layout_tables.md §3.
    /// </param>
    public PinKeypadScramble(int wholeSecondSeed)
    {
        Roll(wholeSecondSeed);
    }

    /// <summary>The number of keypad cells (5 columns × 2 rows = 10). spec: frontend_layout_tables.md §3.</summary>
    public int CellCountValue => CellCount;

    /// <summary>
    ///     The digit shown in cell <paramref name="cell" /> (0..9) after the current scramble. The visible
    ///     button in that cell carries this digit as its tag; pressing it appends this digit to the PIN.
    ///     spec: Docs/RE/scenes/login.md §5.2 ("the visible button's tag IS its digit").
    /// </summary>
    /// <param name="cell">Cell index 0..9 (cells 0..4 = top row, 5..9 = bottom row).</param>
    /// <returns>The digit value 0..9 currently shown in that cell.</returns>
    public int DigitForCell(int cell)
    {
        return _cellDigit[cell];
    }

    /// <summary>
    ///     Whether the digit-face button <paramref name="faceDigit" /> in cell <paramref name="cell" /> is
    ///     the one made VISIBLE by the current scramble (exactly one face per cell is visible). The
    ///     renderer builds all ten faces per cell and shows only the matching one.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §3 ("the scramble makes exactly one digit-button
    ///     visible per cell").
    /// </summary>
    /// <param name="cell">Cell index 0..9.</param>
    /// <param name="faceDigit">The digit glyph this over-built face draws (0..9; srcU = faceDigit·52).</param>
    /// <returns><see langword="true" /> when this face is the visible one for the cell.</returns>
    public bool IsFaceVisible(int cell, int faceDigit)
    {
        return (uint)cell < CellCount && _cellDigit[cell] == faceDigit;
    }

    /// <summary>
    ///     Maps a build-order keypad action id (<c>cell·10 + faceDigit</c>, range 0..99) to the digit a
    ///     press of that button appends — or <c>-1</c> if that face is NOT the visible one for its cell
    ///     (a press on a hidden face must be ignored). The digit is decided by the LIVE permutation, NOT by
    ///     <c>actionId mod 10</c>. spec: Docs/RE/scenes/login.md §5.2 ("the digit a button press appends is
    ///     decided by the keypad's own handler consulting the live scramble permutation — NOT action mod 10").
    /// </summary>
    /// <param name="actionId">Build-order action id <c>cell·10 + faceDigit</c> (0..99).</param>
    /// <returns>The digit 0..9 to append, or <c>-1</c> when the face is hidden / the id is out of range.</returns>
    public int DigitForButtonAction(int actionId)
    {
        if ((uint)actionId >= CellCount * DigitsPerCell) return -1;
        var cell = actionId / DigitsPerCell;
        var faceDigit = actionId % DigitsPerCell;
        return _cellDigit[cell] == faceDigit ? faceDigit : -1;
    }

    /// <summary>
    ///     Re-rolls the scramble in place from <paramref name="wholeSecondSeed" /> — call on every keypad
    ///     open and on Reset / OK / Cancel. spec: Docs/RE/specs/frontend_layout_tables.md §3 ("the scramble
    ///     re-seeds and re-shuffles on open, Reset, OK, and Cancel").
    /// </summary>
    /// <param name="wholeSecondSeed">The whole-second wall-clock seed (host-floored unix-second).</param>
    public void Roll(int wholeSecondSeed)
    {
        for (var i = 0; i < CellCount; i++)
            _cellDigit[i] = i;

        // ASCENDING uniform permutation (MSVC std::random_shuffle shape): bound i walks 1..9; each step
        // swaps element i with index j = rand() mod (i+1), j ∈ [0, i]. spec: frontend_layout_tables.md §3.
        var rng = new Random(wholeSecondSeed);
        for (var i = 1; i < CellCount; i++)
        {
            var j = rng.Next(0, i + 1); // j ∈ [0, i]
            (_cellDigit[i], _cellDigit[j]) = (_cellDigit[j], _cellDigit[i]);
        }
    }
}