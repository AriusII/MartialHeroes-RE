using MartialHeroes.Client.Domain.Actors.Actors;

namespace MartialHeroes.Client.Domain.Actors.Locomotion;

public static class MotionFinish
{
    public static MoveScale ResolveFinishScale(bool isLocalPlayer, EntitySort sort)
    {
        if (isLocalPlayer) return MoveScale.Default;

        return sort == EntitySort.PlayerCharacter
            ? MoveScale.RemotePlayerFinish
            : MoveScale.RemoteNonPlayerFinish;
    }

    public static bool EmitsMotionEnd(bool isLocalPlayer)
    {
        return isLocalPlayer;
    }
}
