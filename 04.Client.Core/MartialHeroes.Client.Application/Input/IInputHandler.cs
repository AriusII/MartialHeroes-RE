namespace MartialHeroes.Client.Application.Input;

public interface IInputHandler
{
    bool TryHandle(in InputEvent e);
}