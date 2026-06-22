namespace MartialHeroes.Client.Application.Assets;

/// <summary>
///     The fixed, compiled state-2 boot-load corpus the original worker registers in a single hardcoded
///     sequence (not a scene manifest). This is the full file-registration spine in its authoritative
///     registration ORDER; the order is load-bearing. Filename quirks (<c>items_extra.do</c>,
///     <c>discript.sc</c> with extension <c>.sc</c>, <c>Tutor.scr</c> capitalised) are intentional
///     spellings in the shipped data set and are preserved verbatim.
///     spec: Docs/RE/specs/resource_pipeline.md §2.1a; Docs/RE/scenes/load.md §6.
/// </summary>
public static class LoadResourcePlan
{
    public const string MessageCataloguePath = "data/script/msg.xdb"; // spec: Docs/RE/specs/resource_pipeline.md §2.2.

    /// <summary>
    ///     The boot data-table corpus in authoritative worker call ORDER (the file-bearing steps of the
    ///     ≈57-step boot worker — §2.1a; the interleaved subsystem-init steps resolve their own paths and
    ///     carry no single file path). Loaded existence-aware via <see cref="ILoadResourceSource" />: an
    ///     absent VFS entry warns-and-continues (contributes zero bytes), never throws.
    ///     Notes faithful to §2.1a: <c>repair.scr</c> is registered twice (#17 and #19) and
    ///     <c>discript.sc</c> is registered twice (#22 and #28) — both repeats are kept because the
    ///     registration order is load-bearing. <c>musajung.do</c>/<c>emoticon.do</c> are NOT worker
    ///     steps — they live in the path-global table and are consumed by the step-8 stance loader and
    ///     downstream, not by this top-level list (§2.1a path-global note). The effect-manifest chain
    ///     (bmplist.lst → xobj.lst → xeffect.lst → effect.cache → totalmugong.txt) runs after the four
    ///     <c>.xdb</c> tables, immediately before the worker's completion handshake.
    ///     spec: Docs/RE/specs/resource_pipeline.md §2.1a.
    /// </summary>
    public static readonly string[] BootWorkerPaths =
    [
        "data/script/events.scr", // #1  spec: Docs/RE/specs/resource_pipeline.md §2.1a.
        "data/script/system_control.scr", // #2
        "data/script/mapsetting.scr", // #3
        "data/script/playtime_reward.scr", // #4
        "data/script/items.scr", // #5
        "data/script/skills.scr", // #6
        // #7 skill-icon manifest parse — subsystem step, no single file path.
        "data/script/skillcategory.scr", // #8 skill-category / stance "do"-table streaming load
        "data/item/items_extra.do", // #9  quirk: extra-items "do" table under data/item/
        "data/script/users.scr", // #10 stat-curve family head (userlevel/userpoint/exp loaded by §2.7 loader)
        "data/script/products.scr", // #11
        "data/script/productcollect.scr", // #12
        "data/script/productrandname.scr", // #13
        "data/script/helps.scr", // #14
        "data/script/npc.scr", // #15
        "data/script/npcs.scr", // #16
        "data/script/repair.scr", // #17
        "data/script/mobs.scr", // #18 mob stat catalogue (§2.8)
        "data/script/repair.scr", // #19 repair table re-registered (registration order is load-bearing)
        "data/script/upgradeitems.scr", // #20
        "data/script/quests.scr", // #21
        "data/script/discript.sc", // #22 quirk: extension .sc (NOT .scr)
        "data/script/textcommand.do", // #23
        "data/script/chivalry.scr", // #24
        "data/script/letters.scr", // #25
        "data/script/nicktofame.scr", // #26
        "data/script/guildcrest.scr", // #27
        "data/script/discript.sc", // #28 descript table re-registered (registration order is load-bearing)
        "data/script/tiphelp.scr", // #29
        "data/script/setitemname.scr", // #30
        "data/script/oblist.scr", // #31
        "data/script/citems.scr", // #32
        "data/script/Tutor.scr", // #33 quirk: capital T
        "data/script/warstoneinfo.scr", // #34
        "data/script/statue.scr", // #35
        "data/script/skillneedset.scr", // #36
        "data/script/viplevels.scr", // #37
        "data/script/itemscale.scr", // #38
        "data/script/itemeffect.scr", // #39
        "data/ui/UiTex.txt", // #40 UI-texture manifest (UI id pool, §3A.2(a))
        // #41-47 subsystem-init / manifest steps (UI focus, banned-word, shadow-manager, effect-manager, …) — no single file path.
        "data/item/skinlist.txt", // #48 item skin-list table
        // #49-51 subsystem-init steps (terrain-manager init, character-visual manifest, …) — no single file path.
        "data/char/sameemoticon.txt", // #52 same-emoticon table
        "data/ui/guildicon/crestlist.txt", // #53 guild-crest icon list + pool
        "data/script/effectscale.xdb", // #54 effect-scale table
        "data/script/creature_item.xdb", // #55 creature-item table
        "data/script/vehicle.xdb", // #56 vehicle table
        "data/script/buff_icon_position.xdb", // #57 buff-icon-position table
        // Effect-manifest chain (after the effect-manager init, before the completion handshake):
        // bmplist.lst then xobj.lst / xeffect.lst (+ effect-cache prime) / totalmugong.txt. §2.1a + specs/effects.md §3.
        "data/effect/bmplist.lst", // effect texture pool list (§3A.2(b))
        "data/effect/xobj.lst", // effect-manifest chain
        "data/effect/xeffect.lst", // effect-manifest chain
        "data/effect/effect.cache", // effect-manifest chain — effect-cache prime
        "data/effect/totalmugong.txt" // effect-manifest chain
    ];
}