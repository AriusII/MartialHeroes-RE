namespace MartialHeroes.Client.Application.Assets;

/// <summary>
/// The fixed, compiled state-2 boot-load corpus slice currently exposed to the clean-room port. The
/// original worker pulls a hardcoded table set, not a scene manifest. spec: Docs/RE/specs/resource_pipeline.md §2.1.
/// </summary>
public static class LoadResourcePlan
{
    public const string MessageCataloguePath = "data/script/msg.xdb"; // spec: Docs/RE/specs/resource_pipeline.md §2.2.

    public static readonly string[] BootWorkerPaths =
    [
        "data/ui/UiTex.txt", // spec: Docs/RE/specs/resource_pipeline.md §3A.2(a).
        "data/effect/bmplist.lst", // spec: Docs/RE/specs/resource_pipeline.md §3A.2(b).
        "data/effect/xobj.lst", // spec: Docs/RE/specs/resource_pipeline.md §3A.2(b).
        "data/effect/xeffect.lst", // spec: Docs/RE/specs/resource_pipeline.md §3A.2(b).
        "data/effect/effect.cache", // spec: Docs/RE/specs/resource_pipeline.md §3A.2(b).
        "data/effect/totalmugong.txt", // spec: Docs/RE/specs/resource_pipeline.md §3A.2(b).
        "data/item/skinlist.txt", // spec: Docs/RE/specs/resource_pipeline.md §2.1.
        "data/char/sameemoticon.txt", // spec: Docs/RE/specs/resource_pipeline.md §2.1.
        "data/char/actormotion.txt", // spec: Docs/RE/specs/resource_pipeline.md §2.1.
    ];
}