namespace MartialHeroes.Client.Domain.Stats.Stats;

public static class ClassHpTable
{
    private static readonly double[] Multipliers = [0.0, 0.3, 0.2, 0.15, 0.1];

    public static int Length => Multipliers.Length;

    public static double MultiplierFor(byte classId)
    {
        return classId < Multipliers.Length ? Multipliers[classId] : 0.0;
    }
}