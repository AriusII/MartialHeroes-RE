namespace MartialHeroes.Client.Application.Login;

public interface ILastServerStore
{
    void Save(ushort serverId);

    ushort Load();
}