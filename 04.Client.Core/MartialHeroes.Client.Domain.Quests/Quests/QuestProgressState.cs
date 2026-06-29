namespace MartialHeroes.Client.Domain.Quests.Quests;

public enum QuestProgressState : byte
{
    NotTracked = 0,
    Available = 1,
    Active = 2,
    Completable = 3,
    Granted = 4,
    Denied = 5,
}
