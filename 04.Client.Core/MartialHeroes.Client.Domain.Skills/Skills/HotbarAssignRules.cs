namespace MartialHeroes.Client.Domain.Skills.Skills;

public readonly record struct HotbarOccupant(uint GlobalCategory, byte TierByte, bool Occupied);

public static class HotbarAssignRules
{
    public static bool CanAssign(in SkillDefinition candidate, ReadOnlySpan<HotbarOccupant> occupants)
    {
        if (candidate.GlobalCategory == 0) return true;

        for (var i = 0; i < occupants.Length; i++)
        {
            var occupant = occupants[i];
            if (!occupant.Occupied) continue;

            if (occupant.GlobalCategory != candidate.GlobalCategory) continue;

            if (candidate.TierByte < occupant.TierByte) return false;
        }

        return true;
    }
}