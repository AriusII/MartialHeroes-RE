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

    public int[] DirArray1 { get; init; } = [];

    public int[] DirArray2 { get; init; } = [];


    public int[] MotionClipIds => DirArray1;

    public int IdleMotionId =>
        DirArray1.Length > 1
            ? DirArray1[1]
            : 0;

    public int[] SfxEventIds => DirArray2;

    public string BndVfsPath =>
        $"data/char/bind/g{IntA}.bnd";


    public int ActorClassId => Col1RawOffset;

    public int SkinClassId => IntA;

    public int[] MotionIds => DirArray1.Length >= 7
        ? DirArray1[..7]
        : DirArray1;
}