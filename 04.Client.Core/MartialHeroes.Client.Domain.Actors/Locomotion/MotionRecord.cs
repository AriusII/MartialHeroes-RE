namespace MartialHeroes.Client.Domain.Actors.Locomotion;

public readonly record struct MotionRecord(
    long WalkRateRawPerSecond,
    long RunRateRawPerSecond,
    int WalkClipId,
    int RunClipId)
{
    public long SelectRateRawPerSecond(byte runFlag)
    {
        return runFlag == 1 ? RunRateRawPerSecond : WalkRateRawPerSecond;
    }

    public int SelectClipId(byte runFlag)
    {
        return runFlag == 1 ? RunClipId : WalkClipId;
    }
}
