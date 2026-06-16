// Screens/ClassAppearanceResolver.cs
//
// ONE shared class -> base-skin (.skn) resolver for BOTH 3D front-end screens (character-select
// and character-create). Before this resolver existed, CharSelectScene3D and CharCreatePreview3D
// each hard-coded a DIFFERENT class->mesh table, and the create screen invented stems
// (g202220001 / g202130001 / g202140001) that are absent from the VFS — so classes 2/3/4 rendered
// nothing in create. This unifies both screens onto the §3.7.5-CONFIRMED four starter meshes so
// the SAME class shows the SAME body in both views.
//
// spec: Docs/RE/specs/frontend_scenes.md §3.7.5 — the four confirmed-present starter meshes
//       (all default appearance IdA=1):
//         class 1 -> g202110001 (Bichimi / Dosa)
//         class 2 -> g203110001 (Monk)
//         class 3 -> g209110001 (Archer)
//         class 4 -> g206110001 (Sorceress / Summoner)
// spec: Docs/RE/specs/skinning.md §3.5.2/§3.5.3 — the FULL appearance chain (model_class_id selector
//       + skin.txt appearance catalogue) is the original's real mechanism; the categoryBase[] value
//       edge (§3.5.5) is a pending live-debugger value-edge, so this confirmed-mesh table is the
//       spec-grounded STOPGAP per frontend_scenes.md §3.7.5.
//
// SPEC GAP (reported): the create-form internal-class (1..4) -> mesh binding is not pinned in any
//       clean spec — §3.7.5 is keyed by the SELECT class tags {3,4,6,11}, a different keying. Until
//       that catalogue edge is promoted, both screens share THIS confirmed table so they at least
//       agree and every class renders a real, present mesh. spec-gap noted in the divergence ledger.

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Resolves a class id (1..4) to its base-skin <c>.skn</c> VFS path, shared by the 3D
/// character-select and character-create screens so a class shows the identical body in both.
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.7.5 (the four confirmed starter meshes) /
///       Docs/RE/specs/skinning.md §3.5.2 (appearance-chain selector — the real mechanism the
///       confirmed table stands in for until §3.5.5's categoryBase[] edge is promoted).
/// </summary>
internal static class ClassAppearanceResolver
{
    /// <summary>
    /// Returns the base-skin <c>.skn</c> path for class id 1..4, or <c>null</c> for any other id
    /// (the caller logs + skips — never substitutes a wrong-class mesh). The four meshes are exactly
    /// the §3.7.5 confirmed-present starter set; each mesh carries its OWN id_b which then drives its
    /// rig (<c>g{id_b}.bnd</c>) and idle clip (actormotion col15).
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7.5.
    /// </summary>
    public static string? SknPathForClass(int classId) => classId switch
    {
        1 => "data/char/skin/g202110001.skn", // §3.7.5 Bichimi / Dosa starter mesh
        2 => "data/char/skin/g203110001.skn", // §3.7.5 Monk starter mesh
        3 => "data/char/skin/g209110001.skn", // §3.7.5 Archer starter mesh
        4 => "data/char/skin/g206110001.skn", // §3.7.5 Sorceress / Summoner starter mesh
        _ => null, // unknown class -> caller logs + skips (no fallback)
    };

    /// <summary>
    /// Candidate list form (single confirmed mesh) for the select-screen path that probes
    /// <c>assets.Contains</c> over a candidate set. Returns an empty array for an unknown id.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7.5.
    /// </summary>
    public static string[] SknCandidatesForClass(int classId)
    {
        string? path = SknPathForClass(classId);
        return path is null ? [] : [path];
    }
}