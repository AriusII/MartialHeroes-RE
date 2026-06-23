namespace MartialHeroes.Client.Application.Net;

public sealed class InFlightLatch
{
    public bool IsArmed { get; private set; }

    public void Arm()
    {
        IsArmed = true;
    }

    public void Clear()
    {
        IsArmed = false;
    }
}