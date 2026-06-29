namespace MartialHeroes.Assets.Parsers.Character.Models;

public sealed class ActormotionEntry
{
    public uint MotionKey { get; init; }

    public int Col0Category { get; init; }

    public int Col1RawOffset { get; init; }

    public int IntA { get; init; }

    public float RateSrcX { get; init; }

    public float RateSrcY { get; init; }

    public int IntB { get; init; }

    public float FloatC { get; init; }

    public float FloatD { get; init; }

    public float FloatE { get; init; }

    public float FloatF { get; init; }

    public float FloatG { get; init; }

    public int DivisorX { get; init; }

    public int DivisorY { get; init; }

    public float RateX { get; init; }

    public float RateY { get; init; }

    public float FloatH { get; init; }

    public float FloatI { get; init; }

    public IReadOnlyList<int> MotionIdsA { get; init; } = [];

    public IReadOnlyList<int> MotionIdsB { get; init; } = [];

    public IReadOnlyList<int> MotionClipIds => MotionIdsA;

    public IReadOnlyList<int> SfxEventIds => MotionIdsB;

    public int IdleMotionId =>
        MotionIdsA.Count > 1
            ? MotionIdsA[1]
            : 0;

    public int WalkMotionId =>
        MotionIdsA.Count > 2
            ? MotionIdsA[2]
            : 0;

    public int RunMotionId =>
        MotionIdsA.Count > 3
            ? MotionIdsA[3]
            : 0;

    public int DeathMotionId =>
        MotionIdsA.Count > 4
            ? MotionIdsA[4]
            : 0;

    public int SkinClassId => IntA;
}