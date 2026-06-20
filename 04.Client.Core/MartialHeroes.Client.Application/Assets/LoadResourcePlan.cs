namespace MartialHeroes.Client.Application.Assets;

/// <summary>
///     The fixed, compiled state-2 boot-load corpus the original worker registers in a single hardcoded
///     sequence (not a scene manifest). This is the full file-registration spine in its authoritative
///     registration ORDER; the order is load-bearing. Filename quirks (<c>musajung.do</c>,
///     <c>items_extra.do</c>, <c>discript.sc</c> with extension <c>.sc</c>, <c>Tutor.scr</c> capitalised)
///     are intentional spellings in the shipped data set and are preserved verbatim.
///     spec: Docs/RE/specs/resource_pipeline.md §2.1a.
/// </summary>
public static class LoadResourcePlan
{
    public const string MessageCataloguePath = "data/script/msg.xdb"; // spec: Docs/RE/specs/resource_pipeline.md §2.2.

    /// <summary>
    ///     The 48-entry boot data-table corpus in registration order. Loaded existence-aware via
    ///     <see cref="ILoadResourceSource" />: an absent VFS entry warns-and-continues (contributes
    ///     zero bytes), never throws. Entry #48 of the spec is the effect-manifest chain that follows
    ///     <c>bmplist.lst</c>. spec: Docs/RE/specs/resource_pipeline.md §2.1a.
    /// </summary>
    public static readonly string[] BootWorkerPaths =
    [
        "data/script/events.scr", // #1  spec: Docs/RE/specs/resource_pipeline.md §2.1a.
        "data/script/system_control.scr", // #2
        "data/script/mapsetting.scr", // #3
        "data/script/playtime_reward.scr", // #4
        "data/script/items.scr", // #5
        "data/script/skills.scr", // #6
        "data/script/musajung.do", // #7  quirk: stance/"do" table
        "data/script/skillcategory.scr", // #8
        "data/script/users.scr", // #9
        "data/script/products.scr", // #10
        "data/script/productcollect.scr", // #11
        "data/script/productrandname.scr", // #12
        "data/script/helps.scr", // #13
        "data/script/npc.scr", // #14
        "data/script/npcs.scr", // #15
        "data/item/items_extra.do", // #16 quirk: extra-items "do" table
        "data/script/mobs.scr", // #17
        "data/script/repair.scr", // #18
        "data/script/upgradeitems.scr", // #19
        "data/script/quests.scr", // #20
        "data/script/emoticon.do", // #21
        "data/script/textcommand.do", // #22
        "data/script/chivalry.scr", // #23
        "data/script/letters.scr", // #24
        "data/script/nicktofame.scr", // #25
        "data/script/guildcrest.scr", // #26
        "data/script/discript.sc", // #27 quirk: extension .sc (NOT .scr)
        "data/script/tiphelp.scr", // #28
        "data/script/setitemname.scr", // #29
        "data/script/oblist.scr", // #30
        "data/script/citems.scr", // #31
        "data/script/Tutor.scr", // #32 quirk: capital T
        "data/script/warstoneinfo.scr", // #33
        "data/script/statue.scr", // #34
        "data/script/skillneedset.scr", // #35
        "data/script/viplevels.scr", // #36
        "data/script/itemscale.scr", // #37
        "data/script/itemeffect.scr", // #38
        "data/ui/UiTex.txt", // #39
        "data/item/skinlist.txt", // #40
        "data/char/sameemoticon.txt", // #41
        "data/ui/guildicon/crestlist.txt", // #42
        "data/script/effectscale.xdb", // #43
        "data/script/creature_item.xdb", // #44
        "data/script/vehicle.xdb", // #45
        "data/script/buff_icon_position.xdb", // #46
        "data/effect/bmplist.lst", // #47 effect texture pool list (§3A.2(b))
        // #48 effect-manifest chain after bmplist.lst (xobj/xeffect + effect-cache prime, totalmugong).
        "data/effect/xobj.lst", // #48 (chain)
        "data/effect/xeffect.lst", // #48 (chain)
        "data/effect/effect.cache", // #48 (chain) effect-cache prime
        "data/effect/totalmugong.txt" // #48 (chain)
    ];
}