namespace MartialHeroes.Client.Presentation.Screens.Layout;

public sealed class PinKeypadScramble
{
    private const int CellCount = LoginLayout.PinKeypadCellCount;
    private const int DigitsPerCell = LoginLayout.PinKeypadDigitsPerCell;

    private readonly int[] _cellDigit = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

    public PinKeypadScramble(int wholeSecondSeed)
    {
        Roll(wholeSecondSeed);
    }

    public int CellCountValue => CellCount;

    public int DigitForCell(int cell)
    {
        return _cellDigit[cell];
    }

    public bool IsFaceVisible(int cell, int faceDigit)
    {
        return (uint)cell < CellCount && _cellDigit[cell] == faceDigit;
    }

    public int DigitForButtonAction(int actionId)
    {
        if ((uint)actionId >= CellCount * DigitsPerCell) return -1;
        var cell = actionId / DigitsPerCell;
        var faceDigit = actionId % DigitsPerCell;
        return _cellDigit[cell] == faceDigit ? faceDigit : -1;
    }

    public void Roll(int wholeSecondSeed)
    {
        for (var i = 0; i < CellCount; i++)
            _cellDigit[i] = i;

        var rng = new Random(wholeSecondSeed);
        for (var i = 1; i < CellCount; i++)
        {
            var j = rng.Next(0, i + 1);
            (_cellDigit[i], _cellDigit[j]) = (_cellDigit[j], _cellDigit[i]);
        }
    }
}