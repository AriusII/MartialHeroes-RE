namespace MartialHeroes.Client.Application.UseCases;

public sealed class AccountCharacterState
{
    public const int MaxCount = CharacterSelectionStore.MaxSlots;

    public AccountCharacterState(int initialCount = 0)
    {
        CharacterCount = Clamp(initialCount);
    }

    public int CharacterCount { get; private set; }

    public void Set(int count)
    {
        CharacterCount = Clamp(count);
    }

    public int Increment()
    {
        CharacterCount = Clamp(CharacterCount + 1);
        return CharacterCount;
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