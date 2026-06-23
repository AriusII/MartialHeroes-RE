namespace MartialHeroes.Shared.Kernel.Ids;


[StronglyTypedId]
public readonly partial record struct PlayerId(uint Value);

[StronglyTypedId]
public readonly partial record struct MonsterId(uint Value);

[StronglyTypedId]
public readonly partial record struct ItemId(uint Value);

[StronglyTypedId]
public readonly partial record struct SkillId(uint Value);