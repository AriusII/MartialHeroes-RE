namespace MartialHeroes.Client.Application.UseCases;

public sealed class AccountCharacterState(int initialCount = 0)
{
    public const int MaxCount = CharacterSelectionStore.MaxSlots;

    public int CharacterCount { get; private set; } = Clamp(initialCount);

    public void Set(int count)
    {
        CharacterCount = Clamp(count);
    }

    public int Decrement()
    {
        CharacterCount = Clamp(CharacterCount - 1);
        return CharacterCount;
    }

    private static int Clamp(int value)
    {
        return Math.Clamp(value, 0, MaxCount);
    }
}