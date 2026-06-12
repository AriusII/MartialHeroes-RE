namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One parsed row from <c>data/char/actormotion.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// The file is a TAB-separated text table (CP949).  Line 0 is the record count.
/// Each data line has at least 22 columns (col[0]..col[21]).
/// </para>
/// <para>
/// Resolution chain:
/// <list type="number">
/// <item><description>
///   <c>mob_id</c> (u16 @ offset 0 in a <c>mob*.arr</c> spawn record;
///   spec: Docs/RE/formats/npc_spawns.md) equals <see cref="ActorClassId"/>.
/// </description></item>
/// <item><description>
///   <see cref="SkinClassId"/> equals the numeric g-id of the skeleton file at
///   <c>data/char/bind/g{SkinClassId}.bnd</c>
///   (spec: Docs/RE/formats/mesh.md §.bnd Header, actor_id field).
/// </description></item>
/// <item><description>
///   The associated skinned mesh (<c>.skn</c>) is the <b>first entry</b> in
///   <c>data/char/skinlist.txt</c> whose parsed <c>id_b</c> field
///   (spec: Docs/RE/formats/mesh.md §.skn Header, id_b at +4) equals
///   <see cref="SkinClassId"/>.  Confirmed: <c>id_b == SkinClassId</c> for all
///   5 spot-checked triples (mob_ids 11, 12, 21, 31, 171).
/// </description></item>
/// </list>
/// </para>
/// <para>
/// Coverage (map001 sample): 42/45 non-zero mob_ids resolve to a full triple;
/// 3 entries (skin_class 2046, 2055, 2056) have no matching .bnd/.skn in the
/// VFS — these are expected missing-asset gaps in the preserved client files.
/// </para>
/// </remarks>
public sealed class ActormotionEntry
{
    /// <summary>
    /// Actor-class identifier.  Equals <c>mob_id</c> from a spawn record in
    /// <c>data/map{NNN}/mob{NNN}.arr</c>.
    /// <br/>Column index: 1 (0-based).
    /// <br/>Observed range in VFS: 1..998.
    /// </summary>
    public int ActorClassId { get; }

    /// <summary>
    /// Skin-class identifier.  Equals the numeric g-id of the skeleton file at
    /// <c>data/char/bind/g{SkinClassId}.bnd</c>.  Also used as the <c>id_b</c> key
    /// when locating the body <c>.skn</c> mesh via a reverse scan of
    /// <c>data/char/skinlist.txt</c>.
    /// <br/>Column index: 2 (0-based).
    /// <br/>Observed range in VFS: 1..8892.
    /// </summary>
    public int SkinClassId { get; }

    /// <summary>
    /// Composite animation IDs for this actor's motion bank, sourced from
    /// columns 15..21 of the row.  These map to <c>data/char/mot/g{id}.mot</c>
    /// files via <c>data/char/motlist.txt</c>.  Unused entries are 0.
    /// Length is always 7 (columns 15, 16, 17, 18, 19, 20, 21).
    /// </summary>
    public int[] MotionIds { get; }

    /// <summary>Convenience: <c>data/char/bind/g{SkinClassId}.bnd</c> virtual path.</summary>
    public string BndVfsPath => $"data/char/bind/g{SkinClassId}.bnd";

    public ActormotionEntry(int actorClassId, int skinClassId, int[] motionIds)
    {
        ActorClassId = actorClassId;
        SkinClassId = skinClassId;
        MotionIds = motionIds;
    }
}